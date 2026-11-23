using System.Collections.Concurrent;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Quilt4Net.Toolkit.Features.Content;
using Quilt4Net.Toolkit.Features.FeatureToggle;
using Quilt4Net.Toolkit.Framework;
using Xunit;

namespace Quilt4Net.Toolkit.Blazor.Tests;

/// <summary>
/// Issue #163: the bulk warm-up ran once per process, so every warmed key expired at the same
/// instant and the next render fanned out one call per key. These pin that the warm-up now repeats
/// before expiry, follows the server's own lifetime, and can still be turned off.
/// </summary>
public class PeriodicContentWarmupTests
{
    [Fact]
    public async Task Warm_up_repeats_on_a_timer_instead_of_once_per_process()
    {
        var call = new TtlReportingCallService(TimeSpan.FromMilliseconds(200));
        var sut = Build(call, o =>
        {
            o.WarmUpRefreshFraction = 0.5;
            o.MinimumWarmUpInterval = TimeSpan.FromMilliseconds(50);
        });

        await sut.StartAsync(CancellationToken.None);
        try
        {
            (await WaitUntil(() => call.WarmCount >= 3, TimeSpan.FromSeconds(5))).Should().BeTrue(
                "a 200ms lifetime at 0.5 re-warms every 100ms, so several passes must run rather than the single startup one");
        }
        finally
        {
            await sut.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task No_repeat_when_the_periodic_warm_up_is_disabled()
    {
        var call = new TtlReportingCallService(TimeSpan.FromMilliseconds(200));
        var sut = Build(call, o =>
        {
            o.PeriodicWarmUpEnabled = false;
            o.WarmUpRefreshFraction = 0.5;
            o.MinimumWarmUpInterval = TimeSpan.FromMilliseconds(50);
        });

        await sut.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntil(() => call.WarmCount >= 1, TimeSpan.FromSeconds(5));
            await Task.Delay(400, TestContext.Current.CancellationToken);

            call.WarmCount.Should().Be(1, "the startup warm-up still runs; only the repeat is off");
        }
        finally
        {
            await sut.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public void Interval_follows_the_server_lifetime_and_respects_the_floor()
    {
        var tenMinutes = new TtlReportingCallService(TimeSpan.FromMinutes(10));
        Build(tenMinutes, o => o.WarmUpRefreshFraction = 0.8).NextInterval()
            .Should().Be(TimeSpan.FromMinutes(8), "the re-warm lands before the entries expire, not after");

        var tooShort = new TtlReportingCallService(TimeSpan.FromSeconds(10));
        Build(tooShort, o => o.WarmUpRefreshFraction = 0.8).NextInterval()
            .Should().Be(TimeSpan.FromSeconds(30), "a very short server lifetime must not turn the re-warm into its own load");

        var unknown = new TtlReportingCallService(null);
        Build(unknown, o => o.WarmUpRefreshFraction = 0.8).NextInterval()
            .Should().BeGreaterThan(TimeSpan.Zero, "nothing observed yet still yields a usable interval");
    }

    private static ContentWarmupHostedService Build(IRemoteContentCallService call, Action<ContentOptions> configure)
    {
        var options = new ContentOptions { WarmUpEnabled = true };
        configure(options);
        return new ContentWarmupHostedService(call, new NoopConnectionService(), Options.Create(options),
            NullLogger<ContentWarmupHostedService>.Instance);
    }

    private static async Task<bool> WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            await Task.Delay(20);
        }
        return condition();
    }

    private sealed class TtlReportingCallService : IRemoteContentCallService
    {
        private int _warmCount;

        public TtlReportingCallService(TimeSpan? observedTtl) => ObservedContentTtl = observedTtl;

        public TimeSpan? ObservedContentTtl { get; }
        public int WarmCount => Volatile.Read(ref _warmCount);

        public Task WarmConfiguredLanguagesAsync(string application = null)
        {
            Interlocked.Increment(ref _warmCount);
            return Task.CompletedTask;
        }

        public Task WarmCacheAsync(Guid languageKey, string application = null) => Task.CompletedTask;
        public Task<(string Value, bool Success)> GetContentAsync(string key, string defaultValue, Guid languageKey, ContentFormat? contentType, string application = null, IReadOnlyDictionary<string, string> translations = null) => Task.FromResult((defaultValue, false));
        public Task<ContentResult> GetContentResultAsync(string key, string defaultValue, Guid languageKey, ContentFormat? contentType, string application = null, IReadOnlyDictionary<string, string> translations = null) => Task.FromResult(new ContentResult { Value = defaultValue, Success = false, Source = ContentSource.Unknown, Stale = true });
        public Task SetContentAsync(string key, string defaultValue, Guid languageKey, ContentFormat contentType, string application = null) => Task.CompletedTask;
        public Task<Language[]> GetLanguagesAsync(bool forceReload) => Task.FromResult(Array.Empty<Language>());
        public Task ClearContentCacheAsync() => Task.CompletedTask;
        public IReadOnlyDictionary<Guid, int> GetCacheCountsByLanguage() => new ConcurrentDictionary<Guid, int>();
    }

    private sealed class NoopConnectionService : IConnectionService
    {
        public Task<ConnectionResult> CanConnectAsync(Service service) => Task.FromResult(new ConnectionResult { Success = true });
    }
}
