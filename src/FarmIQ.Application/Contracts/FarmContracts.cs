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
    public IDictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
    public IList<InboundMediaDto> Media { get; set; } = new List<InboundMediaDto>();
}

public sealed class InboundMediaDto
{
    public MediaType MediaType { get; set; }
    public string ExternalMediaId { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public long? SizeBytes { get; set; }
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

public sealed record ConversationSummaryDto(Guid ConversationId, string FarmerId, string FarmerName, ChannelType ChannelType, DateTime LastMessageUtc, int InboundCount, int OutboundCount);
public sealed record ProcessingJobSummaryDto(Guid JobId, Guid InboundMessageId, ProcessingJobStatus Status, int Attempts, string? LastError, DateTime ScheduledUtc);
public sealed record AdvisorySummaryDto(Guid AdvisoryId, string DiseaseName, decimal ConfidenceScore, string AdvisoryLanguage, string AdvisoryText);
public sealed record DeliveryIssueSummaryDto(Guid DeliveryId, ChannelType ChannelType, string ExternalMessageId, bool IsDuplicate, Guid? InboundMessageId, DateTime CreatedUtc);
public sealed record StuckJobSummaryDto(Guid JobId, string? LeaseOwner, DateTime? LeaseExpiresUtc, int Attempts, string? LastError);
public sealed record ConversationDetailDto(Guid ConversationId, string FarmerName, string FarmerId, ChannelType ChannelType, DateTime LastMessageUtc, IReadOnlyCollection<ConversationMessageDto> Messages);
public sealed record ConversationMessageDto(Guid MessageId, string Direction, string? Text, MessageLifecycleStatus? InboundStatus, OutboundDeliveryStatus? OutboundStatus, DateTime CreatedUtc);
public sealed record AdvisoryDetailDto(Guid AdvisoryId, string DiseaseName, decimal ConfidenceScore, string TreatmentRecommendation, string HarvestTiming, string AdvisoryLanguage, string AdvisoryText, string? SafetyDisclaimer, string? WeatherSummary, string? CropImpact);
public sealed record AdminSystemStatusDto(bool ApiHealthy, bool DatabaseConfigured, bool StorageConfigured, bool WeatherConfigured, bool WhatsAppConfigured, bool TelegramConfigured, bool InstagramConfigured, DateTime ServerUtc);
public sealed record AdminSessionDto(string UserId, string Name, string Email, IReadOnlyCollection<string> Roles);

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
}

public sealed class AdminReplayRequest
{
    public Guid ProcessingJobId { get; set; }
}

public sealed class WebhookEnvelopeDto
{
    public string RawBody { get; set; } = string.Empty;
    public IDictionary<string, string> Query { get; set; } = new Dictionary<string, string>();
    public IDictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();
    public string Path { get; set; } = string.Empty;
}
