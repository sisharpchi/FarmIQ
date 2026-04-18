using FarmIQ.Application.Abstractions;
using FarmIQ.Application.Contracts;
using FarmIQ.Application.Services;
using FarmIQ.Core.Entities;
using FarmIQ.Infrastructure.Configuration;
using FarmIQ.Infrastructure.Persistence;
using FarmIQ.Infrastructure.Services;
using FarmIQ.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FarmIQ.Tests;

public sealed class FarmWorkflowTests
{
    [Fact]
    public async Task AcceptAsync_ShouldPersistInboundMessageAndQueueProcessingJob()
    {
        await using var dbContext = CreateDbContext();
        var queue = new FakeBackgroundJobQueue();
        var service = CreateIngestionService(dbContext, queue);

        var result = await service.AcceptAsync(new NormalizedInboundMessageCommand
        {
            ChannelType = ChannelType.Telegram,
            ExternalUserId = "farmer-1",
            ExternalConversationId = "chat-1",
            ExternalMessageId = "msg-1",
            Text = "Leaves have dark spots",
            IncomingLanguage = "en",
            Media =
            [
                new InboundMediaDto
                {
                    MediaType = MediaType.Image,
                    ExternalMediaId = "image-1",
                    Url = "https://example.com/leaf.jpg",
                    FileName = "leaf.jpg",
                    ContentType = "image/jpeg"
                }
            ]
        });

        result.IsDuplicate.Should().BeFalse();
        result.AcceptedMessage.Status.Should().Be(MessageLifecycleStatus.Queued);
        dbContext.InboundMessages.Should().HaveCount(1);
        dbContext.ProcessingJobs.Should().HaveCount(1);
        dbContext.WebhookDeliveries.Should().HaveCount(1);
        queue.JobIds.Should().ContainSingle().Which.Should().Be(result.AcceptedMessage.ProcessingJobId);
    }

    [Fact]
    public async Task AcceptAsync_ShouldDeduplicateRepeatedWebhookDelivery()
    {
        await using var dbContext = CreateDbContext();
        var queue = new FakeBackgroundJobQueue();
        var service = CreateIngestionService(dbContext, queue);
        var command = new NormalizedInboundMessageCommand
        {
            ChannelType = ChannelType.WhatsApp,
            ExternalUserId = "farmer-1",
            ExternalConversationId = "wa-chat-1",
            ExternalMessageId = "wamid.1",
            Text = "maize leaves have dark spots",
            IncomingLanguage = "en"
        };

        var first = await service.AcceptAsync(command);
        var second = await service.AcceptAsync(command);

        first.IsDuplicate.Should().BeFalse();
        second.IsDuplicate.Should().BeTrue();
        dbContext.InboundMessages.Should().HaveCount(1);
        dbContext.ProcessingJobs.Should().HaveCount(1);
        dbContext.WebhookDeliveries.Should().HaveCount(1);
    }

    [Fact]
    public async Task AcceptAsync_ShouldReplyImmediatelyToStartCommandWithoutQueueingAdvisory()
    {
        await using var dbContext = CreateDbContext();
        var queue = new FakeBackgroundJobQueue();
        var service = CreateIngestionService(dbContext, queue);

        var result = await service.AcceptAsync(new NormalizedInboundMessageCommand
        {
            ChannelType = ChannelType.Telegram,
            ExternalUserId = "farmer-start",
            ExternalConversationId = "chat-start",
            ExternalMessageId = "msg-start",
            Text = "/start",
            IncomingLanguage = "en"
        });

        result.AcceptedMessage.Status.Should().Be(MessageLifecycleStatus.Replied);
        dbContext.ProcessingJobs.Should().BeEmpty();
        dbContext.OutboundMessages.Should().ContainSingle();
        dbContext.InboundMessages.Single().DetectedIntent.Should().Be(InboundIntentType.StartCommand);
        queue.JobIds.Should().BeEmpty();
    }

    [Fact]
    public async Task AcceptAsync_ShouldTreatGreetingAsFollowUpPromptInsteadOfAdvisory()
    {
        await using var dbContext = CreateDbContext();
        var queue = new FakeBackgroundJobQueue();
        var service = CreateIngestionService(dbContext, queue);

        var result = await service.AcceptAsync(new NormalizedInboundMessageCommand
        {
            ChannelType = ChannelType.Telegram,
            ExternalUserId = "farmer-greeting",
            ExternalConversationId = "chat-greeting",
            ExternalMessageId = "msg-greeting",
            Text = "hey",
            IncomingLanguage = "en"
        });

        result.AcceptedMessage.Status.Should().Be(MessageLifecycleStatus.Replied);
        dbContext.ProcessingJobs.Should().BeEmpty();
        dbContext.OutboundMessages.Should().ContainSingle();
        dbContext.InboundMessages.Single().DetectedIntent.Should().Be(InboundIntentType.Greeting);
    }

    [Fact]
    public async Task RetryJobAsync_ShouldResetStatusAndRequeue()
    {
        await using var dbContext = CreateDbContext();
        var queue = new FakeBackgroundJobQueue();
        var failedJob = new ProcessingJob
        {
            InboundMessage = BuildInboundMessage(ChannelType.WhatsApp, "msg"),
            Status = ProcessingJobStatus.Failed,
            LastError = "boom",
            Attempts = 2
        };

        dbContext.ProcessingJobs.Add(failedJob);
        await dbContext.SaveChangesAsync();

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ChannelApis:WhatsAppBaseUrl"] = "https://graph.facebook.com",
            ["ChannelApis:TelegramBaseUrl"] = "https://api.telegram.org",
            ["ChannelApis:InstagramBaseUrl"] = "https://graph.facebook.com",
            ["Storage:RootPath"] = "App_Data/media",
            ["OpenWeatherMap:BaseUrl"] = "https://api.openweathermap.org",
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=farmiq;Username=postgres;Password=postgres"
        }).Build();
        var service = new AdminQueryService(new UnitOfWork(dbContext), queue, configuration);
        await service.RetryJobAsync(failedJob.Id);

        var updated = await dbContext.ProcessingJobs.FindAsync(failedJob.Id);
        updated!.Status.Should().Be(ProcessingJobStatus.Retrying);
        updated.LastError.Should().BeNull();
        updated.NextAttemptUtc.Should().NotBeNull();
        queue.JobIds.Should().Contain(failedJob.Id);
    }

    [Fact]
    public async Task ClaimNextAsync_ShouldLeasePendingJob()
    {
        await using var dbContext = CreateDbContext();
        var job = new ProcessingJob
        {
            InboundMessage = BuildInboundMessage(ChannelType.Telegram, "msg-lease"),
            Status = ProcessingJobStatus.Pending,
            NextAttemptUtc = DateTime.UtcNow.AddMinutes(-1)
        };

        dbContext.ProcessingJobs.Add(job);
        await dbContext.SaveChangesAsync();

        var leaseService = new ProcessingJobLeaseService(new UnitOfWork(dbContext), new FakeRuntimeSettings(5));
        var claimed = await leaseService.ClaimNextAsync("worker-1");

        claimed.Should().NotBeNull();
        claimed!.LeaseOwner.Should().Be("worker-1");
        claimed.LeaseToken.Should().NotBeNullOrWhiteSpace();
        claimed.LeaseExpiresUtc.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public async Task ClaimNextAsync_ShouldLeaseExpiredInProgressJob()
    {
        await using var dbContext = CreateDbContext();
        var job = new ProcessingJob
        {
            InboundMessage = BuildInboundMessage(ChannelType.Instagram, "msg-expired"),
            Status = ProcessingJobStatus.InProgress,
            LeaseExpiresUtc = DateTime.UtcNow.AddMinutes(-2),
            NextAttemptUtc = DateTime.UtcNow.AddMinutes(-2)
        };

        dbContext.ProcessingJobs.Add(job);
        await dbContext.SaveChangesAsync();

        var leaseService = new ProcessingJobLeaseService(new UnitOfWork(dbContext), new FakeRuntimeSettings(5));
        var claimed = await leaseService.ClaimNextAsync("worker-a");

        claimed.Should().NotBeNull();
        claimed!.LeaseOwner.Should().Be("worker-a");
        claimed.Status.Should().Be(ProcessingJobStatus.InProgress);
    }

    [Fact]
    public async Task TelegramService_ShouldParseVoiceAndCaptionPayload()
    {
        var service = new TelegramService(new FakeHttpClientFactory(), Options.Create(new ChannelApiOptions()), NullLogger<TelegramService>.Instance);
        var command = await service.ParseAsync(new WebhookEnvelopeDto
        {
            RawBody = """
            {
              "update_id": 1,
              "message": {
                "message_id": 99,
                "caption": "cassava leaves curling",
                "chat": { "id": 12345 },
                "from": { "id": 456, "first_name": "Asha", "language_code": "en" },
                "voice": { "file_id": "voice-file-1" }
              }
            }
            """
        });

        command.ExternalMessageId.Should().Be("99");
        command.Media.Should().ContainSingle(x => x.MediaType == MediaType.Voice);
        command.Text.Should().Be("cassava leaves curling");
    }

    [Fact]
    public async Task TelegramService_ShouldParseLocationPayload()
    {
        var service = new TelegramService(new FakeHttpClientFactory(), Options.Create(new ChannelApiOptions()), NullLogger<TelegramService>.Instance);
        var command = await service.ParseAsync(new WebhookEnvelopeDto
        {
            RawBody = """
            {
              "update_id": 2,
              "message": {
                "message_id": 100,
                "chat": { "id": 12345 },
                "from": { "id": 456, "first_name": "Asha", "language_code": "en" },
                "location": { "latitude": 41.2995, "longitude": 69.2401 }
              }
            }
            """
        });

        command.HasLocation.Should().BeTrue();
        command.Latitude.Should().Be(41.2995);
        command.Longitude.Should().Be(69.2401);
        command.IsUnsupportedEvent.Should().BeFalse();
    }

    [Fact]
    public async Task WhatsAppService_ShouldParseImagePayload()
    {
        var service = new WhatsAppService(
            new FakeHttpClientFactory(),
            Options.Create(new ChannelApiOptions()),
            Options.Create(new WebhookOptions()),
            NullLogger<WhatsAppService>.Instance);

        var command = await service.ParseAsync(new WebhookEnvelopeDto
        {
            RawBody = """
            {
              "entry": [
                {
                  "changes": [
                    {
                      "value": {
                        "metadata": { "phone_number_id": "phone-1" },
                        "contacts": [{ "wa_id": "255700000001", "profile": { "name": "Farmer One" } }],
                        "messages": [
                          {
                            "id": "wamid.abc",
                            "from": "255700000001",
                            "type": "image",
                            "image": { "id": "media-1", "caption": "maize leaf spots" }
                          }
                        ]
                      }
                    }
                  ]
                }
              ]
            }
            """
        });

        command.ExternalMessageId.Should().Be("wamid.abc");
        command.Media.Should().ContainSingle(x => x.MediaType == MediaType.Image);
        command.Text.Should().Be("maize leaf spots");
    }

    [Fact]
    public async Task InstagramService_ShouldParseMessageWebhook()
    {
        var service = new InstagramService(
            new FakeHttpClientFactory(),
            Options.Create(new ChannelApiOptions()),
            Options.Create(new WebhookOptions()),
            NullLogger<InstagramService>.Instance);

        var command = await service.ParseAsync(new WebhookEnvelopeDto
        {
            RawBody = """
            {
              "entry": [
                {
                  "messaging": [
                    {
                      "sender": { "id": "ig-user-1" },
                      "recipient": { "id": "page-1" },
                      "message": {
                        "mid": "ig-mid-1",
                        "text": "banana leaves drying"
                      }
                    }
                  ]
                }
              ]
            }
            """
        });

        command.ExternalUserId.Should().Be("ig-user-1");
        command.Text.Should().Be("banana leaves drying");
        command.IsUnsupportedEvent.Should().BeFalse();
    }

    private static FarmIQDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FarmIQDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new FarmIQDbContext(options);
    }

    private static InboundMessage BuildInboundMessage(ChannelType channelType, string externalMessageId) =>
        new()
        {
            Conversation = new Conversation
            {
                ChannelType = channelType,
                ExternalConversationId = $"conv-{externalMessageId}",
                ExternalUserId = "farmer",
                FarmerProfile = new FarmerProfile
                {
                    ExternalFarmerId = "farmer"
                }
            },
            ChannelType = channelType,
            ExternalMessageId = externalMessageId,
            RawPayloadJson = "{}",
            Status = MessageLifecycleStatus.Queued
        };

    private static MessageIngestionService CreateIngestionService(FarmIQDbContext dbContext, FakeBackgroundJobQueue queue) =>
        new(
            new UnitOfWork(dbContext),
            queue,
            new InboundIntentClassifier(),
            new ConversationResponseComposer(new MockLanguageService()),
            new FakeMessageChannelResolver());

    private sealed class FakeBackgroundJobQueue : IBackgroundJobQueue
    {
        public List<Guid> JobIds { get; } = [];

        public ValueTask QueueAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            JobIds.Add(jobId);
            return ValueTask.CompletedTask;
        }

        public ValueTask WaitForSignalAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class FakeRuntimeSettings(int leaseDurationMinutes) : IProcessingRuntimeSettings
    {
        public int LeaseDurationMinutes { get; } = leaseDurationMinutes;
    }

    private sealed class FakeMessageChannelResolver : IMessageChannelResolver
    {
        public IMessageChannelService Resolve(ChannelType channelType) => new FakeMessageChannelService(channelType);
    }

    private sealed class FakeMessageChannelService(ChannelType channelType) : IMessageChannelService
    {
        public ChannelType ChannelType { get; } = channelType;

        public Task<NormalizedInboundMessageCommand> ParseAsync(WebhookEnvelopeDto envelope, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ChannelSendResult> SendReplyAsync(ChannelReplyRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChannelSendResult(true, Guid.NewGuid().ToString("N"), null));
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new FakeHttpMessageHandler());
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            });
    }
}
