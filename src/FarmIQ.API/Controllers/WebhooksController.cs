using System.Text;
using FarmIQ.Application.Abstractions;
using FarmIQ.Application.Contracts;
using FarmIQ.Infrastructure.Configuration;
using FarmIQ.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FarmIQ.API.Controllers;

[ApiController]
[Route("api/webhooks")]
public sealed class WebhooksController(
    IMessageChannelResolver channelResolver,
    IMessageIngestionService messageIngestionService,
    IOptions<WebhookOptions> webhookOptions) : ControllerBase
{
    [HttpGet("whatsapp")]
    public IActionResult VerifyWhatsApp([FromQuery(Name = "hub.verify_token")] string? verifyToken, [FromQuery(Name = "hub.challenge")] string? challenge)
    {
        if (verifyToken == webhookOptions.Value.WhatsAppVerifyToken)
        {
            return Content(challenge ?? "verified", "text/plain");
        }

        return Unauthorized();
    }

    [HttpPost("whatsapp")]
    public Task<IActionResult> ReceiveWhatsApp(CancellationToken cancellationToken) =>
        ReceiveAsync(ChannelType.WhatsApp, cancellationToken);

    [HttpGet("telegram")]
    public IActionResult VerifyTelegram([FromHeader(Name = "X-Telegram-Bot-Api-Secret-Token")] string? token)
    {
        return token == webhookOptions.Value.TelegramSecretToken ? Ok(new { status = "verified" }) : Unauthorized();
    }

    [HttpPost("telegram")]
    public Task<IActionResult> ReceiveTelegram(CancellationToken cancellationToken) =>
        ReceiveAsync(ChannelType.Telegram, cancellationToken);

    [HttpGet("instagram")]
    public IActionResult VerifyInstagram([FromQuery(Name = "hub.verify_token")] string? verifyToken, [FromQuery(Name = "hub.challenge")] string? challenge)
    {
        if (verifyToken == webhookOptions.Value.InstagramVerifyToken)
        {
            return Content(challenge ?? "verified", "text/plain");
        }

        return Unauthorized();
    }

    [HttpPost("instagram")]
    public Task<IActionResult> ReceiveInstagram(CancellationToken cancellationToken) =>
        ReceiveAsync(ChannelType.Instagram, cancellationToken);

    private async Task<IActionResult> ReceiveAsync(ChannelType channelType, CancellationToken cancellationToken)
    {
        Request.EnableBuffering();

        string rawBody;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true))
        {
            rawBody = await reader.ReadToEndAsync(cancellationToken);
            Request.Body.Position = 0;
        }

        var envelope = new WebhookEnvelopeDto
        {
            RawBody = rawBody,
            Query = Request.Query.ToDictionary(x => x.Key, x => x.Value.ToString()),
            Headers = Request.Headers.ToDictionary(x => x.Key, x => x.Value.ToString()),
            Path = Request.Path
        };

        var service = channelResolver.Resolve(channelType);
        var command = await service.ParseAsync(envelope, cancellationToken);
        var accepted = await messageIngestionService.AcceptAsync(command, cancellationToken);

        if (accepted.IsDuplicate)
        {
            return Ok(new
            {
                duplicate = true,
                accepted.ExistingInboundMessageId
            });
        }

        return Accepted(new
        {
            accepted.AcceptedMessage.InboundMessageId,
            accepted.AcceptedMessage.ProcessingJobId,
            accepted.AcceptedMessage.Status
        });
    }
}
