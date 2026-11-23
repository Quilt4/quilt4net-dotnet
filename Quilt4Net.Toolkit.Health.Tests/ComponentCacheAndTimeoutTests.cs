using System.Diagnostics;
using AwesomeAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Quilt4Net.Toolkit.Features.Api;
using Quilt4Net.Toolkit.Features.Health;
using Quilt4Net.Toolkit.Features.Probe;
using Xunit;

namespace Quilt4Net.Toolkit.Health.Tests;

// Issue #134: deep Component checks gained optional per-Component result caching, timeout and
// cancellation (Dependency probes already had cache/throttle from #119). These are opt-in — unset
// properties keep the exact previous behaviour.
public class ComponentCacheAndTimeoutTests
{
    private readonly Mock<IServiceProvider> _serviceProvider = new(MockBehavior.Loose);
    private readonly Mock<ILogger<HealthService>> _logger = new(MockBehavior.Loose);
    private readonly Mock<IHostEnvironment> _hostEnvironment = new(MockBehavior.Loose);
    private readonly Mock<IHostedServiceProbeRegistry> _hostedServiceProbeRegistry = new(MockBehavior.Loose);

    private HealthService BuildSut(Quilt4NetHealthApiOptions option, ComponentCheckCache cache)
        => new(_hostEnvironment.Object, _serviceProvider.Object, _hostedServiceProbeRegistry.Object, option, _logger.Object, cache);

    [Fact]
    public async Task CacheDuration_reuses_result_within_window_and_reruns_after_it_expires()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var runs = 0;
        var option = new Quilt4NetHealthApiOptions();
        option.AddComponent(new Component
        {
            Name = "cached",
            CacheDuration = TimeSpan.FromSeconds(30),
            CheckAsync = _ =>
            {
                Interlocked.Increment(ref runs);
                return Task.FromResult(new CheckResult { Success = true });
            }
        });
        var sut = BuildSut(option, new ComponentCheckCache(time));

        await sut.GetStatusAsync(null, false, TestContext.Current.CancellationToken).ToArrayAsync(TestContext.Current.CancellationToken);
        await sut.GetStatusAsync(null, false, TestContext.Current.CancellationToken).ToArrayAsync(TestContext.Current.CancellationToken);
        runs.Should().Be(1, "the second poll within CacheDuration must reuse the cached result");

        time.Advance(TimeSpan.FromSeconds(31));
        await sut.GetStatusAsync(null, false, TestContext.Current.CancellationToken).ToArrayAsync(TestContext.Current.CancellationToken);
        runs.Should().Be(2, "once CacheDuration has elapsed the check must run again");
    }

    [Fact]
    public async Task No_CacheDuration_runs_the_check_every_time()
    {
        var runs = 0;
        var option = new Quilt4NetHealthApiOptions();
        option.AddComponent(new Component
        {
            Name = "uncached",
            CheckAsync = _ =>
            {
                Interlocked.Increment(ref runs);
                return Task.FromResult(new CheckResult { Success = true });
            }
        });
        var sut = BuildSut(option, new ComponentCheckCache(new FakeTimeProvider(DateTimeOffset.UtcNow)));

        await sut.GetStatusAsync(null, false, TestContext.Current.CancellationToken).ToArrayAsync(TestContext.Current.CancellationToken);
        await sut.GetStatusAsync(null, false, TestContext.Current.CancellationToken).ToArrayAsync(TestContext.Current.CancellationToken);

        runs.Should().Be(2, "without CacheDuration caching is disabled and each poll runs the check");
    }

    [Fact]
    public async Task Concurrent_polls_coalesce_into_a_single_check()
    {
        var gate = new TaskCompletionSource();
        var runs = 0;
        var option = new Quilt4NetHealthApiOptions();
        option.AddComponent(new Component
        {
            Name = "c",
            CacheDuration = TimeSpan.FromMinutes(1),
            CheckAsync = async _ =>
            {
                Interlocked.Increment(ref runs);
                await gate.Task;
                return new CheckResult { Success = true };
            }
        });
        var sut = BuildSut(option, new ComponentCheckCache(new FakeTimeProvider(DateTimeOffset.UtcNow)));

        var poll1 = sut.GetStatusAsync(null, false, TestContext.Current.CancellationToken).ToArrayAsync(TestContext.Current.CancellationToken).AsTask();
        var poll2 = sut.GetStatusAsync(null, false, TestContext.Current.CancellationToken).ToArrayAsync(TestContext.Current.CancellationToken).AsTask();
        await Task.Delay(100, TestContext.Current.CancellationToken); // let both reach the per-key gate
        gate.SetResult();
        await Task.WhenAll(poll1, poll2);

        runs.Should().Be(1, "concurrent polls for the same component must coalesce into a single check");
    }

    [Theory]
    [InlineData(false, HealthStatus.Degraded)]
    [InlineData(true, HealthStatus.Unhealthy)]
    public async Task Timeout_on_a_plain_check_reports_failure_fast_without_hanging(bool essential, HealthStatus expected)
    {
        var option = new Quilt4NetHealthApiOptions();
        option.AddComponent(new Component
        {
            Name = "slow",
            Essential = essential,
            Timeout = TimeSpan.FromMilliseconds(100),
            CheckAsync = async _ =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                return new CheckResult { Success = true };
            }
        });
        var sut = BuildSut(option, new ComponentCheckCache(TimeProvider.System));

        var sw = Stopwatch.StartNew();
        var result = (await sut.GetStatusAsync(null, false, TestContext.Current.CancellationToken).ToArrayAsync(TestContext.Current.CancellationToken)).ToHealthResponse();
        sw.Stop();

        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3), "a timed-out check must not block the fan-out for its full duration");
        result.Status.Should().Be(expected);
        result.Components.Single().Value.Details.First(x => x.Key == "message").Value.Should().Contain("timed out");
    }

    [Fact]
    public async Task Timeout_cancels_a_cancellation_aware_check()
    {
        var observedCancellation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var option = new Quilt4NetHealthApiOptions();
        option.AddComponent(new Component
        {
            Name = "cancellable",
            Essential = false,
            Timeout = TimeSpan.FromMilliseconds(100),
            CheckAsync = _ => Task.FromResult(new CheckResult { Success = true }), // required, unused when the cancellable variant is set
            CheckWithCancellationAsync = async (_, ct) =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), ct);
                }
                catch (OperationCanceledException)
                {
                    observedCancellation.TrySetResult();
                    throw;
                }
                return new CheckResult { Success = true };
            }
        });
        var sut = BuildSut(option, new ComponentCheckCache(TimeProvider.System));

        var sw = Stopwatch.StartNew();
        var result = (await sut.GetStatusAsync(null, false, TestContext.Current.CancellationToken).ToArrayAsync(TestContext.Current.CancellationToken)).ToHealthResponse();
        sw.Stop();

        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));
        result.Status.Should().Be(HealthStatus.Degraded);
        var cancelled = () => observedCancellation.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        await cancelled.Should().NotThrowAsync("the token must actually cancel the check so it can free its resources, not just abandon the wait");
    }

    [Fact]
    public async Task A_check_that_finishes_before_its_timeout_returns_normally()
    {
        var option = new Quilt4NetHealthApiOptions();
        option.AddComponent(new Component
        {
            Name = "fast",
            Timeout = TimeSpan.FromSeconds(5),
            CheckAsync = _ => Task.FromResult(new CheckResult { Success = true, Message = "ok" })
        });
        var sut = BuildSut(option, new ComponentCheckCache(TimeProvider.System));

        var result = (await sut.GetStatusAsync(null, false, TestContext.Current.CancellationToken).ToArrayAsync(TestContext.Current.CancellationToken)).ToHealthResponse();

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Components.Single().Value.Details.First(x => x.Key == "message").Value.Should().Be("ok");
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;
        public FakeTimeProvider(DateTimeOffset start) => _utcNow = start;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan by) => _utcNow += by;
    }
}
