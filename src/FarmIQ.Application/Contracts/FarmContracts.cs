using System.ComponentModel.DataAnnotations;
using FarmIQ.Shared;

namespace FarmIQ.Application.Contracts;

public sealed class NormalizedInboundMessageCommand
{
    public ChannelType ChannelType { get; set; }
    public string ExternalUserId { get; set; } = string.Empty;
    public string ExternalConversationId { get; set; } = string.Empty;
    public string ExternalMessageId { get; set; } = string.Empty;
    public string OriginalPayloadJson { get; set; } = "{}";
    public string? Text { get; set; }
    public string? IncomingLanguage { get; set; }
    public string? DisplayName { get; set; }
    public string? TenantKey { get; set; }
    public string? EventType { get; set; }
    public string? CorrelationId { get; set; }
    public bool IsUnsupportedEvent { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public InboundIntentType IntentType { get; set; } = InboundIntentType.Unknown;
    public bool HasLocation { get; set; }
    public string? ImmediateResponseCandidate { get; set; }
    public string? IgnoredReason { get; set; }
    public IDictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
    public IList<InboundMediaDto> Media { get; set; } = [];
}

public sealed class InboundMediaDto
{
    public ChannelType ChannelType { get; set; }
    public MediaType MediaType { get; set; }
    public string ExternalMediaId { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public long? SizeBytes { get; set; }
    public string? StoragePath { get; set; }
    public string? StorageUrl { get; set; }
}

public sealed record InboundMessageAcceptedDto(Guid InboundMessageId, Guid ProcessingJobId, MessageLifecycleStatus Status);

public sealed class InboundAcceptanceResult
{
    public InboundMessageAcceptedDto AcceptedMessage { get; set; } = new(Guid.Empty, Guid.Empty, MessageLifecycleStatus.Received);
    public bool IsDuplicate { get; set; }
    public Guid? ExistingInboundMessageId { get; set; }
}

public sealed class MediaStorageResult
{
    public string StoragePath { get; set; } = string.Empty;
    public string StorageUrl { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
}

public sealed class CropAnalysisResult
{
    public string DiseaseName { get; set; } = string.Empty;
    public decimal ConfidenceScore { get; set; }
    public string TreatmentRecommendation { get; set; } = string.Empty;
    public string HarvestTiming { get; set; } = string.Empty;
    public string? FollowUpQuestion { get; set; }
    public string? SafetyDisclaimer { get; set; }
    public bool NeedsCloserPhoto { get; set; }
    public bool NeedsLocation { get; set; }
    public string? ShortReasoningSummary { get; set; }
    public AdvisoryAnalysisSource AnalysisSource { get; set; } = AdvisoryAnalysisSource.Fallback;
}

public sealed class WeatherSummaryDto
{
    public decimal TemperatureCelsius { get; set; }
    public decimal RainProbability { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string CropImpact { get; set; } = string.Empty;
}

public sealed class AdvisoryResultDto
{
    public Guid AdvisoryId { get; set; }
    public string DiseaseName { get; set; } = string.Empty;
    public decimal ConfidenceScore { get; set; }
    public string TreatmentRecommendation { get; set; } = string.Empty;
    public string HarvestTiming { get; set; } = string.Empty;
    public string AdvisoryText { get; set; } = string.Empty;
    public string AdvisoryLanguage { get; set; } = "en";
    public string? FollowUpQuestion { get; set; }
    public string? SafetyDisclaimer { get; set; }
    public WeatherSummaryDto Weather { get; set; } = new();
}

public sealed class ChannelReplyRequest
{
    public ChannelType ChannelType { get; set; }
    public string RecipientId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public Guid ConversationId { get; set; }
    public IDictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
}

public sealed record ChannelSendResult(bool Success, string? ExternalMessageId, string? ErrorMessage);

public sealed record ConversationSummaryDto(Guid ConversationId, string FarmerId, string FarmerName, ChannelType ChannelType, DateTime LastMessageUtc, int InboundCount, int OutboundCount, InboundIntentType LastDetectedIntent, ConversationAssistantState AssistantState, bool LocationKnown);
public sealed record ProcessingJobSummaryDto(Guid JobId, Guid InboundMessageId, ProcessingJobStatus Status, int Attempts, string? LastError, DateTime ScheduledUtc, DateTime? NextAttemptUtc, DateTime? LeaseExpiresUtc, bool IsTerminalFailure, string? DeadLetterReason);
public sealed record AdvisorySummaryDto(Guid AdvisoryId, string DiseaseName, decimal ConfidenceScore, string AdvisoryLanguage, string AdvisoryText, AdvisoryAnalysisSource AnalysisSource, bool NeedsLocation, bool NeedsCloserPhoto);
public sealed record DeliveryIssueSummaryDto(Guid DeliveryId, ChannelType ChannelType, string ExternalMessageId, bool IsDuplicate, Guid? InboundMessageId, DateTime CreatedUtc);
public sealed record StuckJobSummaryDto(Guid JobId, string? LeaseOwner, DateTime? LeaseExpiresUtc, int Attempts, string? LastError);
public sealed record ConversationDetailDto(Guid ConversationId, string FarmerName, string FarmerId, ChannelType ChannelType, DateTime LastMessageUtc, ConversationAssistantState AssistantState, InboundIntentType LastDetectedIntent, bool LocationKnown, DateTime? LastBotPromptUtc, DateTime? LocationRequestedUtc, IReadOnlyCollection<ConversationMessageDto> Messages);
public sealed record ConversationMessageDto(Guid MessageId, string Direction, string? Text, MessageLifecycleStatus? InboundStatus, OutboundDeliveryStatus? OutboundStatus, DateTime CreatedUtc, InboundIntentType? IntentType, string? IgnoredReason);
public sealed record AdvisoryDetailDto(Guid AdvisoryId, string DiseaseName, decimal ConfidenceScore, string TreatmentRecommendation, string HarvestTiming, string AdvisoryLanguage, string AdvisoryText, string? SafetyDisclaimer, string? WeatherSummary, string? CropImpact, AdvisoryAnalysisSource AnalysisSource, bool NeedsLocation, bool NeedsCloserPhoto, string? FollowUpQuestion, string? ShortReasoningSummary);
public sealed record AdminSystemStatusDto(bool ApiHealthy, bool DatabaseConfigured, bool StorageConfigured, bool WeatherConfigured, bool WhatsAppConfigured, bool TelegramConfigured, bool InstagramConfigured, bool PublicSignupEnabled, int WorkerPollIntervalSeconds, bool OpenAiConfigured, DateTime ServerUtc);
public sealed record AdminSessionDto(string UserId, string Name, string Email, IReadOnlyCollection<string> Roles);
public sealed record AdminUserSummaryDto(Guid UserId, string DisplayName, string Email, bool IsEnabled, IReadOnlyCollection<string> Roles, DateTimeOffset? LockoutEndUtc, int AccessFailedCount);

public sealed class AnalyticsSummaryDto
{
    public int TotalFarmers { get; set; }
    public int TotalConversations { get; set; }
    public int TotalInboundMessages { get; set; }
    public int TotalOutboundMessages { get; set; }
    public int FailedJobs { get; set; }
    public int CompletedAdvisories { get; set; }
    public int DuplicateDeliveries { get; set; }
    public int StuckJobs { get; set; }
    public int CommandMessages { get; set; }
    public int GreetingMessages { get; set; }
    public int FollowUpResponses { get; set; }
    public int OpenAiFallbacks { get; set; }
}

public sealed record IntentClassificationResult(
    InboundIntentType IntentType,
    bool QueueForAdvisory,
    bool SendImmediateResponse,
    ConversationAssistantState NextState,
    string? IgnoredReason = null);

public sealed record ComposedConversationResponse(
    string Message,
    ConversationAssistantState NextState,
    bool RequestedLocation,
    bool RequestedPhoto);

public sealed class AdminReplayRequest
{
    public Guid ProcessingJobId { get; set; }
}

public sealed class AdminCreateUserRequest
{
    [Required]
    [StringLength(120, MinimumLength = 2)]
    public string DisplayName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 8)]
    public string Password { get; set; } = string.Empty;

    [Required]
    public IReadOnlyCollection<string> Roles { get; set; } = Array.Empty<string>();
}

public sealed class AdminResetPasswordRequest
{
    [Required]
    [StringLength(100, MinimumLength = 8)]
    public string NewPassword { get; set; } = string.Empty;
}

public sealed class AdminUpdateUserRolesRequest
{
    [Required]
    public IReadOnlyCollection<string> Roles { get; set; } = Array.Empty<string>();
}

public sealed class WebhookEnvelopeDto
{
    public string RawBody { get; set; } = string.Empty;
    public IDictionary<string, string> Query { get; set; } = new Dictionary<string, string>();
    public IDictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();
    public string Path { get; set; } = string.Empty;
}
