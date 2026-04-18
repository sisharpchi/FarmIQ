using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FarmIQ.Application.Abstractions;
using FarmIQ.Application.Contracts;
using FarmIQ.Infrastructure.Configuration;
using FarmIQ.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FarmIQ.Infrastructure.Services;

public abstract class MessageChannelServiceBase : IMessageChannelService
{
    public abstract ChannelType ChannelType { get; }

    public abstract Task<NormalizedInboundMessageCommand> ParseAsync(WebhookEnvelopeDto envelope, CancellationToken cancellationToken = default);

    public abstract Task<ChannelSendResult> SendReplyAsync(ChannelReplyRequest request, CancellationToken cancellationToken = default);

    protected static string? GetHeader(WebhookEnvelopeDto envelope, string key) =>
        envelope.Headers.TryGetValue(key, out var value) ? value : null;

    protected static string BuildCorrelationId(ChannelType channelType, string externalMessageId) =>
        $"{channelType.ToString().ToLowerInvariant()}-{externalMessageId}";

    protected static string? GetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : property.Value.ToString();
            }
        }

        return null;
    }

    protected static JsonElement? GetProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }

    protected static double? GetDouble(JsonElement element, string propertyName)
    {
        var property = GetProperty(element, propertyName);
        if (property is null)
        {
            return null;
        }

        if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetDouble(out var numericValue))
        {
            return numericValue;
        }

        if (property.Value.ValueKind == JsonValueKind.String && double.TryParse(property.Value.GetString(), out var parsedValue))
        {
            return parsedValue;
        }

        return null;
    }

    protected static string HexHmac(string secret, string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
    }

    protected static NormalizedInboundMessageCommand CreateUnsupportedCommand(
        ChannelType channelType,
        string rawBody,
        IDictionary<string, string> headers,
        IDictionary<string, string> query,
        string eventType,
        string ignoredReason,
        string? externalUserId = null,
        string? externalConversationId = null,
        string? externalMessageId = null) =>
        new()
        {
            ChannelType = channelType,
            ExternalMessageId = externalMessageId ?? $"{channelType.ToString().ToLowerInvariant()}-unsupported-{Guid.NewGuid():N}",
            ExternalConversationId = externalConversationId ?? $"{channelType.ToString().ToLowerInvariant()}-conversation",
            ExternalUserId = externalUserId ?? $"{channelType.ToString().ToLowerInvariant()}-user",
            OriginalPayloadJson = rawBody,
            EventType = eventType,
            IsUnsupportedEvent = true,
            IgnoredReason = ignoredReason,
            CorrelationId = BuildCorrelationId(channelType, externalMessageId ?? Guid.NewGuid().ToString("N")),
            Metadata = headers.Concat(query).ToDictionary(x => x.Key, x => x.Value)
        };
}

public sealed class WhatsAppService(
    IHttpClientFactory httpClientFactory,
    IOptions<ChannelApiOptions> channelOptions,
    IOptions<WebhookOptions> webhookOptions,
    ILogger<WhatsAppService> logger) : MessageChannelServiceBase
{
    public override ChannelType ChannelType => ChannelType.WhatsApp;

    public override Task<NormalizedInboundMessageCommand> ParseAsync(WebhookEnvelopeDto envelope, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(webhookOptions.Value.WhatsAppAppSecret) &&
            envelope.Headers.TryGetValue("X-Hub-Signature-256", out var signature))
        {
            var expected = $"sha256={HexHmac(webhookOptions.Value.WhatsAppAppSecret, envelope.RawBody)}";
            if (!string.Equals(expected, signature, StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException("Invalid WhatsApp signature.");
            }
        }

        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(envelope.RawBody) ? "{}" : envelope.RawBody);
        var root = document.RootElement;
        if (!root.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() == 0)
        {
            return Task.FromResult(CreateUnsupportedCommand(
                ChannelType.WhatsApp,
                envelope.RawBody,
                envelope.Headers,
                envelope.Query,
                eventType: "unsupported",
                ignoredReason: "missing_entry"));
        }

        var entry = entries[0];
        if (!entry.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array || changes.GetArrayLength() == 0)
        {
            return Task.FromResult(CreateUnsupportedCommand(
                ChannelType.WhatsApp,
                envelope.RawBody,
                envelope.Headers,
                envelope.Query,
                eventType: "unsupported",
                ignoredReason: "missing_changes"));
        }

        var change = changes[0];
        if (!change.TryGetProperty("value", out var value))
        {
            return Task.FromResult(CreateUnsupportedCommand(
                ChannelType.WhatsApp,
                envelope.RawBody,
                envelope.Headers,
                envelope.Query,
                eventType: "unsupported",
                ignoredReason: "missing_value"));
        }

        var messages = value.TryGetProperty("messages", out var msgArray) ? msgArray : default;
        var contacts = value.TryGetProperty("contacts", out var contactArray) ? contactArray : default;

        if (messages.ValueKind != JsonValueKind.Array || messages.GetArrayLength() == 0)
        {
            return Task.FromResult(CreateUnsupportedCommand(
                ChannelType.WhatsApp,
                envelope.RawBody,
                envelope.Headers,
                envelope.Query,
                eventType: "unsupported",
                ignoredReason: "missing_message",
                externalUserId: contacts.ValueKind == JsonValueKind.Array && contacts.GetArrayLength() > 0 ? GetString(contacts[0], "wa_id") : null,
                externalConversationId: value.TryGetProperty("metadata", out var metadata) ? GetString(metadata, "phone_number_id") : null));
        }

        var message = messages[0];
        var externalMessageId = GetString(message, "id") ?? Guid.NewGuid().ToString("N");
        var externalUserId = GetString(message, "from") ?? "whatsapp-user";
        var conversationId = value.TryGetProperty("metadata", out var conversationMetadata)
            ? GetString(conversationMetadata, "phone_number_id") ?? externalUserId
            : externalUserId;
        var type = GetString(message, "type") ?? "text";

        var command = new NormalizedInboundMessageCommand
        {
            ChannelType = ChannelType.WhatsApp,
            ExternalUserId = externalUserId,
            ExternalConversationId = conversationId,
            ExternalMessageId = externalMessageId,
            OriginalPayloadJson = envelope.RawBody,
            IncomingLanguage = contacts.ValueKind == JsonValueKind.Array && contacts.GetArrayLength() > 0 ? GetString(contacts[0], "language") : null,
            DisplayName = contacts.ValueKind == JsonValueKind.Array && contacts.GetArrayLength() > 0 && contacts[0].TryGetProperty("profile", out var profile) ? GetString(profile, "name") : null,
            EventType = type,
            CorrelationId = BuildCorrelationId(ChannelType.WhatsApp, externalMessageId),
            Metadata = envelope.Headers.Concat(envelope.Query).ToDictionary(x => x.Key, x => x.Value)
        };

        if (type == "text" && message.TryGetProperty("text", out var text))
        {
            command.Text = GetString(text, "body");
        }
        else if (type == "image" && message.TryGetProperty("image", out var image))
        {
            command.Text = GetString(image, "caption");
            command.Media.Add(new InboundMediaDto
            {
                ChannelType = ChannelType.WhatsApp,
                MediaType = MediaType.Image,
                ExternalMediaId = GetString(image, "id") ?? $"{externalMessageId}-image",
                Url = GetString(image, "id") ?? "whatsapp-media",
                FileName = "whatsapp-image.jpg",
                ContentType = "image/jpeg"
            });
        }
        else if ((type == "audio" || type == "voice") && message.TryGetProperty("audio", out var audio))
        {
            command.Media.Add(new InboundMediaDto
            {
                ChannelType = ChannelType.WhatsApp,
                MediaType = MediaType.Voice,
                ExternalMediaId = GetString(audio, "id") ?? $"{externalMessageId}-audio",
                Url = GetString(audio, "id") ?? "whatsapp-audio",
                FileName = "whatsapp-voice.ogg",
                ContentType = "audio/ogg"
            });
        }
        else if (type == "location" && message.TryGetProperty("location", out var location))
        {
            command.Latitude = GetDouble(location, "latitude");
            command.Longitude = GetDouble(location, "longitude");
            command.HasLocation = command.Latitude.HasValue && command.Longitude.HasValue;
            command.EventType = "location";
        }
        else
        {
            command.IsUnsupportedEvent = true;
        }

        logger.LogInformation("Parsed WhatsApp webhook message {MessageId}", externalMessageId);
        return Task.FromResult(command);
    }

    public override async Task<ChannelSendResult> SendReplyAsync(ChannelReplyRequest request, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            messaging_product = "whatsapp",
            to = request.RecipientId,
            type = "text",
            text = new { body = request.Message }
        });

        if (string.IsNullOrWhiteSpace(channelOptions.Value.WhatsAppAccessToken) || string.IsNullOrWhiteSpace(channelOptions.Value.WhatsAppPhoneNumberId))
        {
            logger.LogInformation("WhatsApp credentials not configured. Simulating outbound payload: {Payload}", payload);
            return new ChannelSendResult(true, Guid.NewGuid().ToString("N"), null);
        }

        var client = httpClientFactory.CreateClient(nameof(WhatsAppService));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", channelOptions.Value.WhatsAppAccessToken);
        var url = $"{channelOptions.Value.WhatsAppBaseUrl.TrimEnd('/')}/{channelOptions.Value.WhatsAppPhoneNumberId}/messages";
        var response = await client.PostAsync(url, new StringContent(payload, Encoding.UTF8, "application/json"), cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        return response.IsSuccessStatusCode
            ? new ChannelSendResult(true, Guid.NewGuid().ToString("N"), null)
            : new ChannelSendResult(false, null, responseBody);
    }
}

public sealed class TelegramService(
    IHttpClientFactory httpClientFactory,
    IOptions<ChannelApiOptions> channelOptions,
    ILogger<TelegramService> logger) : MessageChannelServiceBase
{
    public override ChannelType ChannelType => ChannelType.Telegram;

    public override Task<NormalizedInboundMessageCommand> ParseAsync(WebhookEnvelopeDto envelope, CancellationToken cancellationToken = default)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(envelope.RawBody) ? "{}" : envelope.RawBody);
        var root = document.RootElement;
        if (!root.TryGetProperty("message", out var message) && !root.TryGetProperty("edited_message", out message))
        {
            return Task.FromResult(CreateUnsupportedCommand(
                ChannelType.Telegram,
                envelope.RawBody,
                envelope.Headers,
                envelope.Query,
                eventType: "unsupported",
                ignoredReason: "missing_message"));
        }

        if (!message.TryGetProperty("chat", out var chat) || !message.TryGetProperty("from", out var from))
        {
            return Task.FromResult(CreateUnsupportedCommand(
                ChannelType.Telegram,
                envelope.RawBody,
                envelope.Headers,
                envelope.Query,
                eventType: "unsupported",
                ignoredReason: "missing_chat_or_sender"));
        }

        var externalMessageId = GetString(message, "message_id") ?? Guid.NewGuid().ToString("N");
        var command = new NormalizedInboundMessageCommand
        {
            ChannelType = ChannelType.Telegram,
            ExternalUserId = GetString(from, "id") ?? "telegram-user",
            ExternalConversationId = GetString(chat, "id") ?? "telegram-chat",
            ExternalMessageId = externalMessageId,
            OriginalPayloadJson = envelope.RawBody,
            IncomingLanguage = GetString(from, "language_code"),
            DisplayName = $"{GetString(from, "first_name")} {GetString(from, "last_name")}".Trim(),
            EventType = "message",
            CorrelationId = BuildCorrelationId(ChannelType.Telegram, externalMessageId),
            Metadata = envelope.Headers.Concat(envelope.Query).ToDictionary(x => x.Key, x => x.Value)
        };

        command.Text = GetString(message, "text") ?? GetString(message, "caption");

        if (message.TryGetProperty("photo", out var photos) && photos.ValueKind == JsonValueKind.Array && photos.GetArrayLength() > 0)
        {
            var photo = photos[photos.GetArrayLength() - 1];
            command.Media.Add(new InboundMediaDto
            {
                ChannelType = ChannelType.Telegram,
                MediaType = MediaType.Image,
                ExternalMediaId = GetString(photo, "file_id") ?? $"{externalMessageId}-photo",
                Url = GetString(photo, "file_id") ?? "telegram-photo",
                FileName = "telegram-photo.jpg",
                ContentType = "image/jpeg"
            });
        }

        if (message.TryGetProperty("voice", out var voice))
        {
            command.Media.Add(new InboundMediaDto
            {
                ChannelType = ChannelType.Telegram,
                MediaType = MediaType.Voice,
                ExternalMediaId = GetString(voice, "file_id") ?? $"{externalMessageId}-voice",
                Url = GetString(voice, "file_id") ?? "telegram-voice",
                FileName = "telegram-voice.ogg",
                ContentType = "audio/ogg"
            });
        }

        if (message.TryGetProperty("location", out var location))
        {
            command.Latitude = GetDouble(location, "latitude");
            command.Longitude = GetDouble(location, "longitude");
            command.HasLocation = command.Latitude.HasValue && command.Longitude.HasValue;
            command.EventType = "location";
        }

        if (string.IsNullOrWhiteSpace(command.Text) && command.Media.Count == 0 && !command.HasLocation)
        {
            command.IsUnsupportedEvent = true;
            command.EventType = "unsupported";
        }

        logger.LogInformation("Parsed Telegram webhook message {MessageId}", externalMessageId);
        return Task.FromResult(command);
    }

    public override async Task<ChannelSendResult> SendReplyAsync(ChannelReplyRequest request, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            chat_id = request.RecipientId,
            text = request.Message
        });

        if (string.IsNullOrWhiteSpace(channelOptions.Value.TelegramBotToken))
        {
            logger.LogInformation("Telegram token not configured. Simulating outbound payload: {Payload}", payload);
            return new ChannelSendResult(true, Guid.NewGuid().ToString("N"), null);
        }

        var client = httpClientFactory.CreateClient(nameof(TelegramService));
        var url = $"{channelOptions.Value.TelegramBaseUrl.TrimEnd('/')}/bot{channelOptions.Value.TelegramBotToken}/sendMessage";
        var response = await client.PostAsync(url, new StringContent(payload, Encoding.UTF8, "application/json"), cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        return response.IsSuccessStatusCode
            ? new ChannelSendResult(true, Guid.NewGuid().ToString("N"), null)
            : new ChannelSendResult(false, null, responseBody);
    }
}

public sealed class InstagramService(
    IHttpClientFactory httpClientFactory,
    IOptions<ChannelApiOptions> channelOptions,
    IOptions<WebhookOptions> webhookOptions,
    ILogger<InstagramService> logger) : MessageChannelServiceBase
{
    public override ChannelType ChannelType => ChannelType.Instagram;

    public override Task<NormalizedInboundMessageCommand> ParseAsync(WebhookEnvelopeDto envelope, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(webhookOptions.Value.InstagramAppSecret) &&
            envelope.Headers.TryGetValue("X-Hub-Signature-256", out var signature))
        {
            var expected = $"sha256={HexHmac(webhookOptions.Value.InstagramAppSecret, envelope.RawBody)}";
            if (!string.Equals(expected, signature, StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException("Invalid Instagram signature.");
            }
        }

        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(envelope.RawBody) ? "{}" : envelope.RawBody);
        var root = document.RootElement;
        if (!root.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() == 0)
        {
            return Task.FromResult(CreateUnsupportedCommand(
                ChannelType.Instagram,
                envelope.RawBody,
                envelope.Headers,
                envelope.Query,
                eventType: "unsupported",
                ignoredReason: "missing_entry"));
        }

        var entry = entries[0];
        if (!entry.TryGetProperty("messaging", out var messagingArray) || messagingArray.ValueKind != JsonValueKind.Array || messagingArray.GetArrayLength() == 0)
        {
            return Task.FromResult(CreateUnsupportedCommand(
                ChannelType.Instagram,
                envelope.RawBody,
                envelope.Headers,
                envelope.Query,
                eventType: "unsupported",
                ignoredReason: "missing_messaging"));
        }

        var messaging = messagingArray[0];
        var sender = messaging.TryGetProperty("sender", out var senderElement) ? senderElement : default;
        var recipient = messaging.TryGetProperty("recipient", out var recipientElement) ? recipientElement : default;
        if (!messaging.TryGetProperty("message", out var message))
        {
            return Task.FromResult(CreateUnsupportedCommand(
                ChannelType.Instagram,
                envelope.RawBody,
                envelope.Headers,
                envelope.Query,
                eventType: GetString(messaging, "type") ?? "unsupported",
                ignoredReason: "missing_message",
                externalUserId: GetString(sender, "id"),
                externalConversationId: GetString(recipient, "id")));
        }

        var externalMessageId = GetString(message, "mid") ?? Guid.NewGuid().ToString("N");
        var command = new NormalizedInboundMessageCommand
        {
            ChannelType = ChannelType.Instagram,
            ExternalUserId = GetString(sender, "id") ?? "instagram-user",
            ExternalConversationId = GetString(recipient, "id") ?? "instagram-thread",
            ExternalMessageId = externalMessageId,
            OriginalPayloadJson = envelope.RawBody,
            EventType = "message",
            CorrelationId = BuildCorrelationId(ChannelType.Instagram, externalMessageId),
            Metadata = envelope.Headers.Concat(envelope.Query).ToDictionary(x => x.Key, x => x.Value)
        };

        command.Text = GetString(message, "text");

        if (message.TryGetProperty("attachments", out var attachments) && attachments.ValueKind == JsonValueKind.Array)
        {
            foreach (var attachment in attachments.EnumerateArray())
            {
                var type = GetString(attachment, "type");
                var payload = attachment.GetProperty("payload");
                if (type == "image")
                {
                    command.Media.Add(new InboundMediaDto
                    {
                        ChannelType = ChannelType.Instagram,
                        MediaType = MediaType.Image,
                        ExternalMediaId = GetString(payload, "url") ?? $"{externalMessageId}-image",
                        Url = GetString(payload, "url") ?? "instagram-image",
                        FileName = "instagram-image.jpg",
                        ContentType = "image/jpeg"
                    });
                }
            }
        }

        if (string.IsNullOrWhiteSpace(command.Text) && command.Media.Count == 0 && !command.HasLocation)
        {
            command.IsUnsupportedEvent = true;
            command.EventType = "unsupported";
        }

        logger.LogInformation("Parsed Instagram webhook message {MessageId}", externalMessageId);
        return Task.FromResult(command);
    }

    public override async Task<ChannelSendResult> SendReplyAsync(ChannelReplyRequest request, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            recipient = new { id = request.RecipientId },
            message = new { text = request.Message },
            messaging_type = "RESPONSE"
        });

        if (string.IsNullOrWhiteSpace(channelOptions.Value.InstagramAccessToken) || string.IsNullOrWhiteSpace(channelOptions.Value.InstagramPageId))
        {
            logger.LogInformation("Instagram credentials not configured. Simulating outbound payload: {Payload}", payload);
            return new ChannelSendResult(true, Guid.NewGuid().ToString("N"), null);
        }

        var client = httpClientFactory.CreateClient(nameof(InstagramService));
        var url = $"{channelOptions.Value.InstagramBaseUrl.TrimEnd('/')}/{channelOptions.Value.InstagramPageId}/messages?access_token={channelOptions.Value.InstagramAccessToken}";
        var response = await client.PostAsync(url, new StringContent(payload, Encoding.UTF8, "application/json"), cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        return response.IsSuccessStatusCode
            ? new ChannelSendResult(true, Guid.NewGuid().ToString("N"), null)
            : new ChannelSendResult(false, null, responseBody);
    }
}

public sealed class MessageChannelResolver(IEnumerable<IMessageChannelService> services) : IMessageChannelResolver
{
    private readonly IReadOnlyDictionary<ChannelType, IMessageChannelService> _services = services.ToDictionary(x => x.ChannelType);

    public IMessageChannelService Resolve(ChannelType channelType) =>
        _services.TryGetValue(channelType, out var service)
            ? service
            : throw new InvalidOperationException($"No channel service is registered for {channelType}.");
}
