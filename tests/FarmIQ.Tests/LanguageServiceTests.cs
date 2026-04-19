using FarmIQ.Infrastructure.Configuration;
using FarmIQ.Infrastructure.Services;
using FarmIQ.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FarmIQ.Tests;

public sealed class LanguageServiceTests
{
    private readonly FarmLanguageService _service = new(
        new GlmChatClient(
            new FakeHttpClientFactory(),
            Options.Create(new GlmOptions
            {
                Enabled = false
            }),
            NullLogger<GlmChatClient>.Instance),
        new FakeHttpClientFactory(),
        Options.Create(new OpenAIOptions
        {
            Enabled = false
        }),
        NullLogger<FarmLanguageService>.Instance);

    [Theory]
    [InlineData("my tomato leaves have spots", FarmLanguages.English)]
    [InlineData("у листьев помидора желтые пятна", FarmLanguages.Russian)]
    [InlineData("pomidor barglarida sariq dog'lar bor", FarmLanguages.Uzbek)]
    public async Task DetectLanguageAsync_ShouldDetectSupportedLanguages(string text, string expected)
    {
        var detected = await _service.DetectLanguageAsync(text);
        detected.Should().Be(expected);
    }

    [Fact]
    public async Task TranslateToEnglishAsync_ShouldTranslateCommonUzbekSymptoms()
    {
        var translated = await _service.TranslateToEnglishAsync("pomidor barglarida dog'lar va shira tarqalmoqda", FarmLanguages.Uzbek);

        translated.Should().Contain("tomato");
        translated.Should().Contain("leaf");
        translated.Should().Contain("spot");
        translated.Should().Contain("aphids");
    }

    [Fact]
    public async Task TranslateFromEnglishAsync_ShouldTranslateAdvisoryLabelsToRussian()
    {
        var translated = await _service.TranslateFromEnglishAsync("Possible issue: Possible fungal leaf spot", FarmLanguages.Russian);

        translated.Should().Contain("Возможная проблема:");
        translated.Should().Contain("Возможная грибковая пятнистость листьев");
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
