using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Quilt4Net.Toolkit.Features.HeartbeatReporting;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

/// <summary>
/// The heartbeat reporter: what it posts, and that a failure never escapes into the host.
/// </summary>
public class HeartbeatReporterTests
{
    [Fact]
    public async Task A_report_posts_the_capabilities_and_the_sender()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var sut = CreateSut(handler, new HeartbeatReportingOptions { Capabilities = ["import", "export"], Application = "Worker", Environment = "Production" });

        var accepted = await sut.ReportOnceAsync(TestContext.Current.CancellationToken);

        accepted.Should().BeTrue();
        handler.LastRequest.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.RequestUri!.AbsolutePath.Should().Be("/Api/Heartbeat");
        var body = JsonDocument.Parse(handler.LastBody).RootElement;
        body.GetProperty("application").GetString().Should().Be("Worker");
        body.GetProperty("environment").GetString().Should().Be("Production");
        body.GetProperty("instance").GetString().Should().Be(Environment.MachineName);
        body.GetProperty("capabilities").EnumerateArray().Select(x => x.GetString()).Should().Equal("import", "export");
    }

    [Fact]
    public async Task A_refusal_is_reported_as_not_accepted_rather_than_thrown()
    {
        var sut = CreateSut(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)), new HeartbeatReportingOptions());

        var accepted = await sut.ReportOnceAsync(TestContext.Current.CancellationToken);

        accepted.Should().BeFalse();
    }

    [Fact]
    public async Task Heartbeats_refused_by_a_switched_off_feature_are_said_once_with_the_servers_sentence()
    {
        const string body = """{"title":"Forbidden","status":403,"detail":"'heartbeat:write' belongs to Monitoring, which is switched off for this team."}""";
        var log = new List<(LogLevel Level, string Message)>();
        var sut = new HeartbeatReporter(new StubHttpClientFactory(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent(body) })),
            Options.Create(new HeartbeatReportingOptions()), new RecordingLogger(log));

        await sut.ReportOnceAsync(TestContext.Current.CancellationToken);
        await sut.ReportOnceAsync(TestContext.Current.CancellationToken);

        log.Should().ContainSingle().Which.Should().Match<(LogLevel Level, string Message)>(x =>
            x.Level == LogLevel.Warning && x.Message.Contains("'heartbeat:write' belongs to Monitoring, which is switched off for this team."));
    }

    private sealed class RecordingLogger(List<(LogLevel, string)> entries) : ILogger<HeartbeatReporter>
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter) => entries.Add((logLevel, formatter(state, exception)));
    }

    [Fact]
    public async Task An_unreachable_server_is_reported_as_not_accepted_rather_than_thrown()
    {
        var sut = CreateSut(new RecordingHandler(_ => throw new HttpRequestException("No route to host")), new HeartbeatReportingOptions());

        var accepted = await sut.ReportOnceAsync(TestContext.Current.CancellationToken);

        accepted.Should().BeFalse("the application must keep running when Quilt4Net cannot be reached");
    }

    private static HeartbeatReporter CreateSut(HttpMessageHandler handler, HeartbeatReportingOptions options) =>
        new(new StubHttpClientFactory(handler), Options.Create(options), NullLogger<HeartbeatReporter>.Instance);

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage LastRequest { get; private set; }
        public string LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request);
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("https://example.com/") };
    }
}
