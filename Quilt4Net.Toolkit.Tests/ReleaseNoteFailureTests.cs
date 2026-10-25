using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quilt4Net.Toolkit.Features.ReleaseNotes;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

/// <summary>
/// The release-note reader degrades quietly: a page that cannot show "what's new" renders without it.
/// A 403 - the key lacks <c>release:read</c>, or the team has switched Documentation off - is a choice
/// rather than a fault, so it is a warning carrying the server's own sentence, not an error.
/// </summary>
public class ReleaseNoteFailureTests
{
    private const string SwitchedOff = """{"title":"Forbidden","status":403,"detail":"'release:read' belongs to Documentation, which is switched off for this team."}""";

    [Fact]
    public async Task A_403_is_no_news_and_a_warning_naming_why()
    {
        var (sut, log) = Create(HttpStatusCode.Forbidden, SwitchedOff);

        var delta = await sut.GetDeltaAsync("1.0.0");
        var note = await sut.GetAsync("1.0.0");

        delta.Should().BeSameAs(ReleaseNoteDeltaDto.Empty);
        note.Should().BeNull();
        log.Should().NotContain(x => x.Level >= LogLevel.Error);
        log.Should().Contain(x => x.Level == LogLevel.Warning && x.Message.Contains("'release:read' belongs to Documentation, which is switched off for this team."));
    }

    [Fact]
    public async Task Any_other_failure_is_still_an_error()
    {
        var (sut, log) = Create(HttpStatusCode.InternalServerError, "boom");

        var delta = await sut.GetDeltaAsync("1.0.0");

        delta.Should().BeSameAs(ReleaseNoteDeltaDto.Empty);
        log.Should().Contain(x => x.Level == LogLevel.Error);
    }

    private static (IReleaseNoteService Sut, List<(LogLevel Level, string Message)> Log) Create(HttpStatusCode status, string body)
    {
        var log = new List<(LogLevel, string)>();
        var options = Options.Create(new ContentOptions { ApiKey = "key", Application = "App" });
        var factory = new StubHttpClientFactory(new StubHandler(() => new HttpResponseMessage(status) { Content = new StringContent(body) }));
        return (new RemoteReleaseNoteService(options, factory, new RecordingLogger(log)), log);
    }

    private sealed class StubHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond());
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri("https://quilt4net.test/") };
    }

    private sealed class RecordingLogger(List<(LogLevel, string)> entries) : ILogger<RemoteReleaseNoteService>
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter) => entries.Add((logLevel, formatter(state, exception)));
    }
}
