using AwesomeAssertions;
using Quilt4Net.Toolkit.Features.ApplicationInsights;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

/// <summary>
/// Pins the day-chunk cache contract: a finished UTC day is fetched once and then immutable; the
/// current day refreshes on the TTL; different workspaces / days are isolated; and the Merge fold
/// sums same-key cells across day chunks.
/// </summary>
public class LogCubeDayCacheTests
{
    private static LogCountByServiceCell Cell(string service, LogSource source, long count, long bytes, long trueCount, long trueBytes)
        => new(service, SeverityLevel.Information, "prod", source, count, bytes, "m", trueCount, trueBytes);

    [Fact]
    public async Task MissingDay_Fetches_Once_And_Returns_Cells()
    {
        var now = new DateTimeOffset(2026, 6, 17, 12, 0, 0, TimeSpan.Zero);
        var cache = new LogCubeDayCache(TimeSpan.FromMinutes(5), () => now);

        var calls = 0;
        var expected = new[] { Cell("svc", LogSource.Trace, 1, 10, 1, 10) };
        var result = await cache.GetDayAsync("ws", new DateOnly(2026, 6, 16), _ => { calls++; return Task.FromResult(expected); });

        calls.Should().Be(1);
        result.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task PastDay_Is_Immutable_Even_After_Ttl_And_Day_Rollover()
    {
        var now = new DateTimeOffset(2026, 6, 17, 12, 0, 0, TimeSpan.Zero);
        var cache = new LogCubeDayCache(TimeSpan.FromMinutes(5), () => now);

        var calls = 0;
        Task<LogCountByServiceCell[]> Fetch(DateOnly _) { calls++; return Task.FromResult(new[] { Cell("svc", LogSource.Trace, 1, 1, 1, 1) }); }

        var past = new DateOnly(2026, 6, 16);
        await cache.GetDayAsync("ws", past, Fetch);
        now = now.AddHours(2);        // well past the 5-minute TTL
        await cache.GetDayAsync("ws", past, Fetch);
        now = now.AddDays(3);         // the "today" anchor has moved on; past stays cached
        await cache.GetDayAsync("ws", past, Fetch);

        calls.Should().Be(1);
    }

    [Fact]
    public async Task Today_Refetches_Once_The_Ttl_Lapses()
    {
        var now = new DateTimeOffset(2026, 6, 17, 12, 0, 0, TimeSpan.Zero);
        var cache = new LogCubeDayCache(TimeSpan.FromMinutes(5), () => now);

        var calls = 0;
        Task<LogCountByServiceCell[]> Fetch(DateOnly _) { calls++; return Task.FromResult(new[] { Cell("svc", LogSource.Trace, 1, 1, 1, 1) }); }

        var today = new DateOnly(2026, 6, 17);
        await cache.GetDayAsync("ws", today, Fetch);   // fetch #1
        now = now.AddMinutes(2);
        await cache.GetDayAsync("ws", today, Fetch);   // within TTL → cached
        calls.Should().Be(1);

        now = now.AddMinutes(5);                       // 7 min total → stale
        await cache.GetDayAsync("ws", today, Fetch);   // fetch #2
        calls.Should().Be(2);
    }

    [Fact]
    public async Task Distinct_Days_And_Workspaces_Are_Isolated()
    {
        var now = new DateTimeOffset(2026, 6, 17, 12, 0, 0, TimeSpan.Zero);
        var cache = new LogCubeDayCache(TimeSpan.FromMinutes(5), () => now);

        var calls = 0;
        Task<LogCountByServiceCell[]> Fetch(DateOnly _) { calls++; return Task.FromResult(new[] { Cell("svc", LogSource.Trace, 1, 1, 1, 1) }); }

        await cache.GetDayAsync("ws", new DateOnly(2026, 6, 14), Fetch);
        await cache.GetDayAsync("ws", new DateOnly(2026, 6, 15), Fetch);
        await cache.GetDayAsync("ws2", new DateOnly(2026, 6, 14), Fetch); // different workspace, same date
        // re-request the first two — both past, both cached
        await cache.GetDayAsync("ws", new DateOnly(2026, 6, 14), Fetch);
        await cache.GetDayAsync("ws", new DateOnly(2026, 6, 15), Fetch);

        calls.Should().Be(3);
    }

    [Fact]
    public void Merge_Sums_Same_Key_Cells_And_Keeps_Distinct_Keys()
    {
        var dayA = new[]
        {
            Cell("svc", LogSource.Trace, 2, 10, 2, 10),
            Cell("svc", LogSource.Request, 1, 5, 1, 5),
        };
        var dayB = new[]
        {
            Cell("svc", LogSource.Trace, 3, 20, 4, 40), // same key as dayA[0] → sums
        };

        var merged = LogCubeDayCache.Merge(dayA.Concat(dayB));

        merged.Should().HaveCount(2);
        var trace = merged.Single(c => c.Source == LogSource.Trace);
        trace.Count.Should().Be(5);
        trace.Bytes.Should().Be(30);
        trace.TrueCount.Should().Be(6);
        trace.TrueBytes.Should().Be(50);
        merged.Single(c => c.Source == LogSource.Request).Count.Should().Be(1);
    }
}
