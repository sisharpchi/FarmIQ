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

public sealed class LocalMediaStorageService(
    IHttpClientFactory httpClientFactory,
    IOptions<LocalStorageOptions> options,
    ILogger<LocalMediaStorageService> logger) : IMediaStorageService
{
    public async Task<MediaStorageResult> SaveRemoteMediaAsync(InboundMediaDto media, CancellationToken cancellationToken = default)
    {
        var root = options.Value.RootPath;
        var absoluteRoot = Path.IsPathRooted(root) ? root : Path.Combine(AppContext.BaseDirectory, root);
        Directory.CreateDirectory(absoluteRoot);

        var safeName = $"{Guid.NewGuid():N}_{Sanitize(media.FileName)}";
        var targetPath = Path.Combine(absoluteRoot, safeName);

        byte[] content;
        if (Uri.TryCreate(media.Url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            try
            {
                content = await httpClientFactory.CreateClient(nameof(LocalMediaStorageService)).GetByteArrayAsync(uri, cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Unable to download media from {Url}. Falling back to metadata snapshot.", media.Url);
                content = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(media));
            }
        }
        else
        {
            content = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(media));
        }

        await File.WriteAllBytesAsync(targetPath, content, cancellationToken);

        return new MediaStorageResult
        {
            StoragePath = targetPath,
            StorageUrl = $"{options.Value.PublicBaseUrl.TrimEnd('/')}/{safeName}",
            SizeBytes = content.LongLength
        };
    }

    private static string Sanitize(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.IsNullOrWhiteSpace(fileName)
            ? "media.bin"
            : string.Concat(fileName.Select(ch => invalid.Contains(ch) ? '_' : ch));
    }
}

public sealed class MockSpeechToTextService : ISpeechToTextService
{
    public Task<string?> TranscribeAsync(IEnumerable<InboundMediaDto> media, CancellationToken cancellationToken = default)
    {
        var firstVoice = media.FirstOrDefault();
        return Task.FromResult(firstVoice is null ? null : $"Voice note received from {firstVoice.FileName}.");
    }
}

public sealed class MockLanguageService : ILanguageService
{
    public Task<string> DetectLanguageAsync(string text, CancellationToken cancellationToken = default)
    {
        var lower = text.ToLowerInvariant();
        var language = lower.Contains("hola") ? "es" : lower.Contains("habari") ? "sw" : "en";
        return Task.FromResult(language);
    }

    public Task<string> TranslateToEnglishAsync(string text, string sourceLanguage, CancellationToken cancellationToken = default) =>
        Task.FromResult(text);

    public Task<string> TranslateFromEnglishAsync(string text, string targetLanguage, CancellationToken cancellationToken = default) =>
        Task.FromResult(text);
}

public sealed class MockCropAnalysisService : ICropAnalysisService
{
    public Task<CropAnalysisResult> AnalyzeAsync(string inputText, IEnumerable<InboundMediaDto> media, CancellationToken cancellationToken = default)
    {
        var mentionsLeafSpots = inputText.Contains("spot", StringComparison.OrdinalIgnoreCase) || media.Any(x => x.MediaType == MediaType.Image);
        var confidence = mentionsLeafSpots ? 0.82m : 0.61m;

        return Task.FromResult(new CropAnalysisResult
        {
            DiseaseName = mentionsLeafSpots ? "Possible fungal leaf spot" : "Nutrient stress or pest pressure",
            ConfidenceScore = confidence,
            TreatmentRecommendation = mentionsLeafSpots
                ? "Remove heavily infected leaves, improve spacing, and apply a locally approved fungicide if symptoms spread."
                : "Inspect the underside of leaves, confirm pest presence, and apply balanced nutrients with targeted pest control only if confirmed.",
            HarvestTiming = "Avoid harvesting during the next 5-7 days if treatment is applied; recheck plant vigor before harvesting.",
            FollowUpQuestion = confidence < 0.7m ? "Can you send a closer photo of the affected leaves and the stem base?" : null,
            SafetyDisclaimer = confidence < 0.7m
                ? "Please verify with a local agronomist or extension worker before using expensive inputs."
                : "Follow label instructions and protective equipment guidance for any treatment."
        });
    }
}

public sealed class OpenWeatherMapService(
    IHttpClientFactory httpClientFactory,
    IOptions<OpenWeatherMapOptions> options,
    ILogger<OpenWeatherMapService> logger) : IWeatherService
{
    public async Task<WeatherSummaryDto> GetSummaryAsync(double? latitude, double? longitude, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(options.Value.ApiKey) || !latitude.HasValue || !longitude.HasValue)
        {
            return Fallback("Weather API not configured or farmer location unavailable.");
        }

        try
        {
            var client = httpClientFactory.CreateClient(nameof(OpenWeatherMapService));
            var url = $"{options.Value.BaseUrl.TrimEnd('/')}/weather?lat={latitude.Value}&lon={longitude.Value}&units=metric&appid={options.Value.ApiKey}";
            var payload = await client.GetFromJsonAsync<OpenWeatherCurrentResponse>(url, cancellationToken);

            if (payload?.Main is null)
            {
                return Fallback("Weather payload missing temperature details.");
            }

            var description = payload.Weather?.FirstOrDefault()?.Description ?? "conditions unavailable";
            return new WeatherSummaryDto
            {
                TemperatureCelsius = payload.Main.Temp,
                RainProbability = payload.Rain is null ? 0m : 0.55m,
                Summary = $"Current weather is {description} at {payload.Main.Temp:0.#}C.",
                CropImpact = payload.Main.Temp > 34
                    ? "Heat stress risk is elevated. Irrigate early morning if water is available."
                    : "No acute heat stress signal. Monitor moisture and disease pressure after rainfall."
            };
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Weather lookup failed. Returning fallback weather advisory.");
            return Fallback("Weather lookup unavailable, using fallback advisory.");
        }
    }

    private static WeatherSummaryDto Fallback(string summary) =>
        new()
        {
            TemperatureCelsius = 27,
            RainProbability = 0.25m,
            Summary = summary,
            CropImpact = "Monitor field moisture and postpone spraying if rain is likely in the next 24 hours."
        };

    private sealed class OpenWeatherCurrentResponse
    {
        public MainPayload? Main { get; set; }
        public RainPayload? Rain { get; set; }
        public List<WeatherPayload>? Weather { get; set; }
    }

    private sealed class MainPayload
    {
        public decimal Temp { get; set; }
    }

    private sealed class RainPayload
    {
        public decimal? OneHour { get; set; }
    }

    private sealed class WeatherPayload
    {
        public string Description { get; set; } = string.Empty;
    }
}
