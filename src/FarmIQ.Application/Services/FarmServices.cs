using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FarmIQ.Application.Abstractions;
using FarmIQ.Application.Contracts;
using FarmIQ.Core.Entities;
using FarmIQ.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace FarmIQ.Application.Services;

public sealed class MessageIngestionService(
    IUnitOfWork unitOfWork,
    IBackgroundJobQueue backgroundJobQueue,
    IInboundIntentClassifier inboundIntentClassifier,
    IConversationResponseComposer conversationResponseComposer,
    IMessageChannelResolver messageChannelResolver) : IMessageIngestionService
{
    public async Task<InboundAcceptanceResult> AcceptAsync(NormalizedInboundMessageCommand command, CancellationToken cancellationToken = default)
    {
        var farmerRepository = unitOfWork.Repository<FarmerProfile>();
        var conversationRepository = unitOfWork.Repository<Conversation>();
        var inboundRepository = unitOfWork.Repository<InboundMessage>();
        var outboundRepository = unitOfWork.Repository<OutboundMessage>();
        var jobRepository = unitOfWork.Repository<ProcessingJob>();
        var deliveryRepository = unitOfWork.Repository<WebhookDelivery>();

        var deliveryKey = $"{command.ChannelType}:{command.ExternalMessageId}";
        var existingDelivery = await deliveryRepository.FirstOrDefaultAsync(
            x => x.DeliveryKey == deliveryKey,
            cancellationToken);

        if (existingDelivery is not null)
        {
            existingDelivery.IsDuplicate = true;
            deliveryRepository.Update(existingDelivery);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return new InboundAcceptanceResult
            {
                IsDuplicate = true,
                ExistingInboundMessageId = existingDelivery.InboundMessageId,
                AcceptedMessage = new InboundMessageAcceptedDto(
                    existingDelivery.InboundMessageId ?? Guid.Empty,
                    Guid.Empty,
                    MessageLifecycleStatus.Stored)
            };
        }

        var now = DateTime.UtcNow;
        var farmer = await farmerRepository.FirstOrDefaultAsync(
            x => x.ExternalFarmerId == command.ExternalUserId,
            cancellationToken);

        if (farmer is null)
        {
                farmer = new FarmerProfile
                {
                    ExternalFarmerId = command.ExternalUserId,
                    DisplayName = string.IsNullOrWhiteSpace(command.DisplayName) ? command.ExternalUserId : command.DisplayName,
                    PreferredLanguage = FarmLanguages.Normalize(command.IncomingLanguage),
                    Latitude = command.Latitude,
                    Longitude = command.Longitude,
                    TenantKey = command.TenantKey
                };

            await farmerRepository.AddAsync(farmer, cancellationToken);
        }
        else
        {
            farmer.DisplayName = string.IsNullOrWhiteSpace(command.DisplayName) ? farmer.DisplayName : command.DisplayName;
            farmer.PreferredLanguage = string.IsNullOrWhiteSpace(command.IncomingLanguage)
                ? farmer.PreferredLanguage
                : FarmLanguages.Normalize(command.IncomingLanguage);
            if (command.HasLocation)
            {
                farmer.Latitude = command.Latitude;
                farmer.Longitude = command.Longitude;
            }

            farmer.UpdatedUtc = now;
            farmerRepository.Update(farmer);
        }

        var conversation = await conversationRepository.FirstOrDefaultAsync(
            x => x.ExternalConversationId == command.ExternalConversationId && x.ChannelType == command.ChannelType,
            cancellationToken);

        if (conversation is null)
        {
            conversation = new Conversation
            {
                FarmerProfile = farmer,
                ChannelType = command.ChannelType,
                ExternalConversationId = command.ExternalConversationId,
                ExternalUserId = command.ExternalUserId,
                TenantKey = command.TenantKey,
                LastMessageUtc = now,
                AssistantState = ConversationAssistantState.Idle
            };

            await conversationRepository.AddAsync(conversation, cancellationToken);
        }
        else
        {
            conversation.LastMessageUtc = now;
            conversation.UpdatedUtc = now;
            conversationRepository.Update(conversation);
        }

        var classification = inboundIntentClassifier.Classify(command, conversation);
        command.IntentType = classification.IntentType;
        command.IgnoredReason ??= classification.IgnoredReason;

        var inboundMessage = new InboundMessage
        {
            Conversation = conversation,
            ChannelType = command.ChannelType,
            ExternalMessageId = command.ExternalMessageId,
            RawPayloadJson = command.OriginalPayloadJson,
            OriginalText = command.Text,
            OriginalLanguage = command.IncomingLanguage,
            IsUnsupportedEvent = command.IsUnsupportedEvent || classification.IntentType == InboundIntentType.Unsupported,
            DetectedIntent = classification.IntentType,
            IgnoredReason = command.IgnoredReason,
            Status = MessageLifecycleStatus.Stored,
            NormalizedMetadataJson = JsonSerializer.Serialize(command.Metadata),
            MediaAssets = command.Media.Select(media => new MediaAsset
            {
                MediaType = media.MediaType,
                ExternalMediaId = media.ExternalMediaId,
                SourceUrl = media.Url,
                FileName = media.FileName,
                ContentType = media.ContentType,
                SizeBytes = media.SizeBytes
            }).ToList()
        };

        await inboundRepository.AddAsync(inboundMessage, cancellationToken);

        conversation.LastDetectedIntent = classification.IntentType;

        ProcessingJob? processingJob = null;
        OutboundMessage? outbound = null;
        ComposedConversationResponse? immediateResponse = null;

        if (classification.QueueForAdvisory)
        {
            processingJob = new ProcessingJob
            {
                InboundMessage = inboundMessage,
                Status = ProcessingJobStatus.Pending,
                JobType = "advisory",
                ScheduledUtc = now,
                NextAttemptUtc = now
            };

            conversation.AssistantState = ConversationAssistantState.AwaitingProblemDetails;
            inboundMessage.Status = MessageLifecycleStatus.Queued;
            await jobRepository.AddAsync(processingJob, cancellationToken);
        }
        else if (classification.SendImmediateResponse)
        {
            immediateResponse = await conversationResponseComposer.ComposeImmediateResponseAsync(command, farmer, conversation, cancellationToken);
            conversation.AssistantState = immediateResponse?.NextState ?? classification.NextState;
            inboundMessage.Status = immediateResponse is null ? MessageLifecycleStatus.Completed : MessageLifecycleStatus.Stored;

            if (immediateResponse is not null)
            {
                command.ImmediateResponseCandidate = immediateResponse.Message;
                outbound = new OutboundMessage
                {
                    Conversation = conversation,
                    InboundMessage = inboundMessage,
                    ChannelType = command.ChannelType,
                    Body = immediateResponse.Message,
                    DeliveryStatus = OutboundDeliveryStatus.Pending
                };

                await outboundRepository.AddAsync(outbound, cancellationToken);
            }
        }
        else
        {
            conversation.AssistantState = classification.NextState;
            inboundMessage.Status = MessageLifecycleStatus.Completed;
        }

        conversation.AssistantStateJson = BuildAssistantStateJson(
            conversation.AssistantState,
            classification.IntentType,
            farmer,
            pendingLocation: false,
            pendingPhoto: immediateResponse?.RequestedPhoto == true,
            analysisSource: null);

        await deliveryRepository.AddAsync(new WebhookDelivery
        {
            ChannelType = command.ChannelType,
            ExternalMessageId = command.ExternalMessageId,
            DeliveryKey = deliveryKey,
            InboundMessage = inboundMessage,
            RawPayloadJson = command.OriginalPayloadJson,
            CorrelationId = command.CorrelationId,
            IsDuplicate = false
        }, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (processingJob is not null)
        {
            await backgroundJobQueue.QueueAsync(processingJob.Id, cancellationToken);
        }
        else if (outbound is not null && immediateResponse is not null)
        {
            var channelService = messageChannelResolver.Resolve(command.ChannelType);
            var sendResult = await channelService.SendReplyAsync(
                new ChannelReplyRequest
                {
                    ChannelType = command.ChannelType,
                    ConversationId = conversation.Id,
                    RecipientId = conversation.ExternalUserId,
                    Message = immediateResponse.Message,
                    Metadata = new Dictionary<string, string>
                    {
                        ["intent"] = classification.IntentType.ToString()
                    }
                },
                cancellationToken);

            outbound.ExternalMessageId = sendResult.ExternalMessageId;
            outbound.DeliveryStatus = sendResult.Success ? OutboundDeliveryStatus.Sent : OutboundDeliveryStatus.Failed;
            outbound.FailureReason = sendResult.ErrorMessage;
            outbound.SentUtc = sendResult.Success ? DateTime.UtcNow : null;

            inboundMessage.Status = sendResult.Success ? MessageLifecycleStatus.Replied : MessageLifecycleStatus.Failed;
            if (sendResult.Success)
            {
                conversation.LastBotPromptUtc = DateTime.UtcNow;
            }

            conversation.AssistantState = immediateResponse.NextState;
            conversation.AssistantStateJson = BuildAssistantStateJson(
                conversation.AssistantState,
                classification.IntentType,
                farmer,
                pendingLocation: immediateResponse.RequestedLocation,
                pendingPhoto: immediateResponse.RequestedPhoto,
                analysisSource: null);

            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new InboundAcceptanceResult
        {
            AcceptedMessage = new InboundMessageAcceptedDto(
                inboundMessage.Id,
                processingJob?.Id ?? Guid.Empty,
                inboundMessage.Status)
        };
    }

    private static string BuildAssistantStateJson(
        ConversationAssistantState assistantState,
        InboundIntentType intentType,
        FarmerProfile farmer,
        bool pendingLocation,
        bool pendingPhoto,
        AdvisoryAnalysisSource? analysisSource)
    {
        return JsonSerializer.Serialize(new
        {
            assistantState,
            lastIntent = intentType,
            locationKnown = farmer.Latitude.HasValue && farmer.Longitude.HasValue,
            pendingLocation,
            pendingPhoto,
            analysisSource
        });
    }
}

public sealed class AdvisoryWorkflowService(
    IUnitOfWork unitOfWork,
    IMessageChannelResolver messageChannelResolver,
    IMediaStorageService mediaStorageService,
    ISpeechToTextService speechToTextService,
    ILanguageService languageService,
    ICropAnalysisService cropAnalysisService,
    IWeatherService weatherService) : IAdvisoryWorkflowService
{
    public async Task ProcessAsync(Guid processingJobId, CancellationToken cancellationToken = default)
    {
        var jobRepository = unitOfWork.Repository<ProcessingJob>();
        var job = await jobRepository.Query()
            .Include(x => x.InboundMessage)
                .ThenInclude(x => x.MediaAssets)
            .Include(x => x.InboundMessage)
                .ThenInclude(x => x.Conversation)
                    .ThenInclude(x => x.FarmerProfile)
            .FirstOrDefaultAsync(x => x.Id == processingJobId, cancellationToken);

        if (job is null || job.Status == ProcessingJobStatus.Completed)
        {
            return;
        }

        job.Status = ProcessingJobStatus.InProgress;
        job.Attempts += 1;
        job.StartedUtc = DateTime.UtcNow;
        job.InboundMessage.Status = MessageLifecycleStatus.Processing;
        await unitOfWork.SaveChangesAsync(cancellationToken);

        try
        {
            var inbound = job.InboundMessage;
            var conversation = inbound.Conversation;
            var farmer = conversation.FarmerProfile;

            var inboundMediaDtos = inbound.MediaAssets.Select(x => new InboundMediaDto
            {
                ChannelType = inbound.ChannelType,
                MediaType = x.MediaType,
                ExternalMediaId = x.ExternalMediaId,
                Url = x.SourceUrl,
                FileName = x.FileName,
                ContentType = x.ContentType,
                SizeBytes = x.SizeBytes,
                StoragePath = x.StoragePath,
                StorageUrl = x.StorageUrl
            }).ToList();

            foreach (var media in inbound.MediaAssets.Where(x => !x.IsDownloaded))
            {
                var dto = inboundMediaDtos.First(x => x.ExternalMediaId == media.ExternalMediaId);
                var stored = await mediaStorageService.SaveRemoteMediaAsync(dto, cancellationToken);

                media.StoragePath = stored.StoragePath;
                media.StorageUrl = stored.StorageUrl;
                media.SizeBytes = stored.SizeBytes;
                media.IsDownloaded = true;
                media.UpdatedUtc = DateTime.UtcNow;

                dto.StoragePath = stored.StoragePath;
                dto.StorageUrl = stored.StorageUrl;
                dto.SizeBytes = stored.SizeBytes;
            }

            var transcribedText = await speechToTextService.TranscribeAsync(
                inboundMediaDtos.Where(x => x.MediaType is MediaType.Voice or MediaType.Audio),
                cancellationToken);

            var combinedText = string.Join(
                " ",
                new[] { inbound.OriginalText, transcribedText }.Where(x => !string.IsNullOrWhiteSpace(x)));

            combinedText = string.IsNullOrWhiteSpace(combinedText)
                ? "Farmer sent crop media and needs an urgent advisory."
                : combinedText.Trim();

            var sourceLanguage = inbound.OriginalLanguage;
            if (string.IsNullOrWhiteSpace(sourceLanguage))
            {
                sourceLanguage = await languageService.DetectLanguageAsync(combinedText, cancellationToken);
            }
            sourceLanguage = FarmLanguages.Normalize(sourceLanguage);

            var englishInput = await languageService.TranslateToEnglishAsync(combinedText, sourceLanguage!, cancellationToken);
            var analysis = await cropAnalysisService.AnalyzeAsync(englishInput, inboundMediaDtos, cancellationToken);
            analysis.NeedsLocation |= !farmer.Latitude.HasValue || !farmer.Longitude.HasValue;

            if (analysis.ConfidenceScore < 0.70m && string.IsNullOrWhiteSpace(analysis.FollowUpQuestion))
            {
                analysis.FollowUpQuestion = "Send one closer photo of the affected leaves and stem base.";
            }

            if (analysis.ConfidenceScore < 0.70m && !inboundMediaDtos.Any(x => x.MediaType == MediaType.Image))
            {
                analysis.NeedsCloserPhoto = true;
            }

            var weather = farmer.Latitude.HasValue && farmer.Longitude.HasValue
                ? await weatherService.GetSummaryAsync(farmer.Latitude, farmer.Longitude, cancellationToken)
                : new WeatherSummaryDto();

            var responseLanguage = FarmLanguages.Normalize(
                string.IsNullOrWhiteSpace(farmer.PreferredLanguage)
                    ? sourceLanguage ?? FarmLanguages.English
                    : farmer.PreferredLanguage);

            var advisoryText = BuildAdvisoryText(analysis, weather, farmer.Latitude.HasValue && farmer.Longitude.HasValue);
            var localizedAdvisory = await languageService.TranslateFromEnglishAsync(advisoryText, responseLanguage, cancellationToken);

            inbound.TranscribedText = transcribedText;
            inbound.AdvisoryInputText = englishInput;

            var advisory = new CropAdvisory
            {
                FarmerProfileId = farmer.Id,
                InboundMessageId = inbound.Id,
                AdvisoryLanguage = responseLanguage,
                AdvisoryText = localizedAdvisory,
                FollowUpQuestion = analysis.FollowUpQuestion,
                SafetyDisclaimer = analysis.SafetyDisclaimer,
                ShortReasoningSummary = analysis.ShortReasoningSummary,
                AnalysisSource = analysis.AnalysisSource,
                NeedsCloserPhoto = analysis.NeedsCloserPhoto,
                NeedsLocation = analysis.NeedsLocation,
                Status = AdvisoryStatus.Ready,
                Diagnosis = new AdvisoryDiagnosis
                {
                    DiseaseName = analysis.DiseaseName,
                    ConfidenceScore = analysis.ConfidenceScore,
                    TreatmentRecommendation = analysis.TreatmentRecommendation,
                    HarvestTiming = analysis.HarvestTiming
                },
                WeatherSnapshot = new WeatherSnapshot
                {
                    Latitude = farmer.Latitude,
                    Longitude = farmer.Longitude,
                    TemperatureCelsius = weather.TemperatureCelsius,
                    RainProbability = weather.RainProbability,
                    Summary = weather.Summary,
                    CropImpact = weather.CropImpact
                }
            };

            await unitOfWork.Repository<CropAdvisory>().AddAsync(advisory, cancellationToken);

            var outbound = new OutboundMessage
            {
                ConversationId = inbound.ConversationId,
                InboundMessageId = inbound.Id,
                ChannelType = inbound.ChannelType,
                Body = localizedAdvisory,
                DeliveryStatus = OutboundDeliveryStatus.Pending
            };

            await unitOfWork.Repository<OutboundMessage>().AddAsync(outbound, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            var nextAssistantState = DetermineNextAssistantState(analysis, farmer.Latitude.HasValue && farmer.Longitude.HasValue);

            var channelService = messageChannelResolver.Resolve(inbound.ChannelType);
            var sendResult = await channelService.SendReplyAsync(
                new ChannelReplyRequest
                {
                    ChannelType = inbound.ChannelType,
                    ConversationId = inbound.ConversationId,
                    RecipientId = inbound.Conversation.ExternalUserId,
                    Message = localizedAdvisory,
                    Metadata = new Dictionary<string, string>
                    {
                        ["advisoryId"] = advisory.Id.ToString(),
                        ["confidence"] = analysis.ConfidenceScore.ToString("0.00"),
                        ["analysisSource"] = analysis.AnalysisSource.ToString()
                    }
                },
                cancellationToken);

            outbound.ExternalMessageId = sendResult.ExternalMessageId;
            outbound.DeliveryStatus = sendResult.Success ? OutboundDeliveryStatus.Sent : OutboundDeliveryStatus.Failed;
            outbound.FailureReason = sendResult.ErrorMessage;
            outbound.SentUtc = sendResult.Success ? DateTime.UtcNow : null;

            advisory.Status = sendResult.Success ? AdvisoryStatus.Sent : AdvisoryStatus.Failed;
            inbound.Status = sendResult.Success ? MessageLifecycleStatus.Replied : MessageLifecycleStatus.Failed;
            conversation.AssistantState = nextAssistantState;
            conversation.AssistantStateJson = BuildAssistantStateJson(
                conversation.AssistantState,
                inbound.DetectedIntent,
                farmer,
                pendingLocation: analysis.NeedsLocation && (!farmer.Latitude.HasValue || !farmer.Longitude.HasValue),
                pendingPhoto: analysis.NeedsCloserPhoto,
                analysisSource: analysis.AnalysisSource);

            if (sendResult.Success)
            {
                conversation.LastBotPromptUtc = DateTime.UtcNow;
                if (nextAssistantState == ConversationAssistantState.AwaitingLocation)
                {
                    conversation.LocationRequestedUtc = DateTime.UtcNow;
                    conversation.LastWeatherPromptUtc = DateTime.UtcNow;
                }
            }

            job.LeaseExpiresUtc = null;
            job.LeaseOwner = null;
            job.LeaseToken = null;
            job.NextAttemptUtc = null;
            job.Status = sendResult.Success ? ProcessingJobStatus.Completed : ProcessingJobStatus.Failed;
            job.CompletedUtc = DateTime.UtcNow;
            job.LastError = sendResult.ErrorMessage;
            job.IsTerminalFailure = !sendResult.Success;
            job.DeadLetterReason = sendResult.Success ? null : sendResult.ErrorMessage;

            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            job.LastError = exception.Message;
            job.InboundMessage.Status = MessageLifecycleStatus.Failed;

            if (job.Attempts >= job.MaxAttempts)
            {
                job.Status = ProcessingJobStatus.Failed;
                job.IsTerminalFailure = true;
                job.DeadLetterReason = exception.Message;
                job.CompletedUtc = DateTime.UtcNow;
                job.LeaseExpiresUtc = null;
                job.LeaseOwner = null;
                job.LeaseToken = null;
            }
            else
            {
                job.Status = ProcessingJobStatus.Retrying;
                job.NextAttemptUtc = DateTime.UtcNow.AddMinutes(Math.Min(job.Attempts * 2, 15));
                job.LeaseExpiresUtc = null;
                job.LeaseOwner = null;
                job.LeaseToken = null;
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    private static ConversationAssistantState DetermineNextAssistantState(CropAnalysisResult analysis, bool locationKnown)
    {
        if (analysis.NeedsCloserPhoto)
        {
            return ConversationAssistantState.AwaitingPhoto;
        }

        if (analysis.NeedsLocation && !locationKnown)
        {
            return ConversationAssistantState.AwaitingLocation;
        }

        return ConversationAssistantState.AdvisorySent;
    }

    private static string BuildAdvisoryText(CropAnalysisResult analysis, WeatherSummaryDto weather, bool locationKnown)
    {
        var confidenceLabel = analysis.ConfidenceScore >= 0.85m
            ? "High"
            : analysis.ConfidenceScore >= 0.70m
                ? "Moderate"
                : "Low";

        var lines = new List<string>();

        if (analysis.ConfidenceScore < 0.70m && !string.IsNullOrWhiteSpace(analysis.FollowUpQuestion))
        {
            lines.Add("I need one more detail before I can be fully confident.");
        }

        lines.Add($"Possible issue: {analysis.DiseaseName}");
        lines.Add($"Confidence: {confidenceLabel} ({analysis.ConfidenceScore:P0})");
        lines.Add($"What to do now: {analysis.TreatmentRecommendation}");
        lines.Add($"Harvest note: {analysis.HarvestTiming}");

        if (ShouldIncludeWeather(weather))
        {
            lines.Add($"Weather: {weather.Summary}");
            if (!string.IsNullOrWhiteSpace(weather.CropImpact))
            {
                lines.Add($"Crop impact: {weather.CropImpact}");
            }
        }

        var nextThing = BuildNextThingToSend(analysis, locationKnown);
        if (!string.IsNullOrWhiteSpace(nextThing))
        {
            lines.Add($"Next thing to send: {nextThing}");
        }

        if (!string.IsNullOrWhiteSpace(analysis.ShortReasoningSummary))
        {
            lines.Add($"Why this looks likely: {analysis.ShortReasoningSummary}");
        }

        if (!string.IsNullOrWhiteSpace(analysis.SafetyDisclaimer))
        {
            lines.Add($"Safety: {analysis.SafetyDisclaimer}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static bool ShouldIncludeWeather(WeatherSummaryDto weather)
    {
        if (string.IsNullOrWhiteSpace(weather.Summary))
        {
            return false;
        }

        return !weather.Summary.Contains("not configured", StringComparison.OrdinalIgnoreCase) &&
               !weather.Summary.Contains("unavailable", StringComparison.OrdinalIgnoreCase);
    }

    private static string? BuildNextThingToSend(CropAnalysisResult analysis, bool locationKnown)
    {
        var requests = new List<string>();

        if (analysis.NeedsCloserPhoto)
        {
            requests.Add("a closer photo of the affected leaves and stem base");
        }

        if (analysis.NeedsLocation && !locationKnown)
        {
            requests.Add("your location for rain and spray timing");
        }

        if (requests.Count == 0 && !string.IsNullOrWhiteSpace(analysis.FollowUpQuestion))
        {
            requests.Add(analysis.FollowUpQuestion.TrimEnd('.'));
        }

        return requests.Count switch
        {
            0 => null,
            1 => requests[0],
            _ => string.Join(" and ", requests)
        };
    }

    private static string BuildAssistantStateJson(
        ConversationAssistantState assistantState,
        InboundIntentType intentType,
        FarmerProfile farmer,
        bool pendingLocation,
        bool pendingPhoto,
        AdvisoryAnalysisSource analysisSource)
    {
        return JsonSerializer.Serialize(new
        {
            assistantState,
            lastIntent = intentType,
            locationKnown = farmer.Latitude.HasValue && farmer.Longitude.HasValue,
            pendingLocation,
            pendingPhoto,
            analysisSource
        });
    }
}

public sealed class AdminQueryService(IUnitOfWork unitOfWork, IBackgroundJobQueue backgroundJobQueue, IConfiguration configuration) : IAdminQueryService
{
    public async Task<PagedResponse<ConversationSummaryDto>> GetConversationsAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = unitOfWork.Repository<Conversation>().Query()
            .Include(x => x.FarmerProfile)
            .Include(x => x.InboundMessages)
            .Include(x => x.OutboundMessages)
            .OrderByDescending(x => x.LastMessageUtc);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new ConversationSummaryDto(
                x.Id,
                x.ExternalUserId,
                x.FarmerProfile.DisplayName,
                x.ChannelType,
                x.LastMessageUtc,
                x.InboundMessages.Count,
                x.OutboundMessages.Count,
                x.LastDetectedIntent,
                x.AssistantState,
                x.FarmerProfile.Latitude != null && x.FarmerProfile.Longitude != null))
            .ToListAsync(cancellationToken);

        return new PagedResponse<ConversationSummaryDto>(items, totalCount, page, pageSize);
    }

    public async Task<PagedResponse<ProcessingJobSummaryDto>> GetJobsAsync(ProcessingJobStatus? status, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = unitOfWork.Repository<ProcessingJob>().Query().OrderByDescending(x => x.CreatedUtc);

        if (status.HasValue)
        {
            query = query.Where(x => x.Status == status.Value).OrderByDescending(x => x.CreatedUtc);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new ProcessingJobSummaryDto(
                x.Id,
                x.InboundMessageId,
                x.Status,
                x.Attempts,
                x.LastError,
                x.ScheduledUtc,
                x.NextAttemptUtc,
                x.LeaseExpiresUtc,
                x.IsTerminalFailure,
                x.DeadLetterReason))
            .ToListAsync(cancellationToken);

        return new PagedResponse<ProcessingJobSummaryDto>(items, totalCount, page, pageSize);
    }

    public async Task<PagedResponse<AdvisorySummaryDto>> GetAdvisoriesAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = unitOfWork.Repository<CropAdvisory>().Query()
            .Include(x => x.Diagnosis)
            .OrderByDescending(x => x.CreatedUtc);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new AdvisorySummaryDto(
                x.Id,
                x.Diagnosis.DiseaseName,
                x.Diagnosis.ConfidenceScore,
                x.AdvisoryLanguage,
                x.AdvisoryText,
                x.AnalysisSource,
                x.NeedsLocation,
                x.NeedsCloserPhoto))
            .ToListAsync(cancellationToken);

        return new PagedResponse<AdvisorySummaryDto>(items, totalCount, page, pageSize);
    }

    public async Task<ConversationDetailDto?> GetConversationDetailAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        var conversation = await unitOfWork.Repository<Conversation>().Query()
            .Include(x => x.FarmerProfile)
            .Include(x => x.InboundMessages)
            .Include(x => x.OutboundMessages)
            .FirstOrDefaultAsync(x => x.Id == conversationId, cancellationToken);

        if (conversation is null)
        {
            return null;
        }

        var messages = conversation.InboundMessages
            .Select(x => new ConversationMessageDto(
                x.Id,
                "Inbound",
                x.OriginalText ?? x.TranscribedText ?? (x.DetectedIntent == InboundIntentType.LocationShare ? "Location shared" : null),
                x.Status,
                null,
                x.CreatedUtc,
                x.DetectedIntent,
                x.IgnoredReason))
            .Concat(conversation.OutboundMessages.Select(x => new ConversationMessageDto(
                x.Id,
                "Outbound",
                x.Body,
                null,
                x.DeliveryStatus,
                x.CreatedUtc,
                null,
                null)))
            .OrderBy(x => x.CreatedUtc)
            .ToList();

        return new ConversationDetailDto(
            conversation.Id,
            conversation.FarmerProfile.DisplayName,
            conversation.ExternalUserId,
            conversation.ChannelType,
            conversation.LastMessageUtc,
            conversation.AssistantState,
            conversation.LastDetectedIntent,
            conversation.FarmerProfile.Latitude != null && conversation.FarmerProfile.Longitude != null,
            conversation.LastBotPromptUtc,
            conversation.LocationRequestedUtc,
            messages);
    }

    public async Task<AdvisoryDetailDto?> GetAdvisoryDetailAsync(Guid advisoryId, CancellationToken cancellationToken = default)
    {
        var advisory = await unitOfWork.Repository<CropAdvisory>().Query()
            .Include(x => x.Diagnosis)
            .Include(x => x.WeatherSnapshot)
            .FirstOrDefaultAsync(x => x.Id == advisoryId, cancellationToken);

        if (advisory is null)
        {
            return null;
        }

        return new AdvisoryDetailDto(
            advisory.Id,
            advisory.Diagnosis.DiseaseName,
            advisory.Diagnosis.ConfidenceScore,
            advisory.Diagnosis.TreatmentRecommendation,
            advisory.Diagnosis.HarvestTiming,
            advisory.AdvisoryLanguage,
            advisory.AdvisoryText,
            advisory.SafetyDisclaimer,
            advisory.WeatherSnapshot?.Summary,
            advisory.WeatherSnapshot?.CropImpact,
            advisory.AnalysisSource,
            advisory.NeedsLocation,
            advisory.NeedsCloserPhoto,
            advisory.FollowUpQuestion,
            advisory.ShortReasoningSummary);
    }

    public async Task<PagedResponse<DeliveryIssueSummaryDto>> GetDeliveryIssuesAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = unitOfWork.Repository<WebhookDelivery>().Query()
            .Where(x => x.IsDuplicate)
            .OrderByDescending(x => x.CreatedUtc);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new DeliveryIssueSummaryDto(x.Id, x.ChannelType, x.ExternalMessageId, x.IsDuplicate, x.InboundMessageId, x.CreatedUtc))
            .ToListAsync(cancellationToken);

        return new PagedResponse<DeliveryIssueSummaryDto>(items, totalCount, page, pageSize);
    }

    public async Task<PagedResponse<StuckJobSummaryDto>> GetStuckJobsAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var query = unitOfWork.Repository<ProcessingJob>().Query()
            .Where(x => x.Status == ProcessingJobStatus.InProgress && x.LeaseExpiresUtc != null && x.LeaseExpiresUtc < now)
            .OrderByDescending(x => x.UpdatedUtc);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new StuckJobSummaryDto(x.Id, x.LeaseOwner, x.LeaseExpiresUtc, x.Attempts, x.LastError))
            .ToListAsync(cancellationToken);

        return new PagedResponse<StuckJobSummaryDto>(items, totalCount, page, pageSize);
    }

    public async Task<AnalyticsSummaryDto> GetAnalyticsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        return new AnalyticsSummaryDto
        {
            TotalFarmers = await unitOfWork.Repository<FarmerProfile>().Query().CountAsync(cancellationToken),
            TotalConversations = await unitOfWork.Repository<Conversation>().Query().CountAsync(cancellationToken),
            TotalInboundMessages = await unitOfWork.Repository<InboundMessage>().Query().CountAsync(cancellationToken),
            TotalOutboundMessages = await unitOfWork.Repository<OutboundMessage>().Query().CountAsync(cancellationToken),
            FailedJobs = await unitOfWork.Repository<ProcessingJob>().Query().CountAsync(x => x.Status == ProcessingJobStatus.Failed, cancellationToken),
            CompletedAdvisories = await unitOfWork.Repository<CropAdvisory>().Query().CountAsync(x => x.Status == AdvisoryStatus.Sent || x.Status == AdvisoryStatus.Ready, cancellationToken),
            DuplicateDeliveries = await unitOfWork.Repository<WebhookDelivery>().Query().CountAsync(x => x.IsDuplicate, cancellationToken),
            StuckJobs = await unitOfWork.Repository<ProcessingJob>().Query().CountAsync(x => x.Status == ProcessingJobStatus.InProgress && x.LeaseExpiresUtc != null && x.LeaseExpiresUtc < now, cancellationToken),
            CommandMessages = await unitOfWork.Repository<InboundMessage>().Query().CountAsync(x => x.DetectedIntent == InboundIntentType.StartCommand || x.DetectedIntent == InboundIntentType.HelpCommand, cancellationToken),
            GreetingMessages = await unitOfWork.Repository<InboundMessage>().Query().CountAsync(x => x.DetectedIntent == InboundIntentType.Greeting || x.DetectedIntent == InboundIntentType.SmallTalk, cancellationToken),
            FollowUpResponses = await unitOfWork.Repository<CropAdvisory>().Query().CountAsync(x => x.NeedsCloserPhoto || x.NeedsLocation || x.FollowUpQuestion != null, cancellationToken),
            OpenAiFallbacks = await unitOfWork.Repository<CropAdvisory>().Query().CountAsync(x => x.AnalysisSource == AdvisoryAnalysisSource.Fallback, cancellationToken)
        };
    }

    public Task<AdminSystemStatusDto> GetSystemStatusAsync(CancellationToken cancellationToken = default)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        var publicSignupEnabled = bool.TryParse(configuration["Auth:EnablePublicSignup"], out var signupEnabled) && signupEnabled;
        var workerPollIntervalSeconds = int.TryParse(configuration["Processing:PollIntervalSeconds"], out var pollIntervalSeconds)
            ? pollIntervalSeconds
            : 30;
        var openAiEnabled = bool.TryParse(configuration["OpenAI:Enabled"], out var enabled) && enabled;
        var openAiConfigured = openAiEnabled && !string.IsNullOrWhiteSpace(configuration["OpenAI:ApiKey"]);
        var glmEnabled = bool.TryParse(configuration["Glm:Enabled"], out var glmFlag) && glmFlag;
        var glmConfigured = glmEnabled && !string.IsNullOrWhiteSpace(configuration["Glm:ApiKey"]);
        return Task.FromResult(new AdminSystemStatusDto(
            ApiHealthy: true,
            DatabaseConfigured: !string.IsNullOrWhiteSpace(connectionString),
            StorageConfigured: !string.IsNullOrWhiteSpace(configuration["Storage:RootPath"]),
            WeatherConfigured: !string.IsNullOrWhiteSpace(configuration["OpenWeatherMap:BaseUrl"]) &&
                               !string.IsNullOrWhiteSpace(configuration["OpenWeatherMap:ApiKey"]),
            WhatsAppConfigured: !string.IsNullOrWhiteSpace(configuration["ChannelApis:WhatsAppBaseUrl"]),
            TelegramConfigured: !string.IsNullOrWhiteSpace(configuration["ChannelApis:TelegramBaseUrl"]),
            InstagramConfigured: !string.IsNullOrWhiteSpace(configuration["ChannelApis:InstagramBaseUrl"]),
            PublicSignupEnabled: publicSignupEnabled,
            WorkerPollIntervalSeconds: workerPollIntervalSeconds,
            OpenAiConfigured: openAiConfigured,
            GlmConfigured: glmConfigured,
            ServerUtc: DateTime.UtcNow));
    }

    public Task<AdminSessionDto> GetSessionAsync(ClaimsPrincipal user)
    {
        var roles = user.Claims.Where(x => x.Type is ClaimTypes.Role or "role").Select(x => x.Value).Distinct().ToArray();
        var userId = user.Claims.FirstOrDefault(x => x.Type is ClaimTypes.NameIdentifier or "sub")?.Value ?? string.Empty;
        var name = user.Claims.FirstOrDefault(x => x.Type is ClaimTypes.Name or "name")?.Value ?? "FarmIQ User";
        var email = user.Claims.FirstOrDefault(x => x.Type is ClaimTypes.Email or "email")?.Value ?? string.Empty;
        return Task.FromResult(new AdminSessionDto(userId, name, email, roles));
    }

    public async Task RetryJobAsync(Guid processingJobId, CancellationToken cancellationToken = default)
    {
        var repository = unitOfWork.Repository<ProcessingJob>();
        var job = await repository.GetByIdAsync(processingJobId, cancellationToken)
            ?? throw new InvalidOperationException("Processing job was not found.");

        job.Status = ProcessingJobStatus.Retrying;
        job.LastError = null;
        job.CompletedUtc = null;
        job.StartedUtc = null;
        job.ScheduledUtc = DateTime.UtcNow;
        job.NextAttemptUtc = DateTime.UtcNow;
        job.LeaseExpiresUtc = null;
        job.LeaseOwner = null;
        job.LeaseToken = null;
        job.IsTerminalFailure = false;
        job.DeadLetterReason = null;
        repository.Update(job);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await backgroundJobQueue.QueueAsync(job.Id, cancellationToken);
    }
}

public sealed class ProcessingJobLeaseService(IUnitOfWork unitOfWork, IProcessingRuntimeSettings processingRuntimeSettings) : IProcessingJobLeaseService
{
    public async Task<ProcessingJob?> ClaimNextAsync(string workerId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var repository = unitOfWork.Repository<ProcessingJob>();
        var job = await repository.Query()
            .Include(x => x.InboundMessage)
            .Where(x =>
                (x.Status == ProcessingJobStatus.Pending ||
                 x.Status == ProcessingJobStatus.Retrying ||
                 (x.Status == ProcessingJobStatus.InProgress && x.LeaseExpiresUtc < now)) &&
                !x.IsTerminalFailure &&
                (x.NextAttemptUtc == null || x.NextAttemptUtc <= now))
            .OrderBy(x => x.NextAttemptUtc ?? x.ScheduledUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (job is null)
        {
            return null;
        }

        job.Status = ProcessingJobStatus.InProgress;
        job.LeaseOwner = workerId;
        job.LeaseToken = Guid.NewGuid().ToString("N");
        job.LeaseExpiresUtc = now.AddMinutes(processingRuntimeSettings.LeaseDurationMinutes);
        job.StartedUtc ??= now;
        repository.Update(job);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return job;
    }

    public async Task MarkRetryAsync(Guid processingJobId, string errorMessage, CancellationToken cancellationToken = default)
    {
        var repository = unitOfWork.Repository<ProcessingJob>();
        var job = await repository.GetByIdAsync(processingJobId, cancellationToken);
        if (job is null)
        {
            return;
        }

        job.LastError = errorMessage;
        job.Status = job.Attempts >= job.MaxAttempts ? ProcessingJobStatus.Failed : ProcessingJobStatus.Retrying;
        job.IsTerminalFailure = job.Status == ProcessingJobStatus.Failed;
        job.DeadLetterReason = job.IsTerminalFailure ? errorMessage : null;
        job.NextAttemptUtc = job.IsTerminalFailure ? null : DateTime.UtcNow.AddMinutes(Math.Min(job.Attempts * 2, 15));
        job.LeaseOwner = null;
        job.LeaseToken = null;
        job.LeaseExpiresUtc = null;
        job.CompletedUtc = job.IsTerminalFailure ? DateTime.UtcNow : null;
        repository.Update(job);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
