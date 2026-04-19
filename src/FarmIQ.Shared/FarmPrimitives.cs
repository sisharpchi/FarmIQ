namespace FarmIQ.Shared;

public enum ChannelType
{
    WhatsApp = 1,
    Telegram = 2,
    Instagram = 3,
    Sms = 4
}

public enum MediaType
{
    Text = 1,
    Image = 2,
    Voice = 3,
    Audio = 4,
    Video = 5,
    Document = 6,
    Unknown = 99
}

public enum MessageLifecycleStatus
{
    Received = 1,
    Stored = 2,
    Queued = 3,
    Processing = 4,
    Completed = 5,
    Failed = 6,
    Replied = 7
}

public enum InboundIntentType
{
    Unknown = 0,
    StartCommand = 1,
    HelpCommand = 2,
    Greeting = 3,
    SmallTalk = 4,
    SymptomReport = 5,
    MediaOnly = 6,
    LocationShare = 7,
    Unsupported = 8
}

public enum ConversationAssistantState
{
    Idle = 0,
    AwaitingProblemDetails = 1,
    AwaitingPhoto = 2,
    AwaitingLocation = 3,
    AdvisorySent = 4
}

public enum ProcessingJobStatus
{
    Pending = 1,
    InProgress = 2,
    Completed = 3,
    Failed = 4,
    Retrying = 5
}

public enum AdvisoryStatus
{
    Draft = 1,
    Ready = 2,
    Sent = 3,
    Failed = 4
}

public enum AdvisoryAnalysisSource
{
    Fallback = 0,
    OpenAi = 1,
    Glm = 2
}

public enum OutboundDeliveryStatus
{
    Pending = 1,
    Sent = 2,
    Failed = 3
}

public sealed record PagedResponse<T>(IReadOnlyCollection<T> Items, int TotalCount, int Page, int PageSize);

public static class FarmLanguages
{
    public const string English = "en";
    public const string Russian = "ru";
    public const string Uzbek = "uz";

    public static readonly IReadOnlyCollection<string> Supported = [English, Russian, Uzbek];

    public static string Normalize(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return English;
        }

        var value = language.Trim().ToLowerInvariant();

        if (value.StartsWith("ru") || value.Contains("russian") || value.Contains("рус"))
        {
            return Russian;
        }

        if (value.StartsWith("uz") || value.Contains("uzbek") || value.Contains("o'zbek") || value.Contains("ўзбек"))
        {
            return Uzbek;
        }

        if (value.StartsWith("en") || value.Contains("english"))
        {
            return English;
        }

        return Supported.Contains(value, StringComparer.OrdinalIgnoreCase) ? value : English;
    }
}
