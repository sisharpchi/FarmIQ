using FarmIQ.Core.Common;
using FarmIQ.Shared;

namespace FarmIQ.Core.Entities;

public sealed class FarmerProfile : BaseEntity
{
    public string ExternalFarmerId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string PreferredLanguage { get; set; } = "en";
    public string? Region { get; set; }
    public string? Country { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? TenantKey { get; set; }
    public ICollection<Conversation> Conversations { get; set; } = new List<Conversation>();
    public ICollection<CropAdvisory> Advisories { get; set; } = new List<CropAdvisory>();
}

public sealed class Conversation : BaseEntity
{
    public Guid FarmerProfileId { get; set; }
    public FarmerProfile FarmerProfile { get; set; } = null!;
    public ChannelType ChannelType { get; set; }
    public string ExternalConversationId { get; set; } = string.Empty;
    public string ExternalUserId { get; set; } = string.Empty;
    public string? TenantKey { get; set; }
    public DateTime LastMessageUtc { get; set; } = DateTime.UtcNow;
    public ICollection<InboundMessage> InboundMessages { get; set; } = new List<InboundMessage>();
    public ICollection<OutboundMessage> OutboundMessages { get; set; } = new List<OutboundMessage>();
}

public sealed class InboundMessage : BaseEntity
{
    public Guid ConversationId { get; set; }
    public Conversation Conversation { get; set; } = null!;
    public ChannelType ChannelType { get; set; }
    public string ExternalMessageId { get; set; } = string.Empty;
    public string RawPayloadJson { get; set; } = "{}";
    public string? OriginalText { get; set; }
    public string? TranscribedText { get; set; }
    public string? AdvisoryInputText { get; set; }
    public string? OriginalLanguage { get; set; }
    public string? NormalizedMetadataJson { get; set; }
    public bool IsDuplicateDelivery { get; set; }
    public bool IsUnsupportedEvent { get; set; }
    public string? DuplicateOfMessageId { get; set; }
    public MessageLifecycleStatus Status { get; set; } = MessageLifecycleStatus.Received;
    public DateTime ReceivedUtc { get; set; } = DateTime.UtcNow;
    public ICollection<MediaAsset> MediaAssets { get; set; } = new List<MediaAsset>();
    public ICollection<ProcessingJob> ProcessingJobs { get; set; } = new List<ProcessingJob>();
    public ICollection<CropAdvisory> Advisories { get; set; } = new List<CropAdvisory>();
}

public sealed class MediaAsset : BaseEntity
{
    public Guid InboundMessageId { get; set; }
    public InboundMessage InboundMessage { get; set; } = null!;
    public MediaType MediaType { get; set; }
    public string ExternalMediaId { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public long? SizeBytes { get; set; }
    public bool IsDownloaded { get; set; }
    public string? StoragePath { get; set; }
    public string? StorageUrl { get; set; }
}

public sealed class CropAdvisory : BaseEntity
{
    public Guid FarmerProfileId { get; set; }
    public FarmerProfile FarmerProfile { get; set; } = null!;
    public Guid InboundMessageId { get; set; }
    public InboundMessage InboundMessage { get; set; } = null!;
    public AdvisoryStatus Status { get; set; } = AdvisoryStatus.Draft;
    public string AdvisoryLanguage { get; set; } = "en";
    public string AdvisoryText { get; set; } = string.Empty;
    public string? FollowUpQuestion { get; set; }
    public string? SafetyDisclaimer { get; set; }
    public AdvisoryDiagnosis Diagnosis { get; set; } = null!;
    public WeatherSnapshot? WeatherSnapshot { get; set; }
}

public sealed class AdvisoryDiagnosis : BaseEntity
{
    public Guid CropAdvisoryId { get; set; }
    public CropAdvisory CropAdvisory { get; set; } = null!;
    public string DiseaseName { get; set; } = string.Empty;
    public decimal ConfidenceScore { get; set; }
    public string TreatmentRecommendation { get; set; } = string.Empty;
    public string HarvestTiming { get; set; } = string.Empty;
}

public sealed class WeatherSnapshot : BaseEntity
{
    public Guid CropAdvisoryId { get; set; }
    public CropAdvisory CropAdvisory { get; set; } = null!;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public decimal TemperatureCelsius { get; set; }
    public decimal RainProbability { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string CropImpact { get; set; } = string.Empty;
}

public sealed class OutboundMessage : BaseEntity
{
    public Guid ConversationId { get; set; }
    public Conversation Conversation { get; set; } = null!;
    public Guid? InboundMessageId { get; set; }
    public InboundMessage? InboundMessage { get; set; }
    public ChannelType ChannelType { get; set; }
    public string Body { get; set; } = string.Empty;
    public string? ExternalMessageId { get; set; }
    public OutboundDeliveryStatus DeliveryStatus { get; set; } = OutboundDeliveryStatus.Pending;
    public string? FailureReason { get; set; }
    public DateTime? SentUtc { get; set; }
}

public sealed class ChannelConnection : BaseEntity
{
    public ChannelType ChannelType { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ExternalAccountId { get; set; } = string.Empty;
    public string? TenantKey { get; set; }
    public bool IsActive { get; set; } = true;
    public string? MetadataJson { get; set; }
}

public sealed class ProcessingJob : BaseEntity
{
    public Guid InboundMessageId { get; set; }
    public InboundMessage InboundMessage { get; set; } = null!;
    public string JobType { get; set; } = "advisory";
    public ProcessingJobStatus Status { get; set; } = ProcessingJobStatus.Pending;
    public int Attempts { get; set; }
    public int MaxAttempts { get; set; } = 5;
    public string? LastError { get; set; }
    public string? LeaseToken { get; set; }
    public string? LeaseOwner { get; set; }
    public DateTime? LeaseExpiresUtc { get; set; }
    public DateTime? NextAttemptUtc { get; set; }
    public bool IsTerminalFailure { get; set; }
    public string? DeadLetterReason { get; set; }
    public DateTime ScheduledUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
}

public sealed class WebhookDelivery : BaseEntity
{
    public ChannelType ChannelType { get; set; }
    public string ExternalMessageId { get; set; } = string.Empty;
    public string DeliveryKey { get; set; } = string.Empty;
    public Guid? InboundMessageId { get; set; }
    public InboundMessage? InboundMessage { get; set; }
    public bool IsDuplicate { get; set; }
    public string? RawPayloadJson { get; set; }
    public string? CorrelationId { get; set; }
}

public sealed class AuditLog : BaseEntity
{
    public string EntityName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Actor { get; set; } = "system";
    public string? CorrelationId { get; set; }
    public string PayloadJson { get; set; } = "{}";
}
