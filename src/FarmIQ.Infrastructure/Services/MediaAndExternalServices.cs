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

public sealed class LocalMediaStorageService(
    IHttpClientFactory httpClientFactory,
    IOptions<LocalStorageOptions> options,
    IOptions<ChannelApiOptions> channelOptions,
    ILogger<LocalMediaStorageService> logger) : IMediaStorageService
{
    public async Task<MediaStorageResult> SaveRemoteMediaAsync(InboundMediaDto media, CancellationToken cancellationToken = default)
    {
        var root = options.Value.RootPath;
        var absoluteRoot = Path.IsPathRooted(root) ? root : Path.Combine(AppContext.BaseDirectory, root);
        Directory.CreateDirectory(absoluteRoot);

        var safeName = $"{Guid.NewGuid():N}_{Sanitize(media.FileName)}";
        var targetPath = Path.Combine(absoluteRoot, safeName);
        var content = await ResolveContentAsync(media, cancellationToken);
        await File.WriteAllBytesAsync(targetPath, content, cancellationToken);

        return new MediaStorageResult
        {
            StoragePath = targetPath,
            StorageUrl = $"{options.Value.PublicBaseUrl.TrimEnd('/')}/{safeName}",
            SizeBytes = content.LongLength
        };
    }

    private async Task<byte[]> ResolveContentAsync(InboundMediaDto media, CancellationToken cancellationToken)
    {
        if (Uri.TryCreate(media.Url, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return await TryDownloadAsync(media, uri, cancellationToken) ?? BuildFallbackPayload(media);
        }

        return media.ChannelType switch
        {
            ChannelType.Telegram => await DownloadTelegramMediaAsync(media, cancellationToken) ?? BuildFallbackPayload(media),
            ChannelType.WhatsApp => await DownloadWhatsAppMediaAsync(media, cancellationToken) ?? BuildFallbackPayload(media),
            _ => BuildFallbackPayload(media)
        };
    }

    private async Task<byte[]?> DownloadTelegramMediaAsync(InboundMediaDto media, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(channelOptions.Value.TelegramBotToken))
        {
            return null;
        }

        try
        {
            var fileId = string.IsNullOrWhiteSpace(media.ExternalMediaId) ? media.Url : media.ExternalMediaId;
            var client = httpClientFactory.CreateClient(nameof(LocalMediaStorageService));
            var metadataUrl = $"{channelOptions.Value.TelegramBaseUrl.TrimEnd('/')}/bot{channelOptions.Value.TelegramBotToken}/getFile?file_id={Uri.EscapeDataString(fileId)}";
            var payload = await client.GetFromJsonAsync<TelegramFileLookupResponse>(metadataUrl, cancellationToken);
            var filePath = payload?.Result?.FilePath;
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return null;
            }

            var fileUrl = $"{channelOptions.Value.TelegramBaseUrl.TrimEnd('/')}/file/bot{channelOptions.Value.TelegramBotToken}/{filePath}";
            return await client.GetByteArrayAsync(fileUrl, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Unable to download Telegram media {MediaId}.", media.ExternalMediaId);
            return null;
        }
    }

    private async Task<byte[]?> DownloadWhatsAppMediaAsync(InboundMediaDto media, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(channelOptions.Value.WhatsAppAccessToken))
        {
            return null;
        }

        try
        {
            var mediaId = string.IsNullOrWhiteSpace(media.ExternalMediaId) ? media.Url : media.ExternalMediaId;
            var client = httpClientFactory.CreateClient(nameof(LocalMediaStorageService));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", channelOptions.Value.WhatsAppAccessToken);
            var metadataUrl = $"{channelOptions.Value.WhatsAppBaseUrl.TrimEnd('/')}/{mediaId}";
            var payload = await client.GetFromJsonAsync<WhatsAppMediaLookupResponse>(metadataUrl, cancellationToken);
            if (string.IsNullOrWhiteSpace(payload?.Url))
            {
                return null;
            }

            using var mediaRequest = new HttpRequestMessage(HttpMethod.Get, payload.Url);
            mediaRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", channelOptions.Value.WhatsAppAccessToken);
            using var mediaResponse = await client.SendAsync(mediaRequest, cancellationToken);
            if (!mediaResponse.IsSuccessStatusCode)
            {
                return null;
            }

            return await mediaResponse.Content.ReadAsByteArrayAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Unable to download WhatsApp media {MediaId}.", media.ExternalMediaId);
            return null;
        }
    }

    private async Task<byte[]?> TryDownloadAsync(InboundMediaDto media, Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            var client = httpClientFactory.CreateClient(nameof(LocalMediaStorageService));
            if (media.ChannelType == ChannelType.WhatsApp && !string.IsNullOrWhiteSpace(channelOptions.Value.WhatsAppAccessToken))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", channelOptions.Value.WhatsAppAccessToken);
            }

            return await client.GetByteArrayAsync(uri, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Unable to download media from {Url}. Falling back to metadata snapshot.", media.Url);
            return null;
        }
    }

    private static byte[] BuildFallbackPayload(InboundMediaDto media) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(media));

    private static string Sanitize(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.IsNullOrWhiteSpace(fileName)
            ? "media.bin"
            : string.Concat(fileName.Select(ch => invalid.Contains(ch) ? '_' : ch));
    }

    private sealed class TelegramFileLookupResponse
    {
        public TelegramFileLookupResult? Result { get; set; }
    }

    private sealed class TelegramFileLookupResult
    {
        public string? FilePath { get; set; }
    }

    private sealed class WhatsAppMediaLookupResponse
    {
        public string? Url { get; set; }
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
        var language = lower.Contains("hola") ? "es" : lower.Contains("habari") ? "sw" : lower.Contains("salom") ? "uz" : "en";
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
        var lower = inputText.ToLowerInvariant();
        var hasImage = media.Any(x => x.MediaType == MediaType.Image);
        var mentionsAphids = lower.Contains("aphid");
        var mentionsLeafSpots = lower.Contains("spot") || lower.Contains("stain") || lower.Contains("mildew");
        var confidence = mentionsAphids || mentionsLeafSpots || hasImage ? 0.82m : 0.61m;
        var diagnosis = mentionsAphids
            ? "Possible aphid infestation"
            : mentionsLeafSpots
                ? "Possible fungal leaf spot"
                : "Nutrient stress or mixed pest pressure";

        return Task.FromResult(new CropAnalysisResult
        {
            DiseaseName = diagnosis,
            ConfidenceScore = confidence,
            TreatmentRecommendation = mentionsAphids
                ? "Inspect leaf undersides, wash off clustered aphids where possible, and use locally approved targeted control if colonies keep spreading."
                : mentionsLeafSpots
                    ? "Remove heavily infected leaves, improve spacing, and apply a locally approved fungicide if symptoms spread."
                    : "Inspect the underside of leaves, confirm pest presence, and apply balanced nutrients with targeted pest control only if confirmed.",
            HarvestTiming = "Avoid harvesting during the next 5-7 days if treatment is applied; recheck plant vigor before harvesting.",
            FollowUpQuestion = confidence < 0.7m ? "Can you send a closer photo of the affected leaves and stem base?" : null,
            SafetyDisclaimer = confidence < 0.7m
                ? "Please verify with a local agronomist or extension worker before using expensive inputs."
                : "Follow label instructions and protective equipment guidance for any treatment.",
            NeedsCloserPhoto = confidence < 0.7m && !hasImage,
            ShortReasoningSummary = mentionsAphids
                ? "The report mentions aphids spreading and leaf damage, which often points to sap-sucking pest pressure."
                : mentionsLeafSpots
                    ? "Spotting or staining on leaves often aligns with early fungal disease patterns."
                    : "The description is broad, so the issue may involve several stress factors.",
            AnalysisSource = AdvisoryAnalysisSource.Fallback
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
            return Fallback(string.Empty);
        }

        try
        {
            var client = httpClientFactory.CreateClient(nameof(OpenWeatherMapService));
            var currentUrl = $"{options.Value.BaseUrl.TrimEnd('/')}/weather?lat={latitude.Value}&lon={longitude.Value}&units=metric&appid={options.Value.ApiKey}";
            var forecastUrl = $"{options.Value.BaseUrl.TrimEnd('/')}/forecast?lat={latitude.Value}&lon={longitude.Value}&units=metric&appid={options.Value.ApiKey}";

            var current = await client.GetFromJsonAsync<OpenWeatherCurrentResponse>(currentUrl, cancellationToken);
            var forecast = await client.GetFromJsonAsync<OpenWeatherForecastResponse>(forecastUrl, cancellationToken);
            if (current?.Main is null)
            {
                return Fallback(string.Empty);
            }

            var description = current.Weather?.FirstOrDefault()?.Description ?? "conditions unavailable";
            var forecastItems = forecast?.Items?
                .OrderBy(x => x.DateUtc)
                .Take(4)
                .ToList() ?? [];
            var maxRainProbability = forecastItems.Count == 0 ? 0m : forecastItems.Max(x => x.RainProbability);
            var nextWindowHours = forecastItems.Count * 3;

            return new WeatherSummaryDto
            {
                TemperatureCelsius = current.Main.Temp,
                RainProbability = maxRainProbability,
                Summary = BuildSummary(description, current.Main.Temp, maxRainProbability, nextWindowHours),
                CropImpact = BuildCropImpact(current.Main.Temp, maxRainProbability)
            };
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Weather lookup failed. Returning quiet fallback weather advisory.");
            return Fallback(string.Empty);
        }
    }

    private static string BuildSummary(string description, decimal temperature, decimal rainProbability, int nextWindowHours)
    {
        if (rainProbability >= 0.55m)
        {
            return $"Current weather is {description} at {temperature:0.#}C. Rain chance in the next {Math.Max(nextWindowHours, 6)} hours is {rainProbability:P0}.";
        }

        return $"Current weather is {description} at {temperature:0.#}C. Rain risk in the next {Math.Max(nextWindowHours, 6)} hours looks low.";
    }

    private static string BuildCropImpact(decimal temperature, decimal rainProbability)
    {
        if (temperature > 34)
        {
            return "Heat stress risk is elevated. Irrigate early morning if water is available.";
        }

        if (rainProbability >= 0.55m)
        {
            return "Postpone spraying if possible and monitor disease pressure after the wet period.";
        }

        return "No acute weather stress signal. Keep watching field moisture and pest spread.";
    }

    private static WeatherSummaryDto Fallback(string summary) =>
        new()
        {
            TemperatureCelsius = 0,
            RainProbability = 0,
            Summary = summary,
            CropImpact = string.Empty
        };

    private sealed class OpenWeatherCurrentResponse
    {
        public MainPayload? Main { get; set; }
        public List<WeatherPayload>? Weather { get; set; }
    }

    private sealed class OpenWeatherForecastResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("list")]
        public List<ForecastItem>? Items { get; set; }
    }

    private sealed class ForecastItem
    {
        [System.Text.Json.Serialization.JsonPropertyName("dt_txt")]
        public DateTime DateUtc { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("pop")]
        public decimal RainProbability { get; set; }
    }

    private sealed class MainPayload
    {
        public decimal Temp { get; set; }
    }

    private sealed class WeatherPayload
    {
        public string Description { get; set; } = string.Empty;
    }
}
