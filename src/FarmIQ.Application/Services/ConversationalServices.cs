using FarmIQ.Application.Abstractions;
using FarmIQ.Application.Contracts;
using FarmIQ.Core.Entities;
using FarmIQ.Shared;

namespace FarmIQ.Application.Services;

public sealed class InboundIntentClassifier : IInboundIntentClassifier
{
    private static readonly string[] GreetingTokens =
    [
        "hi",
        "hello",
        "hey",
        "salom",
        "assalomu alaykum",
        "assalamu alaykum",
        "habari",
        "hola"
    ];

    private static readonly string[] SmallTalkTokens =
    [
        "ok",
        "thanks",
        "thank you",
        "good",
        "fine",
        "test"
    ];

    private static readonly string[] SymptomSignals =
    [
        "spot",
        "spots",
        "yellow",
        "brown",
        "black",
        "curl",
        "curly",
        "wilting",
        "wilt",
        "dry",
        "drying",
        "rot",
        "mildew",
        "fung",
        "blight",
        "aphid",
        "aphids",
        "mites",
        "worm",
        "worms",
        "pest",
        "disease",
        "leaf",
        "leaves",
        "stem",
        "stain",
        "stained",
        "breaking",
        "branch",
        "branches"
    ];

    public IntentClassificationResult Classify(NormalizedInboundMessageCommand command, Conversation? conversation)
    {
        if (command.IsUnsupportedEvent)
        {
            command.IntentType = InboundIntentType.Unsupported;
            command.IgnoredReason = "unsupported_event";
            return new IntentClassificationResult(
                InboundIntentType.Unsupported,
                QueueForAdvisory: false,
                SendImmediateResponse: false,
                NextState: conversation?.AssistantState ?? ConversationAssistantState.Idle,
                IgnoredReason: command.IgnoredReason);
        }

        if (command.HasLocation)
        {
            command.IntentType = InboundIntentType.LocationShare;
            return new IntentClassificationResult(
                InboundIntentType.LocationShare,
                QueueForAdvisory: false,
                SendImmediateResponse: true,
                NextState: ConversationAssistantState.AwaitingProblemDetails);
        }

        var normalizedText = Normalize(command.Text);

        if (normalizedText.StartsWith("/start", StringComparison.Ordinal))
        {
            command.IntentType = InboundIntentType.StartCommand;
            return new IntentClassificationResult(
                InboundIntentType.StartCommand,
                QueueForAdvisory: false,
                SendImmediateResponse: true,
                NextState: ConversationAssistantState.AwaitingProblemDetails);
        }

        if (normalizedText.StartsWith("/help", StringComparison.Ordinal))
        {
            command.IntentType = InboundIntentType.HelpCommand;
            return new IntentClassificationResult(
                InboundIntentType.HelpCommand,
                QueueForAdvisory: false,
                SendImmediateResponse: true,
                NextState: ConversationAssistantState.AwaitingProblemDetails);
        }

        if (command.Media.Count > 0 && string.IsNullOrWhiteSpace(normalizedText))
        {
            command.IntentType = InboundIntentType.MediaOnly;
            return new IntentClassificationResult(
                InboundIntentType.MediaOnly,
                QueueForAdvisory: true,
                SendImmediateResponse: false,
                NextState: ConversationAssistantState.AwaitingProblemDetails);
        }

        if (IsGreeting(normalizedText))
        {
            command.IntentType = InboundIntentType.Greeting;
            return new IntentClassificationResult(
                InboundIntentType.Greeting,
                QueueForAdvisory: false,
                SendImmediateResponse: true,
                NextState: ConversationAssistantState.AwaitingProblemDetails);
        }

        if (command.Media.Count > 0 || LooksLikeSymptomReport(normalizedText, conversation))
        {
            command.IntentType = command.Media.Count > 0 ? InboundIntentType.MediaOnly : InboundIntentType.SymptomReport;
            return new IntentClassificationResult(
                command.IntentType,
                QueueForAdvisory: true,
                SendImmediateResponse: false,
                NextState: ConversationAssistantState.AwaitingProblemDetails);
        }

        command.IntentType = InboundIntentType.SmallTalk;
        command.IgnoredReason = string.IsNullOrWhiteSpace(normalizedText) ? "no_text_or_media" : "insufficient_problem_detail";
        return new IntentClassificationResult(
            InboundIntentType.SmallTalk,
            QueueForAdvisory: false,
            SendImmediateResponse: true,
            NextState: ConversationAssistantState.AwaitingProblemDetails,
            IgnoredReason: command.IgnoredReason);
    }

    private static string Normalize(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : text.Trim().ToLowerInvariant();

    private static bool IsGreeting(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (GreetingTokens.Any(token => string.Equals(text, token, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(token => GreetingTokens.Contains(token, StringComparer.OrdinalIgnoreCase));
    }

    private static bool LooksLikeSymptomReport(string text, Conversation? conversation)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (SmallTalkTokens.Any(token => string.Equals(text, token, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (conversation?.AssistantState is ConversationAssistantState.AwaitingPhoto or ConversationAssistantState.AwaitingProblemDetails)
        {
            if (text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length >= 4)
            {
                return true;
            }
        }

        return SymptomSignals.Any(signal => text.Contains(signal, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class ConversationResponseComposer(ILanguageService languageService) : IConversationResponseComposer
{
    public async Task<ComposedConversationResponse?> ComposeImmediateResponseAsync(
        NormalizedInboundMessageCommand command,
        FarmerProfile farmer,
        Conversation conversation,
        CancellationToken cancellationToken = default)
    {
        if (command.IntentType == InboundIntentType.Unsupported)
        {
            return null;
        }

        var english = command.IntentType switch
        {
            InboundIntentType.StartCommand => """
                Welcome to FarmIQ. Send a photo of the affected crop and one short sentence about the problem.
                Example: "My tomato leaves have brown spots and aphids are spreading."
                If you want rain or spray timing, you can also share your location.
                """,
            InboundIntentType.HelpCommand => """
                Send three things when you can: the crop name, what you see, and a clear photo.
                Example: "Tomato leaves have yellow spots for 5 days."
                Share your location only if you want weather-based advice.
                """,
            InboundIntentType.Greeting => """
                Tell me the crop and the problem in one sentence, and send a clear photo if possible.
                Example: "My tomato leaves have spots and the lower branches are weakening."
                """,
            InboundIntentType.SmallTalk => """
                I need a little more detail before I can help.
                Send the crop name, the main symptom, and a clear photo if you have one.
                """,
            InboundIntentType.LocationShare => BuildLocationReply(conversation),
            _ => null
        };

        if (english is null)
        {
            return null;
        }

        var targetLanguage = string.IsNullOrWhiteSpace(command.IncomingLanguage)
            ? farmer.PreferredLanguage
            : command.IncomingLanguage;

        var localized = await languageService.TranslateFromEnglishAsync(english, targetLanguage ?? "en", cancellationToken);
        return new ComposedConversationResponse(
            localized.Trim(),
            command.IntentType == InboundIntentType.LocationShare ? ConversationAssistantState.AwaitingProblemDetails : ConversationAssistantState.AwaitingProblemDetails,
            RequestedLocation: false,
            RequestedPhoto: command.IntentType is InboundIntentType.Greeting or InboundIntentType.SmallTalk or InboundIntentType.StartCommand or InboundIntentType.HelpCommand);
    }

    private static string BuildLocationReply(Conversation conversation) =>
        conversation.AssistantState == ConversationAssistantState.AwaitingLocation
            ? """
                Thanks, I saved your location. Your next advisory can include rain and spray timing.
                Now send a photo or describe the crop problem.
                """
            : """
                Thanks, I saved your location for weather-based advice.
                Now send a crop photo or describe the issue you want checked.
                """;
}
