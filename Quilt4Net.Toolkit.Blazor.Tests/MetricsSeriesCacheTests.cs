using System;
using AwesomeAssertions;
using Quilt4Net.Toolkit.Blazor.Features.Metrics;
using Quilt4Net.Toolkit.Features.ApplicationInsights;
using Xunit;

namespace Quilt4Net.Toolkit.Blazor.Tests;

public class MetricsSeriesCacheTests
{
    private static MetricSample[] One(string series, double value)
        => [new MetricSample(series, new DateTime(2026, 6, 14, 10, 0, 0, DateTimeKind.Utc), value)];

    [Fact]
    public void Round_trips_host_and_cluster_node_series()
    {
        var cache = new MetricsSeriesCache();
        var range = TimeSpan.FromHours(1);
        var loaded = DateTime.UtcNow;

        cache.Set("ctx:a", range,
            cpu: One("Eplicta1", 12),
            memory: One("Eplicta1", 34),
            disk: One("Eplicta1 C:", 50),
            diskCapacity: [new DiskCapacity("Eplicta1 C:", 50, 50, 0, 100)],
            nodeCpu: One("cog-audry", 0.5),
            nodeMemory: One("cog-audry", 29),
            nodeFilesystem: One("cog-audry", 1.7),
            loadedUtc: loaded);

        cache.TryGet("ctx:a", range, TimeSpan.FromMinutes(10), out var entry).Should().BeTrue();
        entry.NodeCpu.Should().ContainSingle().Which.Series.Should().Be("cog-audry");
        entry.NodeMemory[0].Value.Should().Be(29);
        entry.NodeFilesystem[0].Value.Should().Be(1.7);
        entry.Cpu[0].Series.Should().Be("Eplicta1");
        entry.LoadedUtc.Should().Be(loaded);
    }

    [Fact]
    public void Expired_entry_is_evicted()
    {
        var cache = new MetricsSeriesCache();
        var range = TimeSpan.FromHours(1);

        cache.Set("ctx:a", range, [], [], [], [], One("cog-audry", 0.5), [], [],
            loadedUtc: DateTime.UtcNow - TimeSpan.FromMinutes(30));

        cache.TryGet("ctx:a", range, TimeSpan.FromMinutes(10), out _).Should().BeFalse();
    }

    [Fact]
    public void Invalidate_removes_the_entry()
    {
        var cache = new MetricsSeriesCache();
        var range = TimeSpan.FromHours(1);
        cache.Set("ctx:a", range, [], [], [], [], One("cog-audry", 0.5), [], [], DateTime.UtcNow);

        cache.Invalidate("ctx:a", range);

        cache.TryGet("ctx:a", range, TimeSpan.FromMinutes(10), out _).Should().BeFalse();
    }
}
