using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using Radzen;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Quilt4Net.Toolkit.Blazor.Features.Metrics;
using Quilt4Net.Toolkit.Features.ApplicationInsights;
using Xunit;

namespace Quilt4Net.Toolkit.Blazor.Tests;

public class MetricsViewClusterTests : BunitContext
{
    public MetricsViewClusterTests()
    {
        // RadzenChart / RadzenDropDown make JS-interop calls during render; loose mode returns
        // defaults so the component renders its markup without a strict-mode throw.
        JSInterop.Mode = JSRuntimeMode.Loose;
        // RadzenChart property-injects TooltipService (and siblings) — register the Radzen
        // services so the chart can instantiate when a series has data.
        Services.AddRadzenComponents();
    }

    [Fact]
    public void NodeNames_dedupes_sorts_and_drops_empty()
    {
        // The drill-down dropdown is populated from the node CPU series — one entry per node,
        // sorted, with blank (no k8s.node.name) samples excluded. Rendering the populated
        // RadzenCharts isn't viable under bUnit (RadzenChart.OnAfterRenderAsync needs real browser
        // layout), so the node-derivation logic is asserted directly via the extracted helper.
        MetricSample S(string series) => new(series, new DateTime(2026, 6, 14, 10, 0, 0, DateTimeKind.Utc), 1);
        var samples = new[] { S("cog-courvoisier"), S("cog-audry"), S("cog-audry"), S(""), S("vm-ygg-cp-1") };

        var nodes = MetricsViewLogic.NodeNames(samples);

        nodes.Should().Equal("cog-audry", "cog-courvoisier", "vm-ygg-cp-1");
    }

    [Fact]
    public void NodeNames_of_empty_series_is_empty()
        => MetricsViewLogic.NodeNames(System.Array.Empty<MetricSample>()).Should().BeEmpty();

    [Fact]
    public void NodeNames_unions_across_series_so_tab_survives_empty_cpu()
    {
        // Regression: the node list (and thus the Cluster tab) must be the union across series.
        // CPU% can be empty when allocatable_cpu doesn't overlap the window, but memory/filesystem
        // are always present — the tab must still show every node.
        MetricSample S(string series) => new(series, new DateTime(2026, 6, 14, 10, 0, 0, DateTimeKind.Utc), 1);
        var emptyCpu = System.Array.Empty<MetricSample>();
        var memory = new[] { S("cog-audry"), S("cog-exshaw") };
        var filesystem = new[] { S("cog-audry"), S("vm-ygg-cp-1") };

        var nodes = MetricsViewLogic.NodeNames(emptyCpu, memory, filesystem);

        nodes.Should().Equal("cog-audry", "cog-exshaw", "vm-ygg-cp-1");
    }

    [Fact]
    public void Hides_cluster_section_when_no_node_metrics()
    {
        Services.AddSingleton<IApplicationInsightsService>(new StubApplicationInsightsService(hasNodes: false));

        var cut = Render<MetricsView>(p => p.Add(c => c.Context, new TestContext()));

        // The Physical machines tab always renders; the Cluster tab must not appear once loading
        // completes and no node series came back (no "Whole-cluster total" toggle present).
        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Physical machines");
            cut.Markup.Should().NotContain("Whole-cluster total");
        });
    }

    private sealed class TestContext : IApplicationInsightsContext
    {
        public string TenantId => "t";
        public string WorkspaceId => "w";
        public string ClientId => "c";
        public string ClientSecret => "s";
    }

    private sealed class StubApplicationInsightsService : IApplicationInsightsService
    {
        private readonly bool _hasNodes;
        public StubApplicationInsightsService(bool hasNodes) => _hasNodes = hasNodes;

        private static MetricSample Sample(string series, double value)
            => new(series, new DateTime(2026, 6, 14, 10, 0, 0, DateTimeKind.Utc), value);

        public IAsyncEnumerable<MetricSample> GetClusterNodeCpuAsync(IApplicationInsightsContext context, TimeSpan timeSpan, CancellationToken cancellationToken = default)
            => _hasNodes ? Of(Sample("cog-audry", 0.5), Sample("cog-braastad", 0.6)) : Empty<MetricSample>();
        public IAsyncEnumerable<MetricSample> GetClusterNodeMemoryAsync(IApplicationInsightsContext context, TimeSpan timeSpan, CancellationToken cancellationToken = default)
            => _hasNodes ? Of(Sample("cog-audry", 29), Sample("cog-braastad", 35)) : Empty<MetricSample>();
        public IAsyncEnumerable<MetricSample> GetClusterNodeFilesystemAsync(IApplicationInsightsContext context, TimeSpan timeSpan, CancellationToken cancellationToken = default)
            => _hasNodes ? Of(Sample("cog-audry", 1.7)) : Empty<MetricSample>();
        public IAsyncEnumerable<MetricSample> GetClusterPodCpuAsync(IApplicationInsightsContext context, string node, TimeSpan timeSpan, CancellationToken cancellationToken = default)
            => Of(Sample("agents/web-1", 0.1));
        public IAsyncEnumerable<MetricSample> GetClusterPodMemoryAsync(IApplicationInsightsContext context, string node, TimeSpan timeSpan, CancellationToken cancellationToken = default)
            => Of(Sample("agents/web-1", 200));
        public IAsyncEnumerable<MetricSample> GetClusterTotalCpuAsync(IApplicationInsightsContext context, TimeSpan timeSpan, CancellationToken cancellationToken = default)
            => _hasNodes ? Of(Sample("Cluster", 1.1)) : Empty<MetricSample>();
        public IAsyncEnumerable<MetricSample> GetClusterTotalMemoryAsync(IApplicationInsightsContext context, TimeSpan timeSpan, CancellationToken cancellationToken = default)
            => _hasNodes ? Of(Sample("Cluster", 31)) : Empty<MetricSample>();
        public IAsyncEnumerable<MetricSample> GetClusterTotalFilesystemAsync(IApplicationInsightsContext context, TimeSpan timeSpan, CancellationToken cancellationToken = default)
            => _hasNodes ? Of(Sample("Cluster", 3)) : Empty<MetricSample>();

        // Everything else returns empty — the view only needs the metric methods to render.
        public Task<bool> CanConnectAsync(IApplicationInsightsContext context) => Task.FromResult(true);
        public IAsyncEnumerable<VolumeBySource> GetVolumeBySourceAsync(IApplicationInsightsContext context, TimeSpan timeSpan, CancellationToken cancellationToken = default) => Empty<VolumeBySource>();
        public IAsyncEnumerable<VolumeBySource> GetVolumeBySourceAsync(IApplicationInsightsContext context, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default) => Empty<VolumeBySource>();
        public IAsyncEnumerable<VolumeTimelinePoint> GetVolumeTimelineAsync(IApplicationInsightsContext context, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default) => Empty<VolumeTimelinePoint>();
        public Task<CapTimeline> GetCapTimelineAsync(IApplicationInsightsContext context, int days, CancellationToken cancellationToken = default) => Task.FromResult(new CapTimeline { Days = [] });
        public IAsyncEnumerable<EnvironmentOption> GetEnvironments(IApplicationInsightsContext context) => Empty<EnvironmentOption>();
        public IAsyncEnumerable<LogItem> SearchAsync(IApplicationInsightsContext context, string environment, string text, TimeSpan timeSpan, SeverityLevel minSeverityLevel = SeverityLevel.Verbose, CancellationToken cancellationToken = default) => Empty<LogItem>();
        public IAsyncEnumerable<MeasureData> GetMeasureAsync(IApplicationInsightsContext context, string environment, TimeSpan timeSpan, CancellationToken cancellationToken = default) => Empty<MeasureData>();
        public IAsyncEnumerable<CountData> GetCountAsync(IApplicationInsightsContext context, string environment, TimeSpan timeSpan, CancellationToken cancellationToken = default) => Empty<CountData>();
        public Task<LogDetails> GetDetail(IApplicationInsightsContext context, string id, LogSource source, string environment, TimeSpan timeSpan) => Task.FromResult<LogDetails>(null);
        public Task<SummaryData> GetSummary(IApplicationInsightsContext context, string fingerprint, LogSource source, string environment, TimeSpan timeSpan, int maxItems = 100, string application = null) => Task.FromResult<SummaryData>(null);
        public IAsyncEnumerable<SummarySubset> GetSummaries(IApplicationInsightsContext context, string environment, TimeSpan timeSpan, CancellationToken cancellationToken = default) => Empty<SummarySubset>();
        public IAsyncEnumerable<VersionMatrixCell> GetVersionMatrixAsync(IApplicationInsightsContext context, TimeSpan? lookback = null, bool forceRefresh = false) => Empty<VersionMatrixCell>();
        public IAsyncEnumerable<LogItem> SearchByIncidentIdAsync(IApplicationInsightsContext context, string incidentId, TimeSpan timeSpan) => Empty<LogItem>();
        public IAsyncEnumerable<LogItem> SearchByCorrelationIdAsync(IApplicationInsightsContext context, string correlationId, TimeSpan timeSpan) => Empty<LogItem>();
        public IAsyncEnumerable<MetricSample> GetCpuUtilizationAsync(IApplicationInsightsContext context, TimeSpan timeSpan, CancellationToken cancellationToken = default) => Empty<MetricSample>();
        public IAsyncEnumerable<MetricSample> GetMemoryUtilizationAsync(IApplicationInsightsContext context, TimeSpan timeSpan, CancellationToken cancellationToken = default) => Empty<MetricSample>();
        public IAsyncEnumerable<MetricSample> GetDiskFreeAsync(IApplicationInsightsContext context, TimeSpan timeSpan, CancellationToken cancellationToken = default) => Empty<MetricSample>();
        public IAsyncEnumerable<DiskCapacity> GetDiskCapacityAsync(IApplicationInsightsContext context, TimeSpan timeSpan, CancellationToken cancellationToken = default) => Empty<DiskCapacity>();
        public IAsyncEnumerable<MetricSample> GetNetworkThroughputAsync(IApplicationInsightsContext context, TimeSpan timeSpan, CancellationToken cancellationToken = default) => Empty<MetricSample>();
        public IAsyncEnumerable<LogCountByServiceCell> GetLogCountByServiceAsync(IApplicationInsightsContext context, TimeSpan timeSpan, CancellationToken cancellationToken = default) => Empty<LogCountByServiceCell>();

        private static async IAsyncEnumerable<T> Empty<T>()
        {
            await Task.CompletedTask;
            yield break;
        }

        private static async IAsyncEnumerable<T> Of<T>(params T[] items)
        {
            await Task.CompletedTask;
            foreach (var i in items) yield return i;
        }
    }
}
