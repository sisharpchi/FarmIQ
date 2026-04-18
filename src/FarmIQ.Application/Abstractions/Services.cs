using System.Linq.Expressions;
using FarmIQ.Application.Contracts;
using FarmIQ.Core.Common;
using FarmIQ.Core.Entities;
using FarmIQ.Shared;

namespace FarmIQ.Application.Abstractions;

public interface IGenericRepository<T> where T : BaseEntity
{
    IQueryable<T> Query();
    Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);
    Task AddAsync(T entity, CancellationToken cancellationToken = default);
    void Update(T entity);
    void Remove(T entity);
}

public interface IUnitOfWork
{
    IGenericRepository<T> Repository<T>() where T : BaseEntity;
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IMessageChannelService
{
    ChannelType ChannelType { get; }
    Task<NormalizedInboundMessageCommand> ParseAsync(WebhookEnvelopeDto envelope, CancellationToken cancellationToken = default);
    Task<ChannelSendResult> SendReplyAsync(ChannelReplyRequest request, CancellationToken cancellationToken = default);
}

public interface IMessageChannelResolver
{
    IMessageChannelService Resolve(ChannelType channelType);
}

public interface IBackgroundJobQueue
{
    ValueTask QueueAsync(Guid jobId, CancellationToken cancellationToken = default);
    ValueTask WaitForSignalAsync(CancellationToken cancellationToken);
}

public interface IMediaStorageService
{
    Task<MediaStorageResult> SaveRemoteMediaAsync(InboundMediaDto media, CancellationToken cancellationToken = default);
}

public interface ISpeechToTextService
{
    Task<string?> TranscribeAsync(IEnumerable<InboundMediaDto> media, CancellationToken cancellationToken = default);
}

public interface ILanguageService
{
    Task<string> DetectLanguageAsync(string text, CancellationToken cancellationToken = default);
    Task<string> TranslateToEnglishAsync(string text, string sourceLanguage, CancellationToken cancellationToken = default);
    Task<string> TranslateFromEnglishAsync(string text, string targetLanguage, CancellationToken cancellationToken = default);
}

public interface ICropAnalysisService
{
    Task<CropAnalysisResult> AnalyzeAsync(string inputText, IEnumerable<InboundMediaDto> media, CancellationToken cancellationToken = default);
}

public interface IWeatherService
{
    Task<WeatherSummaryDto> GetSummaryAsync(double? latitude, double? longitude, CancellationToken cancellationToken = default);
}

public interface IMessageIngestionService
{
    Task<InboundAcceptanceResult> AcceptAsync(NormalizedInboundMessageCommand command, CancellationToken cancellationToken = default);
}

public interface IInboundIntentClassifier
{
    IntentClassificationResult Classify(NormalizedInboundMessageCommand command, Conversation? conversation);
}

public interface IConversationResponseComposer
{
    Task<ComposedConversationResponse?> ComposeImmediateResponseAsync(
        NormalizedInboundMessageCommand command,
        FarmerProfile farmer,
        Conversation conversation,
        CancellationToken cancellationToken = default);
}

public interface IAdvisoryWorkflowService
{
    Task ProcessAsync(Guid processingJobId, CancellationToken cancellationToken = default);
}

public interface IProcessingJobLeaseService
{
    Task<ProcessingJob?> ClaimNextAsync(string workerId, CancellationToken cancellationToken = default);
    Task MarkRetryAsync(Guid processingJobId, string errorMessage, CancellationToken cancellationToken = default);
}

public interface IProcessingRuntimeSettings
{
    int LeaseDurationMinutes { get; }
}

public interface IAdminQueryService
{
    Task<PagedResponse<ConversationSummaryDto>> GetConversationsAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task<PagedResponse<ProcessingJobSummaryDto>> GetJobsAsync(ProcessingJobStatus? status, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<PagedResponse<AdvisorySummaryDto>> GetAdvisoriesAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ConversationDetailDto?> GetConversationDetailAsync(Guid conversationId, CancellationToken cancellationToken = default);
    Task<AdvisoryDetailDto?> GetAdvisoryDetailAsync(Guid advisoryId, CancellationToken cancellationToken = default);
    Task<PagedResponse<DeliveryIssueSummaryDto>> GetDeliveryIssuesAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task<PagedResponse<StuckJobSummaryDto>> GetStuckJobsAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task<AnalyticsSummaryDto> GetAnalyticsAsync(CancellationToken cancellationToken = default);
    Task<AdminSystemStatusDto> GetSystemStatusAsync(CancellationToken cancellationToken = default);
    Task<AdminSessionDto> GetSessionAsync(System.Security.Claims.ClaimsPrincipal user);
    Task RetryJobAsync(Guid processingJobId, CancellationToken cancellationToken = default);
}

public interface IAdminUserManagementService
{
    Task<PagedResponse<AdminUserSummaryDto>> GetUsersAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task<AdminUserSummaryDto> CreateUserAsync(AdminCreateUserRequest request, string actor, string? correlationId, CancellationToken cancellationToken = default);
    Task<AdminUserSummaryDto> DisableUserAsync(Guid userId, string actor, string? correlationId, CancellationToken cancellationToken = default);
    Task<AdminUserSummaryDto> EnableUserAsync(Guid userId, string actor, string? correlationId, CancellationToken cancellationToken = default);
    Task<AdminUserSummaryDto> ResetPasswordAsync(Guid userId, AdminResetPasswordRequest request, string actor, string? correlationId, CancellationToken cancellationToken = default);
    Task<AdminUserSummaryDto> UpdateRolesAsync(Guid userId, AdminUpdateUserRolesRequest request, string actor, string? correlationId, CancellationToken cancellationToken = default);
}
