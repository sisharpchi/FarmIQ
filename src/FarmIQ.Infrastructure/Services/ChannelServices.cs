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

public abstract class MessageChannelServiceBase(ILogger logger) : IMessageChannelService
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

    protected static string HexHmac(string secret, string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
    }
}

public sealed class WhatsAppService(
    IHttpClientFactory httpClientFactory,
    IOptions<ChannelApiOptions> channelOptions,
    IOptions<WebhookOptions> webhookOptions,
    ILogger<WhatsAppService> logger) : MessageChannelServiceBase(logger)
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
                throw new InvalidOperationException("Invalid WhatsApp signature.");
            }
        }

        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(envelope.RawBody) ? "{}" : envelope.RawBody);
        var root = document.RootElement;
        var entry = root.GetProperty("entry")[0];
        var change = entry.GetProperty("changes")[0];
        var value = change.GetProperty("value");
        var messages = value.TryGetProperty("messages", out var msgArray) ? msgArray : default;
        var contacts = value.TryGetProperty("contacts", out var contactArray) ? contactArray : default;

        if (messages.ValueKind != JsonValueKind.Array || messages.GetArrayLength() == 0)
        {
            return Task.FromResult(new NormalizedInboundMessageCommand
            {
                ChannelType = ChannelType.WhatsApp,
                ExternalMessageId = $"wa-unsupported-{Guid.NewGuid():N}",
                ExternalConversationId = value.TryGetProperty("metadata", out var metadata) ? GetString(metadata, "phone_number_id") ?? "whatsapp-conversation" : "whatsapp-conversation",
                ExternalUserId = contacts.ValueKind == JsonValueKind.Array && contacts.GetArrayLength() > 0 ? GetString(contacts[0], "wa_id") ?? "whatsapp-user" : "whatsapp-user",
                OriginalPayloadJson = envelope.RawBody,
                EventType = "unsupported",
                IsUnsupportedEvent = true,
                CorrelationId = BuildCorrelationId(ChannelType.WhatsApp, Guid.NewGuid().ToString("N"))
            });
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
            DisplayName = contacts.ValueKind == JsonValueKind.Array && contacts.GetArrayLength() > 0 ? GetString(contacts[0].GetProperty("profile"), "name") : null,
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
                MediaType = MediaType.Voice,
                ExternalMediaId = GetString(audio, "id") ?? $"{externalMessageId}-audio",
                Url = GetString(audio, "id") ?? "whatsapp-audio",
                FileName = "whatsapp-voice.ogg",
                ContentType = "audio/ogg"
            });
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
    ILogger<TelegramService> logger) : MessageChannelServiceBase(logger)
{
    public override ChannelType ChannelType => ChannelType.Telegram;

    public override Task<NormalizedInboundMessageCommand> ParseAsync(WebhookEnvelopeDto envelope, CancellationToken cancellationToken = default)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(envelope.RawBody) ? "{}" : envelope.RawBody);
        var root = document.RootElement;
        var message = root.TryGetProperty("message", out var messageElement) ? messageElement : root.GetProperty("edited_message");
        var chat = message.GetProperty("chat");
        var from = message.GetProperty("from");
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
                MediaType = MediaType.Voice,
                ExternalMediaId = GetString(voice, "file_id") ?? $"{externalMessageId}-voice",
                Url = GetString(voice, "file_id") ?? "telegram-voice",
                FileName = "telegram-voice.ogg",
                ContentType = "audio/ogg"
            });
        }

        if (string.IsNullOrWhiteSpace(command.Text) && command.Media.Count == 0)
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
    ILogger<InstagramService> logger) : MessageChannelServiceBase(logger)
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
                throw new InvalidOperationException("Invalid Instagram signature.");
            }
        }

        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(envelope.RawBody) ? "{}" : envelope.RawBody);
        var root = document.RootElement;
        var entry = root.GetProperty("entry")[0];
        var messaging = entry.GetProperty("messaging")[0];
        var sender = messaging.GetProperty("sender");
        var recipient = messaging.GetProperty("recipient");
        var externalMessageId = GetString(messaging.GetProperty("message"), "mid") ?? Guid.NewGuid().ToString("N");
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

        var message = messaging.GetProperty("message");
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
                        MediaType = MediaType.Image,
                        ExternalMediaId = GetString(payload, "url") ?? $"{externalMessageId}-image",
                        Url = GetString(payload, "url") ?? "instagram-image",
                        FileName = "instagram-image.jpg",
                        ContentType = "image/jpeg"
                    });
                }
            }
        }

        if (string.IsNullOrWhiteSpace(command.Text) && command.Media.Count == 0)
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
