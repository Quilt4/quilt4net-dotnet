using Bunit;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quilt4Net.Toolkit.Blazor.Features.Log;
using Quilt4Net.Toolkit.Features.ApplicationInsights;
using Xunit;

namespace Quilt4Net.Toolkit.Blazor.Tests;

public class LogEnvironmentSelectorIncidentTests : BunitContext
{
    public LogEnvironmentSelectorIncidentTests()
    {
        Services.AddLogging();
        Services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());
    }

    [Fact]
    public void Renders_alert_with_Incident_id_when_GetEnvironments_throws()
    {
        Services.AddSingleton<IApplicationInsightsService>(new ThrowingApplicationInsightsService(new InvalidOperationException("boom")));

        var cut = Render<LogEnvironmentSelector>();

        cut.Markup.Should().Contain("Could not load Application Insights environments.");
        cut.Markup.Should().MatchRegex(@"\[Incident:\s+[2-9A-HJ-NP-Z]{6}\]");
    }

    private sealed class ThrowingApplicationInsightsService : IApplicationInsightsService
    {
        private readonly Exception _ex;
        public ThrowingApplicationInsightsService(Exception ex) => _ex = ex;

        public Task<bool> CanConnectAsync(IApplicationInsightsContext context) => Task.FromResult(false);

        public IAsyncEnumerable<VolumeBySource> GetVolumeBySourceAsync(IApplicationInsightsContext context, TimeSpan timeSpan, CancellationToken cancellationToken = default) => Throw<VolumeBySource>();
        public IAsyncEnumerable<VolumeBySource> GetVolumeBySourceAsync(IApplicationInsightsContext context, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default) => Throw<VolumeBySource>();
        public IAsyncEnumerable<VolumeTimelinePoint> GetVolumeTimelineAsync(IApplicationInsightsContext context, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default) => Throw<VolumeTimelinePoint>();
        public Task<CapTimeline> GetCapTimelineAsync(IApplicationInsightsContext context, int days, CancellationToken cancellationToken = default) => Task.FromException<CapTimeline>(_ex);
        public IAsyncEnumerable<EnvironmentOption> GetEnvironments(IApplicationInsightsContext context) => Throw<EnvironmentOption>();
        public IAsyncEnumerable<LogItem> SearchAsync(IApplicationInsightsContext context, string environment, string text, TimeSpan timeSpan, SeverityLevel minSeverityLevel = SeverityLevel.Verbose, CancellationToken cancellationToken = default) => Throw<LogItem>();
        public IAsyncEnumerable<MeasureData> GetMeasureAsync(IApplicationInsightsContext context, string environment, TimeSpan timeSpan, CancellationToken cancellationToken = default) => Throw<MeasureData>();
        public IAsyncEnumerable<CountData> GetCountAsync(IApplicationInsightsContext context, string environment, TimeSpan timeSpan, CancellationToken cancellationToken = default) => Throw<CountData>();
        public Task<LogDetails> GetDetail(IApplicationInsightsContext context, string id, LogSource source, string environment, TimeSpan timeSpan) => Task.FromException<LogDetails>(_ex);
        public Task<SummaryData> GetSummary(IApplicationInsightsContext context, string fingerprint, LogSource source, string environment, TimeSpan timeSpan, int maxItems = 100, string application = null) => Task.FromException<SummaryData>(_ex);
        public IAsyncEnumerable<SummarySubset> GetSummaries(IApplicationInsightsContext context, string environment, TimeSpan timeSpan, CancellationToken cancellationToken = default) => Throw<SummarySubset>();
        public IAsyncEnumerable<VersionMatrixCell> GetVersionMatrixAsync(IApplicationInsightsContext context, TimeSpan? lookback = null, bool forceRefresh = false) => Throw<VersionMatrixCell>();
        public IAsyncEnumerable<LogItem> SearchByIncidentIdAsync(IApplicationInsightsContext context, string incidentId, TimeSpan timeSpan) => Throw<LogItem>();
        public IAsyncEnumerable<LogItem> SearchByCorrelationIdAsync(IApplicationInsightsContext context, string correlationId, TimeSpan timeSpan) => Throw<LogItem>();
        public IAsyncEnumerable<MetricSample> GetCpuUtilizationAsync(IApplicationInsightsContext context, TimeSpan timeSpan, CancellationToken cancellationToken = default) => Throw<MetricSample>();
        public IAsyncEnumerable<MetricSample> GetMemoryUtilizationAsync(IApplicationInsightsContext context, TimeSpan timeSpan, CancellationToken cancellationToken = default) => Throw<MetricSample>();
        public IAsyncEnumerable<MetricSample> GetDiskFreeAsync(IApplicationInsightsContext context, TimeSpan timeSpan, CancellationToken cancellationToken = default) => Throw<MetricSample>();
        public IAsyncEnumerable<DiskCapacity> GetDiskCapacityAsync(IApplicationInsightsContext context, TimeSpan timeSpan, CancellationToken cancellationToken = default) => Throw<DiskCapacity>();
        public IAsyncEnumerable<MetricSample> GetNetworkThroughputAsync(IApplicationInsightsContext context, TimeSpan timeSpan, CancellationToken cancellationToken = default) => Throw<MetricSample>();
        public IAsyncEnumerable<MetricSample> GetClusterNodeCpuAsync(IApplicationInsightsContext context, TimeSpan timeSpan, CancellationToken cancellationToken = default) => Throw<MetricSample>();
        public IAsyncEnumerable<MetricSample> GetClusterNodeMemoryAsync(IApplicationInsightsContext context, TimeSpan timeSpan, CancellationToken cancellationToken = default) => Throw<MetricSample>();
        public IAsyncEnumerable<MetricSample> GetClusterPodCpuAsync(IApplicationInsightsContext context, string node, TimeSpan timeSpan, CancellationToken cancellationToken = default) => Throw<MetricSample>();
        public IAsyncEnumerable<MetricSample> GetClusterPodMemoryAsync(IApplicationInsightsContext context, string node, TimeSpan timeSpan, CancellationToken cancellationToken = default) => Throw<MetricSample>();
        public IAsyncEnumerable<MetricSample> GetClusterNodeFilesystemAsync(IApplicationInsightsContext context, TimeSpan timeSpan, CancellationToken cancellationToken = default) => Throw<MetricSample>();
        public IAsyncEnumerable<MetricSample> GetClusterTotalCpuAsync(IApplicationInsightsContext context, TimeSpan timeSpan, CancellationToken cancellationToken = default) => Throw<MetricSample>();
        public IAsyncEnumerable<MetricSample> GetClusterTotalMemoryAsync(IApplicationInsightsContext context, TimeSpan timeSpan, CancellationToken cancellationToken = default) => Throw<MetricSample>();
        public IAsyncEnumerable<MetricSample> GetClusterTotalFilesystemAsync(IApplicationInsightsContext context, TimeSpan timeSpan, CancellationToken cancellationToken = default) => Throw<MetricSample>();
        public IAsyncEnumerable<LogCountByServiceCell> GetLogCountByServiceAsync(IApplicationInsightsContext context, TimeSpan timeSpan, CancellationToken cancellationToken = default) => Throw<LogCountByServiceCell>();

#pragma warning disable CS1998
        private async IAsyncEnumerable<T> Throw<T>()
#pragma warning restore CS1998
        {
            throw _ex;
            yield break;
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = string.Empty;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
