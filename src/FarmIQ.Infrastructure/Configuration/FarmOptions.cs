namespace FarmIQ.Infrastructure.Configuration;

public sealed class OpenWeatherMapOptions
{
    public const string SectionName = "OpenWeatherMap";
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.openweathermap.org/data/2.5";
}

public sealed class LocalStorageOptions
{
    public const string SectionName = "Storage";
    public string RootPath { get; set; } = "App_Data/media";
    public string PublicBaseUrl { get; set; } = "/media";
}

public sealed class SeedAdminOptions
{
    public const string SectionName = "SeedAdmin";
    public string Email { get; set; } = "admin@farmiq.local";
    public string Password { get; set; } = "FarmIQ!123";
    public string DisplayName { get; set; } = "FarmIQ Admin";
}

public sealed class WebhookOptions
{
    public const string SectionName = "Webhooks";
    public string WhatsAppVerifyToken { get; set; } = "verify-token";
    public string InstagramVerifyToken { get; set; } = "verify-token";
    public string TelegramSecretToken { get; set; } = "telegram-secret";
    public string? WhatsAppAppSecret { get; set; }
    public string? InstagramAppSecret { get; set; }
}

public sealed class ChannelApiOptions
{
    public const string SectionName = "ChannelApis";
    public string WhatsAppBaseUrl { get; set; } = "https://graph.facebook.com/v19.0";
    public string? WhatsAppAccessToken { get; set; }
    public string? WhatsAppPhoneNumberId { get; set; }
    public string TelegramBaseUrl { get; set; } = "https://api.telegram.org";
    public string? TelegramBotToken { get; set; }
    public string InstagramBaseUrl { get; set; } = "https://graph.facebook.com/v19.0";
    public string? InstagramAccessToken { get; set; }
    public string? InstagramPageId { get; set; }
}

public sealed class ProcessingOptions
{
    public const string SectionName = "Processing";
    public int PollIntervalSeconds { get; set; } = 5;
    public int LeaseDurationMinutes { get; set; } = 5;
}
