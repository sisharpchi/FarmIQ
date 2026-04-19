using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FarmIQ.Application.Abstractions;
using FarmIQ.Application.Contracts;
using FarmIQ.Infrastructure.Configuration;
using FarmIQ.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FarmIQ.Infrastructure.Services;

public sealed class FarmCropAnalysisService(
    GlmCropAnalysisService glmCropAnalysisService,
    OpenAiCropAnalysisService openAiCropAnalysisService) : ICropAnalysisService
{
    public async Task<CropAnalysisResult> AnalyzeAsync(string inputText, IEnumerable<InboundMediaDto> media, CancellationToken cancellationToken = default)
    {
        var mediaList = media.ToList();
        var hasImage = mediaList.Any(x => x.MediaType == MediaType.Image);

        if (!hasImage)
        {
            var glmResult = await glmCropAnalysisService.AnalyzeTextOnlyAsync(inputText, cancellationToken);
            if (glmResult is not null)
            {
                return glmResult;
            }
        }

        return await openAiCropAnalysisService.AnalyzeAsync(inputText, mediaList, cancellationToken);
    }
}

public sealed class GlmCropAnalysisService(
    GlmChatClient glmChatClient,
    ILogger<GlmCropAnalysisService> logger)
{
    public async Task<CropAnalysisResult?> AnalyzeTextOnlyAsync(string inputText, CancellationToken cancellationToken = default)
    {
        if (!glmChatClient.IsConfigured || string.IsNullOrWhiteSpace(inputText))
        {
            return null;
        }

        try
        {
            var hints = ExtractAgronomyHints(inputText);
            var content = await glmChatClient.CompleteAsync(
                [
                    new GlmChatMessage(
                        "system",
                        """
                        You are FarmIQ, a cautious agronomy assistant for smallholder farmers.
                        Analyze the farmer report and return strict JSON only with these exact fields:
                        diseaseOrPestName, confidenceScore, treatmentRecommendation, harvestTiming, followUpQuestion,
                        safetyDisclaimer, needsCloserPhoto, needsLocation, shortReasoningSummary.
                        Rules:
                        - Prefer the most likely disease, pest, or stress category.
                        - Keep confidenceScore between 0 and 1.
                        - Keep recommendations practical, low cost, and farmer-friendly.
                        - If evidence is limited, lower confidence and ask for one useful follow-up.
                        - Do not include markdown, code fences, or extra commentary.
                        """),
                    new GlmChatMessage(
                        "user",
                        $"""
                        Farmer report:
                        {inputText}

                        Deterministic agronomy hints:
                        {hints}
                        """)
                ],
                temperature: 0.1m,
                cancellationToken);

            if (string.IsNullOrWhiteSpace(content))
            {
                return null;
            }

            var json = ExtractJsonObject(content);
            var parsed = JsonSerializer.Deserialize<GlmCropAnalysisPayload>(json, JsonOptions);
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.DiseaseOrPestName))
            {
                return null;
            }

            return new CropAnalysisResult
            {
                DiseaseName = parsed.DiseaseOrPestName.Trim(),
                ConfidenceScore = Math.Clamp(parsed.ConfidenceScore, 0m, 1m),
                TreatmentRecommendation = NormalizeRequired(parsed.TreatmentRecommendation, "Inspect the affected crop closely and start with the lowest-cost approved treatment option."),
                HarvestTiming = NormalizeRequired(parsed.HarvestTiming, "Recheck crop condition before harvesting, especially if treatment is applied."),
                FollowUpQuestion = NormalizeOptional(parsed.FollowUpQuestion),
                SafetyDisclaimer = NormalizeOptional(parsed.SafetyDisclaimer) ?? "Follow local label guidance and protective equipment instructions before applying any treatment.",
                NeedsCloserPhoto = parsed.NeedsCloserPhoto,
                NeedsLocation = parsed.NeedsLocation,
                ShortReasoningSummary = NormalizeOptional(parsed.ShortReasoningSummary),
                AnalysisSource = AdvisoryAnalysisSource.Glm
            };
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "GLM crop analysis failed. Falling through to the next provider.");
            return null;
        }
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

    private static string ExtractJsonObject(string content)
    {
        var trimmed = content.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineBreak = trimmed.IndexOf('\n');
            if (firstLineBreak >= 0)
            {
                trimmed = trimmed[(firstLineBreak + 1)..];
            }

            var closingFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (closingFence >= 0)
            {
                trimmed = trimmed[..closingFence];
            }
        }

        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            return trimmed[start..(end + 1)];
        }

        return trimmed;
    }

    private static string NormalizeRequired(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), "none", StringComparison.OrdinalIgnoreCase)
            ? null
            : value.Trim();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class GlmCropAnalysisPayload
    {
        public string DiseaseOrPestName { get; set; } = string.Empty;
        public decimal ConfidenceScore { get; set; }
        public string? TreatmentRecommendation { get; set; }
        public string? HarvestTiming { get; set; }
        public string? FollowUpQuestion { get; set; }
        public string? SafetyDisclaimer { get; set; }
        public bool NeedsCloserPhoto { get; set; }
        public bool NeedsLocation { get; set; }
        public string? ShortReasoningSummary { get; set; }
    }
}

public sealed class GlmChatClient(
    IHttpClientFactory httpClientFactory,
    IOptions<GlmOptions> options,
    ILogger<GlmChatClient> logger)
{
    public bool IsConfigured => options.Value.Enabled && !string.IsNullOrWhiteSpace(options.Value.ApiKey);

    public async Task<string?> CompleteAsync(IEnumerable<GlmChatMessage> messages, decimal temperature, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return null;
        }

        try
        {
            var request = new
            {
                model = options.Value.Model,
                temperature,
                messages = messages.Select(x => new { role = x.Role, content = x.Content }).ToArray()
            };

            var client = CreateClient();
            using var response = await client.PostAsJsonAsync($"{options.Value.BaseUrl.TrimEnd('/')}/chat/completions", request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning("GLM chat completion returned status {StatusCode}: {Error}", response.StatusCode, error);
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(cancellationToken: cancellationToken);
            return payload?.Choices?.FirstOrDefault()?.Message?.Content?.Trim();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "GLM chat completion failed.");
            return null;
        }
    }

    private HttpClient CreateClient()
    {
        var client = httpClientFactory.CreateClient(nameof(GlmChatClient));
        client.Timeout = TimeSpan.FromSeconds(options.Value.TimeoutSeconds);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.ApiKey);
        return client;
    }

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
}

public sealed record GlmChatMessage(string Role, string Content);
