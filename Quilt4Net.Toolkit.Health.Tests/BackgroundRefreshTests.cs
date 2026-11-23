using System.Diagnostics;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Quilt4Net.Toolkit.Features.Api;
using Quilt4Net.Toolkit.Features.Health;
using Quilt4Net.Toolkit.Features.Probe;
using Xunit;

namespace Quilt4Net.Toolkit.Health.Tests;

// GitHub #233: a component with RefreshInBackground answers from its last result and is refreshed in the background.
public class BackgroundRefreshTests
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(5);
    private static readonly DateTimeOffset Start = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_stale_result_is_answered_at_once_while_a_refresh_runs_in_the_background()
    {
        await using var h = new Harness();
        var check = new CountingCheck();
        h.Options.AddComponent(check.Component("mail", refreshInBackground: true));
        h.Options.AddComponent(new Component { Name = "fast", CheckAsync = _ => Task.FromResult(new CheckResult { Success = true }) });
        var sut = h.HealthService();

        try
        {
            (await PollAsync(sut)).Components["mail"].Details["message"].Should().Be("run 1", "the very first probe waits for the first result");

            check.Block();
            h.Clock.Advance(CacheDuration + TimeSpan.FromSeconds(1));

            var sw = Stopwatch.StartNew();
            var stale = await PollAsync(sut);
            sw.Stop();

            sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1), "a probe must not wait on an opted-in component's check");
            stale.Components.Keys.Should().BeEquivalentTo("mail", "fast");
            stale.Components["mail"].Details["message"].Should().Be("run 1");
            await EventuallyAsync(() => check.Runs == 2, "the stale result must start a refresh");

            check.Release();
            await EventuallyAsync(() => !h.Cache.GetState("mail").Refreshing, "the refresh must finish once the check returns");

            (await PollAsync(sut)).Components["mail"].Details["message"].Should().Be("run 2");
            check.Runs.Should().Be(2);
        }
        finally
        {
            check.Release();
        }
    }

    [Fact]
    public async Task Only_one_refresh_runs_at_a_time()
    {
        await using var h = new Harness();
        var check = new CountingCheck();
        h.Options.AddComponent(check.Component("mail", refreshInBackground: true));
        var sut = h.HealthService();

        try
        {
            await PollAsync(sut);
            check.Block();
            h.Clock.Advance(CacheDuration + TimeSpan.FromSeconds(1));

            await Task.WhenAll(PollAsync(sut), PollAsync(sut), PollAsync(sut));
            await EventuallyAsync(() => check.Runs == 2, "a stale result must start a refresh");
            await Task.Delay(200, TestContext.Current.CancellationToken);

            check.Runs.Should().Be(2, "probes that find a refresh already running must not start another");
        }
        finally
        {
            check.Release();
        }
    }

    [Fact]
    public async Task A_refresh_runs_in_its_own_scope_and_outlives_the_request_that_started_it()
    {
        await using var h = new Harness();
        var seen = new List<ScopeMarker>();
        var check = new CountingCheck();
        h.Options.AddComponent(new Component
        {
            Name = "mail",
            Essential = false,
            CacheDuration = CacheDuration,
            RefreshInBackground = true,
            CheckAsync = sp =>
            {
                lock (seen)
                {
                    seen.Add(sp.GetRequiredService<ScopeMarker>());
                }

                return check.Component("unused", false).CheckAsync(sp);
            }
        });

        try
        {
            ScopeMarker requestMarker;
            using (var request = h.Root.CreateScope())
            {
                requestMarker = request.ServiceProvider.GetRequiredService<ScopeMarker>();
                await PollAsync(h.HealthService(request.ServiceProvider));
            }

            check.Block();
            h.Clock.Advance(CacheDuration + TimeSpan.FromSeconds(1));
            using (var request = h.Root.CreateScope())
            {
                using var abort = new CancellationTokenSource();
                await PollWithTokenAsync(h.HealthService(request.ServiceProvider), abort.Token);
                await abort.CancelAsync();
            }

            await EventuallyAsync(() => check.Runs == 2, "the stale result must start a refresh");
            check.Release();
            await EventuallyAsync(() => !h.Cache.GetState("mail").Refreshing, "the refresh must finish after its request is gone");

            var result = await PollAsync(h.HealthService());
            result.Components["mail"].Status.Should().Be(HealthStatus.Healthy);
            result.Components["mail"].Details["message"].Should().Be("run 2");
            seen.Should().HaveCount(2).And.NotContain(requestMarker, "a refresh must not use the request's service scope");
            seen[0].Should().NotBeSameAs(seen[1], "each refresh gets a scope of its own");
        }
        finally
        {
            check.Release();
        }
    }

    [Fact]
    public async Task An_aborted_first_probe_ends_only_its_wait_and_the_result_is_still_stored()
    {
        await using var h = new Harness();
        var check = new CountingCheck();
        check.Block();
        h.Options.AddComponent(check.Component("mail", refreshInBackground: true));
        var sut = h.HealthService();

        try
        {
            using var abort = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            var aborted = () => PollWithTokenAsync(sut, abort.Token);
            await aborted.Should().ThrowAsync<OperationCanceledException>();

            check.Release();
            await EventuallyAsync(() => h.Cache.GetState("mail").CheckedAt != null, "the first check must finish and be stored after the probe gave up");

            (await PollAsync(sut)).Components["mail"].Details["message"].Should().Be("run 1");
            check.Runs.Should().Be(1);
        }
        finally
        {
            check.Release();
        }
    }

    [Fact]
    public async Task A_slow_refresh_is_cut_off_at_Timeout_and_replaces_the_result()
    {
        await using var h = new Harness();
        var check = new CountingCheck();
        h.Options.AddComponent(check.Component("register", refreshInBackground: true) with { Timeout = TimeSpan.FromMilliseconds(200) });
        var sut = h.HealthService();

        try
        {
            (await PollAsync(sut)).Components["register"].Status.Should().Be(HealthStatus.Healthy);

            check.Block();
            h.Clock.Advance(CacheDuration + TimeSpan.FromSeconds(1));
            (await PollAsync(sut)).Components["register"].Status.Should().Be(HealthStatus.Healthy, "the stale result is answered while the refresh runs");

            await EventuallyAsync(() => check.Runs == 2 && !h.Cache.GetState("register").Refreshing, "Timeout must end a refresh nobody waits on");

            var result = await PollAsync(sut);
            result.Components["register"].Status.Should().Be(HealthStatus.Degraded);
            result.Components["register"].Details["message"].Should().Contain("timed out");
        }
        finally
        {
            check.Release();
        }
    }

    [Fact]
    public async Task RefreshDue_warms_up_then_refreshes_each_component_when_its_result_reaches_CacheDuration()
    {
        await using var h = new Harness();
        var slow = new CountingCheck();
        var quick = new CountingCheck();
        h.Options.AddComponent(slow.Component("every-30s", refreshInBackground: true));
        h.Options.AddComponent(quick.Component("every-10s", refreshInBackground: true) with { CacheDuration = TimeSpan.FromSeconds(10) });
        var components = h.Refresher.ListOptedIn();

        h.Refresher.RefreshDue(components);
        await EventuallyAsync(() => slow.Runs == 1 && quick.Runs == 1 && h.Cache.GetState("every-30s").CheckedAt != null && h.Cache.GetState("every-10s").CheckedAt != null, "the first pass warms every opted-in component up");

        h.Refresher.RefreshDue(components).Should().Be(TimeSpan.FromSeconds(10), "the next one is due when the 10-second result expires");
        slow.Runs.Should().Be(1);
        quick.Runs.Should().Be(1);

        h.Clock.Advance(TimeSpan.FromSeconds(10));
        h.Refresher.RefreshDue(components);
        await EventuallyAsync(() => quick.Runs == 2, "the 10-second component is due");
        slow.Runs.Should().Be(1, "the 30-second component is not due yet");
    }

    [Fact]
    public async Task ListOptedIn_takes_component_services_into_account_and_needs_a_CacheDuration()
    {
        var services = new ServiceCollection();
        services.AddTransient<OptedInComponentService>();
        await using var h = new Harness(services);
        h.Options.AddComponentService<OptedInComponentService>();
        h.Options.AddComponent(new CountingCheck().Component("added", refreshInBackground: true));
        h.Options.AddComponent(new CountingCheck().Component("no-duration", refreshInBackground: true) with { CacheDuration = null });
        h.Options.AddComponent(new CountingCheck().Component("plain-cache", refreshInBackground: false));

        var names = h.Refresher.ListOptedIn().Select(ComponentSource.NameOf);

        names.Should().BeEquivalentTo("from-service", "added");
    }

    [Fact]
    public async Task The_refresher_stops_at_once_when_no_component_opts_in()
    {
        await using var h = new Harness();
        h.Options.AddComponent(new CountingCheck().Component("plain-cache", refreshInBackground: false));

        await h.Refresher.StartAsync(TestContext.Current.CancellationToken);

        await h.Refresher.ExecuteTask.WaitAsync(Generous, TestContext.Current.CancellationToken);
        h.Refresher.ExecuteTask.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task The_refresher_warms_opted_in_components_up_at_start()
    {
        await using var h = new Harness();
        var check = new CountingCheck();
        h.Options.AddComponent(check.Component("mail", refreshInBackground: true));

        await h.Refresher.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            await EventuallyAsync(() => h.Cache.GetState("mail").CheckedAt != null, "the refresher runs each opted-in check once at start");

            (await PollAsync(h.HealthService())).Components["mail"].Details["message"].Should().Be("run 1");
            check.Runs.Should().Be(1, "the first probe finds the warmed-up result and runs nothing");
        }
        finally
        {
            await h.Refresher.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task CheckedAt_is_shown_for_cached_components_only()
    {
        await using var h = new Harness();
        h.Options.AddComponent(new CountingCheck().Component("cached", refreshInBackground: false));
        h.Options.AddComponent(new CountingCheck().Component("background", refreshInBackground: true));
        h.Options.AddComponent(new Component { Name = "uncached", CheckAsync = _ => Task.FromResult(new CheckResult { Success = true }) });

        var result = await PollAsync(h.HealthService());

        result.Components["cached"].Details["checkedAt"].Should().Be("2026-11-23T12:00:00.0000000Z");
        result.Components["background"].Details["checkedAt"].Should().Be("2026-11-23T12:00:00.0000000Z");
        result.Components["uncached"].Details.Should().NotContainKey("checkedAt");
    }

    [Fact]
    public async Task RefreshInBackground_without_a_CacheDuration_runs_the_check_on_every_probe()
    {
        await using var h = new Harness();
        var check = new CountingCheck();
        h.Options.AddComponent(check.Component("mail", refreshInBackground: true) with { CacheDuration = null });
        var sut = h.HealthService();

        await PollAsync(sut);
        var result = await PollAsync(sut);

        check.Runs.Should().Be(2);
        result.Components["mail"].Details.Should().NotContainKey("checkedAt");
    }

    private static Task<HealthResponse> PollAsync(HealthService sut) => PollWithTokenAsync(sut, TestContext.Current.CancellationToken);

    private static async Task<HealthResponse> PollWithTokenAsync(HealthService sut, CancellationToken cancellationToken)
    {
        return (await sut.GetStatusAsync(null, false, cancellationToken).ToArrayAsync(CancellationToken.None)).ToHealthResponse();
    }

    private static async Task EventuallyAsync(Func<bool> condition, string because)
    {
        var sw = Stopwatch.StartNew();
        while (!condition() && sw.Elapsed < Generous)
        {
            await Task.Delay(20, CancellationToken.None);
        }

        condition().Should().BeTrue(because);
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly Mock<IHostEnvironment> _hostEnvironment = new(MockBehavior.Loose);
        private readonly Mock<IHostedServiceProbeRegistry> _probes = new(MockBehavior.Loose);

        public Harness(IServiceCollection services = null)
        {
            services ??= new ServiceCollection();
            services.AddSingleton(Options);
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton<ComponentCheckCache>();
            services.AddSingleton<ComponentRefreshService>();
            services.AddScoped<ScopeMarker>();
            Root = services.BuildServiceProvider();
        }

        public Quilt4NetHealthApiOptions Options { get; } = new();
        public ManualClock Clock { get; } = new(Start);
        public ServiceProvider Root { get; }
        public ComponentCheckCache Cache => Root.GetRequiredService<ComponentCheckCache>();
        public ComponentRefreshService Refresher => Root.GetRequiredService<ComponentRefreshService>();

        public HealthService HealthService(IServiceProvider requestProvider = null)
            => new(_hostEnvironment.Object, requestProvider ?? Root, _probes.Object, Options, null, Cache, Refresher);

        public ValueTask DisposeAsync() => Root.DisposeAsync();
    }

    private sealed class CountingCheck
    {
        private readonly ManualResetEventSlim _open = new(true);
        private int _runs;

        public int Runs => Volatile.Read(ref _runs);

        public void Block() => _open.Reset();

        public void Release() => _open.Set();

        public Component Component(string name, bool refreshInBackground) => new()
        {
            Name = name,
            Essential = false,
            CacheDuration = CacheDuration,
            RefreshInBackground = refreshInBackground,
            CheckAsync = _ =>
            {
                var run = Interlocked.Increment(ref _runs);
                _open.Wait(Generous);
                return Task.FromResult(new CheckResult { Success = true, Message = $"run {run}" });
            }
        };
    }

    private sealed class OptedInComponentService : IComponentService
    {
        public IEnumerable<Component> GetComponents()
        {
            yield return new CountingCheck().Component("from-service", refreshInBackground: true);
        }
    }

    private sealed class ScopeMarker;

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public ManualClock(DateTimeOffset start) => _utcNow = start;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan by) => _utcNow += by;
    }
}
