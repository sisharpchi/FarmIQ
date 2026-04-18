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
    OpenAi = 1
}

public enum OutboundDeliveryStatus
{
    Pending = 1,
    Sent = 2,
    Failed = 3
}

public sealed record PagedResponse<T>(IReadOnlyCollection<T> Items, int TotalCount, int Page, int PageSize);
