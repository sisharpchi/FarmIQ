using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FarmIQ.Application.Abstractions;
using FarmIQ.Application.Contracts;
using FarmIQ.Infrastructure.Configuration;
using FarmIQ.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FarmIQ.Infrastructure.Services;

public sealed class OpenAiSpeechToTextService(
    IHttpClientFactory httpClientFactory,
    IOptions<OpenAIOptions> options,
    ILogger<OpenAiSpeechToTextService> logger) : ISpeechToTextService
{
    private readonly MockSpeechToTextService _fallback = new();

    public async Task<string?> TranscribeAsync(IEnumerable<InboundMediaDto> media, CancellationToken cancellationToken = default)
    {
        var voice = media.FirstOrDefault();
        if (voice is null)
        {
            return null;
        }

        if (!options.Value.Enabled || string.IsNullOrWhiteSpace(options.Value.ApiKey))
        {
            return await _fallback.TranscribeAsync(media, cancellationToken);
        }

        var sourcePath = voice.StoragePath;
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return await _fallback.TranscribeAsync(media, cancellationToken);
        }

        try
        {
            using var stream = File.OpenRead(sourcePath);
            using var content = new MultipartFormDataContent
            {
                { new StringContent(options.Value.TranscriptionModel), "model" },
                { new StreamContent(stream), "file", Path.GetFileName(sourcePath) }
            };

            var client = CreateClient(nameof(OpenAiSpeechToTextService));
            using var response = await client.PostAsync($"{options.Value.BaseUrl.TrimEnd('/')}/audio/transcriptions", content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("OpenAI transcription returned status {StatusCode}. Falling back to mock transcription.", response.StatusCode);
                return await _fallback.TranscribeAsync(media, cancellationToken);
            }

            var payload = await response.Content.ReadFromJsonAsync<TranscriptionResponse>(cancellationToken: cancellationToken);
            return string.IsNullOrWhiteSpace(payload?.Text)
                ? await _fallback.TranscribeAsync(media, cancellationToken)
                : payload.Text.Trim();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "OpenAI transcription failed. Falling back to mock transcription.");
            return await _fallback.TranscribeAsync(media, cancellationToken);
        }
    }

    private HttpClient CreateClient(string name)
    {
        var client = httpClientFactory.CreateClient(name);
        client.Timeout = TimeSpan.FromSeconds(options.Value.TimeoutSeconds);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.ApiKey);
        return client;
    }

    private sealed class TranscriptionResponse
    {
        public string? Text { get; set; }
    }
}

public sealed class OpenAiCropAnalysisService(
    IHttpClientFactory httpClientFactory,
    IOptions<OpenAIOptions> options,
    ILogger<OpenAiCropAnalysisService> logger) : ICropAnalysisService
{
    private readonly MockCropAnalysisService _fallback = new();

    public async Task<CropAnalysisResult> AnalyzeAsync(string inputText, IEnumerable<InboundMediaDto> media, CancellationToken cancellationToken = default)
    {
        if (!options.Value.Enabled || string.IsNullOrWhiteSpace(options.Value.ApiKey))
        {
            return await _fallback.AnalyzeAsync(inputText, media, cancellationToken);
        }

        try
        {
            var imageInputs = BuildImageInputs(media).ToList();
            var responseBody = new
            {
                model = options.Value.VisionModel,
                temperature = 0.2,
                messages = new object[]
                {
                    new
                    {
                        role = "system",
                        content = """
                            You are FarmIQ, a cautious agronomy assistant for smallholder farmers.
                            Analyze the crop problem from the farmer text and any attached images.
                            Return strict JSON only.
                            Prefer the most likely disease, pest, or stress category. If evidence is limited, lower confidence and ask for a closer photo.
                            Do not mention weather, pricing, or unsupported chemicals. Keep treatment practical and low cost.
                            """
                    },
                    new
                    {
                        role = "user",
                        content = BuildUserContent(inputText, imageInputs)
                    }
                },
                response_format = BuildResponseFormat()
            };

            var client = CreateClient(nameof(OpenAiCropAnalysisService));
            using var response = await client.PostAsJsonAsync($"{options.Value.BaseUrl.TrimEnd('/')}/chat/completions", responseBody, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("OpenAI crop analysis returned status {StatusCode}. Falling back to rules.", response.StatusCode);
                return await _fallback.AnalyzeAsync(inputText, media, cancellationToken);
            }

            var payload = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(cancellationToken: cancellationToken);
            var content = payload?.Choices?.FirstOrDefault()?.Message?.Content;
            if (string.IsNullOrWhiteSpace(content))
            {
                return await _fallback.AnalyzeAsync(inputText, media, cancellationToken);
            }

            var parsed = JsonSerializer.Deserialize<OpenAiCropAnalysisPayload>(content, JsonOptions);
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.DiseaseOrPestName))
            {
                return await _fallback.AnalyzeAsync(inputText, media, cancellationToken);
            }

            return new CropAnalysisResult
            {
                DiseaseName = parsed.DiseaseOrPestName.Trim(),
                ConfidenceScore = Math.Clamp(parsed.ConfidenceScore, 0m, 1m),
                TreatmentRecommendation = parsed.TreatmentRecommendation.Trim(),
                HarvestTiming = parsed.HarvestTiming.Trim(),
                FollowUpQuestion = NormalizeOptional(parsed.FollowUpQuestion),
                SafetyDisclaimer = NormalizeOptional(parsed.SafetyDisclaimer) ?? "Follow local label guidance and protective equipment instructions before applying any treatment.",
                NeedsCloserPhoto = parsed.NeedsCloserPhoto,
                NeedsLocation = parsed.NeedsLocation,
                ShortReasoningSummary = NormalizeOptional(parsed.ShortReasoningSummary),
                AnalysisSource = AdvisoryAnalysisSource.OpenAi
            };
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "OpenAI crop analysis failed. Falling back to rules.");
            return await _fallback.AnalyzeAsync(inputText, media, cancellationToken);
        }
    }

    private IEnumerable<object> BuildImageInputs(IEnumerable<InboundMediaDto> media)
    {
        foreach (var image in media.Where(x => x.MediaType == MediaType.Image).Take(options.Value.MaxImagesPerRequest))
        {
            string? dataUrl = null;
            if (!string.IsNullOrWhiteSpace(image.StoragePath) && File.Exists(image.StoragePath))
            {
                var bytes = File.ReadAllBytes(image.StoragePath);
                dataUrl = $"data:{image.ContentType};base64,{Convert.ToBase64String(bytes)}";
            }
            else if (Uri.TryCreate(image.Url, UriKind.Absolute, out _))
            {
                dataUrl = image.Url;
            }

            if (!string.IsNullOrWhiteSpace(dataUrl))
            {
                yield return new
                {
                    type = "image_url",
                    image_url = new
                    {
                        url = dataUrl,
                        detail = "low"
                    }
                };
            }
        }
    }

    private static object[] BuildUserContent(string inputText, IReadOnlyCollection<object> imageInputs)
    {
        var hints = ExtractAgronomyHints(inputText);
        var content = new List<object>
        {
            new
            {
                type = "text",
                text = $"""
                    Farmer report:
                    {inputText}

                    Deterministic agronomy hints:
                    {hints}
                    """
            }
        };

        content.AddRange(imageInputs);
        return content.ToArray();
    }

    private static string ExtractAgronomyHints(string text)
    {
        var lower = text.ToLowerInvariant();
        var hints = new List<string>();

        AddHintIfPresent(lower, hints, "aphid", "Possible sap-sucking pest pressure such as aphids.");
        AddHintIfPresent(lower, hints, "mites", "Consider mite pressure if leaves look speckled or curled.");
        AddHintIfPresent(lower, hints, "worm", "Chewing pests may be involved.");
        AddHintIfPresent(lower, hints, "rot", "Consider rot or root/stem disease.");
        AddHintIfPresent(lower, hints, "spot", "Leaf spotting can align with fungal or bacterial disease.");
        AddHintIfPresent(lower, hints, "mildew", "Mildew-like disease is possible.");
        AddHintIfPresent(lower, hints, "yellow", "Yellowing may reflect nutrient or pest stress.");
        AddHintIfPresent(lower, hints, "breaking", "Stem or branch weakness may indicate structural or vascular stress.");

        return hints.Count == 0 ? "No strong deterministic hint detected from text alone." : string.Join(" ", hints);
    }

    private static void AddHintIfPresent(string lower, ICollection<string> hints, string token, string hint)
    {
        if (lower.Contains(token, StringComparison.OrdinalIgnoreCase))
        {
            hints.Add(hint);
        }
    }

    private HttpClient CreateClient(string name)
    {
        var client = httpClientFactory.CreateClient(name);
        client.Timeout = TimeSpan.FromSeconds(options.Value.TimeoutSeconds);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.ApiKey);
        return client;
    }

    private static object BuildResponseFormat() =>
        new
        {
            type = "json_schema",
            json_schema = new
            {
                name = "crop_advisory",
                strict = true,
                schema = new
                {
                    type = "object",
                    additionalProperties = false,
                    properties = new
                    {
                        diseaseOrPestName = new { type = "string" },
                        confidenceScore = new { type = "number" },
                        treatmentRecommendation = new { type = "string" },
                        harvestTiming = new { type = "string" },
                        followUpQuestion = new { type = "string" },
                        safetyDisclaimer = new { type = "string" },
                        needsCloserPhoto = new { type = "boolean" },
                        needsLocation = new { type = "boolean" },
                        shortReasoningSummary = new { type = "string" }
                    },
                    required = new[]
                    {
                        "diseaseOrPestName",
                        "confidenceScore",
                        "treatmentRecommendation",
                        "harvestTiming",
                        "followUpQuestion",
                        "safetyDisclaimer",
                        "needsCloserPhoto",
                        "needsLocation",
                        "shortReasoningSummary"
                    }
                }
            }
        };

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), "none", StringComparison.OrdinalIgnoreCase)
            ? null
            : value.Trim();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class ChatCompletionResponse
    {
        public List<Choice>? Choices { get; set; }
    }

    private sealed class Choice
    {
        public ChatMessage? Message { get; set; }
    }

    private sealed class ChatMessage
    {
        public string? Content { get; set; }
    }

    private sealed class OpenAiCropAnalysisPayload
    {
        public string DiseaseOrPestName { get; set; } = string.Empty;
        public decimal ConfidenceScore { get; set; }
        public string TreatmentRecommendation { get; set; } = string.Empty;
        public string HarvestTiming { get; set; } = string.Empty;
        public string? FollowUpQuestion { get; set; }
        public string? SafetyDisclaimer { get; set; }
        public bool NeedsCloserPhoto { get; set; }
        public bool NeedsLocation { get; set; }
        public string? ShortReasoningSummary { get; set; }
    }
}
