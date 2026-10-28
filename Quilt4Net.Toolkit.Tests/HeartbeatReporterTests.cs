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

    [Fact]
    public async Task A_report_carries_the_processs_own_cpu_and_memory_by_default()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var sut = CreateSut(handler, new HeartbeatReportingOptions());
        GC.Collect(); // Memory load is read from the collector, which knows nothing before its first collection.

        await sut.ReportOnceAsync(TestContext.Current.CancellationToken);

        var metrics = Metrics(handler);
        metrics.Keys.Should().BeEquivalentTo(["cpu.process", "memory.process"]);
        metrics.Values.Should().AllSatisfy(x => x.Value.Should().BeInRange(0, 100));
        metrics.Values.Should().AllSatisfy(x => x.Unit.Should().Be("percent"));
    }

    [Fact]
    public async Task Built_in_metrics_can_be_switched_off()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var sut = CreateSut(handler, new HeartbeatReportingOptions { Cpu = CpuMetric.None, Memory = MemoryMetric.None });

        await sut.ReportOnceAsync(TestContext.Current.CancellationToken);

        Metrics(handler).Should().BeEmpty();
    }

    [Fact]
    public async Task Both_reports_the_process_and_the_whole_machine()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var sut = CreateSut(handler, new HeartbeatReportingOptions { Cpu = CpuMetric.None, Memory = MemoryMetric.Both });
        GC.Collect();

        await sut.ReportOnceAsync(TestContext.Current.CancellationToken);

        Metrics(handler).Keys.Should().BeEquivalentTo(["memory.process", "memory.machine"]);
    }

    [Fact]
    public async Task The_applications_own_metrics_are_read_on_each_heartbeat()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var queue = 3;
        var options = new HeartbeatReportingOptions { Cpu = CpuMetric.None, Memory = MemoryMetric.None }
            .AddMetric("queue.length", () => queue, "items")
            .AddMetric("cache.hit-ratio", async ct => { await Task.Yield(); return 87.5; });
        var sut = CreateSut(handler, options);

        await sut.ReportOnceAsync(TestContext.Current.CancellationToken);
        queue = 9;
        await sut.ReportOnceAsync(TestContext.Current.CancellationToken);

        var metrics = Metrics(handler);
        metrics["queue.length"].Should().Be((9d, "items"), "the value is read when the heartbeat is sent, not when it was added");
        metrics["cache.hit-ratio"].Should().Be((87.5, null));
    }

    [Fact]
    public async Task A_metric_that_throws_is_left_out_and_the_heartbeat_is_still_sent()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var options = new HeartbeatReportingOptions { Cpu = CpuMetric.None, Memory = MemoryMetric.None }
            .AddMetric("broken", (Func<double>)(() => throw new InvalidOperationException("no queue")))
            .AddMetric("not.a.number", () => double.NaN)
            .AddMetric("fine", () => 1);
        var sut = CreateSut(handler, options);

        var accepted = await sut.ReportOnceAsync(TestContext.Current.CancellationToken);

        accepted.Should().BeTrue();
        Metrics(handler).Keys.Should().Equal("fine");
    }

    [Theory]
    [InlineData("")]
    [InlineData("memory.machine")]
    [InlineData("CPU.Process")]
    public void A_metric_name_that_is_empty_or_built_in_is_refused(string name)
    {
        var act = () => new HeartbeatReportingOptions().AddMetric(name, () => 1);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_metric_name_can_only_be_added_once()
    {
        var options = new HeartbeatReportingOptions().AddMetric("queue.length", () => 1);

        var act = () => options.AddMetric("Queue.Length", () => 2);

        act.Should().Throw<ArgumentException>().WithMessage("*already added*");
    }

    [Fact]
    public void At_most_sixteen_metrics_of_your_own_fit_beside_the_built_in_ones()
    {
        var options = new HeartbeatReportingOptions();
        for (var i = 0; i < HeartbeatReportingOptions.MaxCustomMetrics; i++) options.AddMetric($"m{i}", () => i);

        var act = () => options.AddMetric("one.more", () => 1);

        act.Should().Throw<ArgumentException>();
    }

    private static Dictionary<string, (double Value, string Unit)> Metrics(RecordingHandler handler) =>
        JsonDocument.Parse(handler.LastBody).RootElement.GetProperty("metrics").EnumerateArray()
            .ToDictionary(
                x => x.GetProperty("name").GetString(),
                x => (x.GetProperty("value").GetDouble(), x.GetProperty("unit").ValueKind == JsonValueKind.Null ? null : x.GetProperty("unit").GetString()));

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
