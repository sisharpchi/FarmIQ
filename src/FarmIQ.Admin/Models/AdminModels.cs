using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using FarmIQ.Shared;

namespace FarmIQ.Admin.Models;

public sealed class TokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = "Bearer";

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}

public sealed class AuthSessionModel
{
    public string AccessToken { get; set; } = string.Empty;
    public DateTime ExpiresUtc { get; set; }
    public UserSessionModel User { get; set; } = new();
}

public sealed class UserSessionModel
{
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public IReadOnlyCollection<string> Roles { get; set; } = Array.Empty<string>();
}

public sealed record ConversationSummaryModel(Guid ConversationId, string FarmerId, string FarmerName, ChannelType ChannelType, DateTime LastMessageUtc, int InboundCount, int OutboundCount);
public sealed record ConversationMessageModel(Guid MessageId, string Direction, string? Text, MessageLifecycleStatus? InboundStatus, OutboundDeliveryStatus? OutboundStatus, DateTime CreatedUtc);
public sealed record ConversationDetailModel(Guid ConversationId, string FarmerName, string FarmerId, ChannelType ChannelType, DateTime LastMessageUtc, IReadOnlyCollection<ConversationMessageModel> Messages);
public sealed record ProcessingJobSummaryModel(Guid JobId, Guid InboundMessageId, ProcessingJobStatus Status, int Attempts, string? LastError, DateTime ScheduledUtc);
public sealed record DeliveryIssueSummaryModel(Guid DeliveryId, ChannelType ChannelType, string ExternalMessageId, bool IsDuplicate, Guid? InboundMessageId, DateTime CreatedUtc);
public sealed record StuckJobSummaryModel(Guid JobId, string? LeaseOwner, DateTime? LeaseExpiresUtc, int Attempts, string? LastError);
public sealed record AdvisorySummaryModel(Guid AdvisoryId, string DiseaseName, decimal ConfidenceScore, string AdvisoryLanguage, string AdvisoryText);
public sealed record AdvisoryDetailModel(Guid AdvisoryId, string DiseaseName, decimal ConfidenceScore, string TreatmentRecommendation, string HarvestTiming, string AdvisoryLanguage, string AdvisoryText, string? SafetyDisclaimer, string? WeatherSummary, string? CropImpact);
public sealed record AnalyticsSummaryModel(int TotalFarmers, int TotalConversations, int TotalInboundMessages, int TotalOutboundMessages, int FailedJobs, int CompletedAdvisories, int DuplicateDeliveries, int StuckJobs);
public sealed record SystemStatusModel(bool ApiHealthy, bool DatabaseConfigured, bool StorageConfigured, bool WeatherConfigured, bool WhatsAppConfigured, bool TelegramConfigured, bool InstagramConfigured, DateTime ServerUtc);
public sealed record InsightSummaryModel(int TotalFarmers, int TotalConversations, int CompletedAdvisories, int FailedJobs);

public sealed class PagedResponseModel<T>
{
    public IReadOnlyCollection<T> Items { get; set; } = Array.Empty<T>();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public sealed class LoginRequestModel
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}

public sealed class SignupRequestModel
{
    [Required, StringLength(120, MinimumLength = 2)]
    public string DisplayName { get; set; } = string.Empty;

    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(100, MinimumLength = 8)]
    public string Password { get; set; } = string.Empty;

    [Required, Compare(nameof(Password))]
    public string ConfirmPassword { get; set; } = string.Empty;
}
