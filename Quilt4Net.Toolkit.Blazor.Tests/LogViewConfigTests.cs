using Bunit;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Quilt4Net.Toolkit.Blazor.Features.Log;
using Quilt4Net.Toolkit.Features.ApplicationInsights;
using Xunit;

namespace Quilt4Net.Toolkit.Blazor.Tests;

public class LogViewConfigTests : BunitContext
{
    public LogViewConfigTests()
    {
        // Register a stub service so the LogView's service-missing guard passes — these tests
        // exercise the *options*-missing guard, which runs after the service guard.
        Services.AddSingleton<IApplicationInsightsService>(new StubApplicationInsightsService());
    }

    [Fact]
    public void Shows_Error_When_ApplicationInsights_Not_Configured()
    {
        Services.AddSingleton(Options.Create(new ApplicationInsightsOptions()));

        var cut = Render<LogView>();

        cut.Markup.Should().Contain("Application Insights is not configured");
    }

    [Fact]
    public void Shows_Error_When_ClientSecret_Is_Missing()
    {
        Services.AddSingleton(Options.Create(new ApplicationInsightsOptions
        {
            TenantId = "tenant",
            WorkspaceId = "workspace",
            ClientId = "client",
            ClientSecret = null
        }));

        var cut = Render<LogView>();

        cut.Markup.Should().Contain("Application Insights is not configured");
    }

    [Fact]
    public void Shows_Error_When_Options_Not_Registered()
    {
        var cut = Render<LogView>();

        cut.Markup.Should().Contain("Application Insights is not configured");
    }

    private sealed class StubApplicationInsightsService : IApplicationInsightsService
    {
        public Task<bool> CanConnectAsync(IApplicationInsightsContext context) => Task.FromResult(false);
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
        public IAsyncEnumerable<MetricSample> GetClusterNodeCpuAsync(IApplicationInsightsContext context, TimeSpan timeSpan, CancellationToken cancellationToken = default) => Empty<MetricSample>();
        public IAsyncEnumerable<MetricSample> GetClusterNodeMemoryAsync(IApplicationInsightsContext context, TimeSpan timeSpan, CancellationToken cancellationToken = default) => Empty<MetricSample>();
        public IAsyncEnumerable<MetricSample> GetClusterPodCpuAsync(IApplicationInsightsContext context, string node, TimeSpan timeSpan, CancellationToken cancellationToken = default) => Empty<MetricSample>();
        public IAsyncEnumerable<MetricSample> GetClusterPodMemoryAsync(IApplicationInsightsContext context, string node, TimeSpan timeSpan, CancellationToken cancellationToken = default) => Empty<MetricSample>();
        public IAsyncEnumerable<MetricSample> GetClusterNodeFilesystemAsync(IApplicationInsightsContext context, TimeSpan timeSpan, CancellationToken cancellationToken = default) => Empty<MetricSample>();
        public IAsyncEnumerable<MetricSample> GetClusterTotalCpuAsync(IApplicationInsightsContext context, TimeSpan timeSpan, CancellationToken cancellationToken = default) => Empty<MetricSample>();
        public IAsyncEnumerable<MetricSample> GetClusterTotalMemoryAsync(IApplicationInsightsContext context, TimeSpan timeSpan, CancellationToken cancellationToken = default) => Empty<MetricSample>();
        public IAsyncEnumerable<MetricSample> GetClusterTotalFilesystemAsync(IApplicationInsightsContext context, TimeSpan timeSpan, CancellationToken cancellationToken = default) => Empty<MetricSample>();
        public IAsyncEnumerable<LogCountByServiceCell> GetLogCountByServiceAsync(IApplicationInsightsContext context, TimeSpan timeSpan, CancellationToken cancellationToken = default) => Empty<LogCountByServiceCell>();

        private static async IAsyncEnumerable<T> Empty<T>()
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
