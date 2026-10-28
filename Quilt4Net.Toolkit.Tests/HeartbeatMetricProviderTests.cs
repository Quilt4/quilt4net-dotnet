using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Quilt4Net.Toolkit.Features.HeartbeatReporting;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

/// <summary>
/// Metrics from the application's own services (<see cref="IHeartbeatMetricProvider"/>): resolved with
/// their dependencies, read with a timeout, and never able to stop the heartbeat.
/// </summary>
public class HeartbeatMetricProviderTests
{
    [Fact]
    public async Task A_provider_gets_its_dependencies_injected_and_is_read_on_each_heartbeat()
    {
        var queue = new FakeQueue { Length = 4 };
        var (sut, handler) = CreateSut(o => o.AddMetricProvider<QueueMetrics>(), s => s.AddSingleton(queue));

        await sut.ReportOnceAsync(TestContext.Current.CancellationToken);
        queue.Length = 12;
        await sut.ReportOnceAsync(TestContext.Current.CancellationToken);

        Metrics(handler).Should().BeEquivalentTo(new Dictionary<string, double> { ["queue.length"] = 12, ["queue.oldest"] = 30 });
    }

    [Fact]
    public async Task A_provider_that_does_not_answer_in_time_is_left_out_and_the_others_are_sent()
    {
        var (sut, handler) = CreateSut(o =>
        {
            o.MetricTimeout = TimeSpan.FromMilliseconds(200);
            o.AddMetricProvider<HangingMetrics>();
            o.AddMetric("fine", () => 1);
        });

        var started = DateTime.UtcNow;
        var accepted = await sut.ReportOnceAsync(TestContext.Current.CancellationToken);

        accepted.Should().BeTrue();
        Metrics(handler).Keys.Should().Equal("fine");
        (DateTime.UtcNow - started).Should().BeLessThan(TimeSpan.FromSeconds(5), "the heartbeat waits for the timeout, not for the provider");
    }

    [Fact]
    public async Task A_provider_that_throws_is_left_out_and_the_heartbeat_is_still_sent()
    {
        var (sut, handler) = CreateSut(o => o.AddMetricProvider<FailingMetrics>().AddMetric("fine", () => 1));

        var accepted = await sut.ReportOnceAsync(TestContext.Current.CancellationToken);

        accepted.Should().BeTrue();
        Metrics(handler).Keys.Should().Equal("fine");
    }

    [Fact]
    public async Task A_metric_the_server_would_refuse_is_left_out_rather_than_losing_the_heartbeat()
    {
        // A duplicate name, a non-finite value or an empty name would make Quilt4Net refuse the whole report.
        var (sut, handler) = CreateSut(o => o.AddMetric("queue.length", () => 1).AddMetricProvider<UnrulyMetrics>());

        await sut.ReportOnceAsync(TestContext.Current.CancellationToken);

        Metrics(handler).Should().BeEquivalentTo(new Dictionary<string, double> { ["queue.length"] = 1, ["fine"] = 2 });
    }

    [Fact]
    public async Task A_provider_registered_directly_is_used_too()
    {
        var (sut, handler) = CreateSut(_ => { }, s => s.AddScoped<IHeartbeatMetricProvider, FailingMetrics>().AddScoped<IHeartbeatMetricProvider, ConstantMetrics>());

        await sut.ReportOnceAsync(TestContext.Current.CancellationToken);

        Metrics(handler).Keys.Should().Equal("constant");
    }

    [Fact]
    public void The_registration_adds_each_provider_once_as_scoped()
    {
        var services = new ServiceCollection();
        services.AddQuilt4NetHeartbeatReporting(null, o => o.AddMetricProvider<ConstantMetrics>().AddMetricProvider<ConstantMetrics>());

        services.Where(x => x.ServiceType == typeof(IHeartbeatMetricProvider)).Should().ContainSingle()
            .Which.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    private static (HeartbeatReporter Sut, RecordingHandler Handler) CreateSut(Action<HeartbeatReportingOptions> configure, Action<IServiceCollection> register = null)
    {
        var options = new HeartbeatReportingOptions { Cpu = CpuMetric.None, Memory = MemoryMetric.None };
        configure(options);

        var services = new ServiceCollection();
        foreach (var provider in options.MetricProviders) services.AddScoped(typeof(IHeartbeatMetricProvider), provider);
        register?.Invoke(services);
        var scopes = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var handler = new RecordingHandler();
        return (new HeartbeatReporter(new StubHttpClientFactory(handler), Options.Create(options), NullLogger<HeartbeatReporter>.Instance, scopeFactory: scopes), handler);
    }

    private static Dictionary<string, double> Metrics(RecordingHandler handler) =>
        JsonDocument.Parse(handler.LastBody).RootElement.GetProperty("metrics").EnumerateArray()
            .ToDictionary(x => x.GetProperty("name").GetString(), x => x.GetProperty("value").GetDouble());

    public sealed class FakeQueue
    {
        public int Length { get; set; }
    }

    private sealed class QueueMetrics(FakeQueue queue) : IHeartbeatMetricProvider
    {
        public async Task<IEnumerable<HeartbeatMetric>> GetMetricsAsync(CancellationToken cancellationToken)
        {
            await Task.Yield();
            return [new HeartbeatMetric("queue.length", queue.Length, "items"), new HeartbeatMetric("queue.oldest", 30, "s")];
        }
    }

    private sealed class HangingMetrics : IHeartbeatMetricProvider
    {
        // Ignores the token on purpose: the reporter must not wait for a provider that does not listen.
        public async Task<IEnumerable<HeartbeatMetric>> GetMetricsAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromMinutes(5), CancellationToken.None);
            return [new HeartbeatMetric("late", 1)];
        }
    }

    private sealed class FailingMetrics : IHeartbeatMetricProvider
    {
        public Task<IEnumerable<HeartbeatMetric>> GetMetricsAsync(CancellationToken cancellationToken) => throw new HttpRequestException("queue service unreachable");
    }

    private sealed class UnrulyMetrics : IHeartbeatMetricProvider
    {
        public Task<IEnumerable<HeartbeatMetric>> GetMetricsAsync(CancellationToken cancellationToken) => Task.FromResult<IEnumerable<HeartbeatMetric>>(
        [
            new HeartbeatMetric("Queue.Length", 5),
            new HeartbeatMetric("", 1),
            new HeartbeatMetric("nan", double.NaN),
            new HeartbeatMetric("fine", 2)
        ]);
    }

    private sealed class ConstantMetrics : IHeartbeatMetricProvider
    {
        public Task<IEnumerable<HeartbeatMetric>> GetMetricsAsync(CancellationToken cancellationToken) => Task.FromResult<IEnumerable<HeartbeatMetric>>([new HeartbeatMetric("constant", 7)]);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri("https://example.com/") };
    }
}
