using System.Net.Http.Json;
using System.Reflection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Quilt4Net.Toolkit.Features.HeartbeatReporting;

/// <summary>
/// Reports this instance's capabilities and metrics to Quilt4Net on an interval, starting at once.
/// </summary>
/// <remarks>
/// A failed report is logged and retried on the next interval, never thrown: the application must
/// keep running when Quilt4Net cannot be reached, and a missed heartbeat is exactly what the rules
/// on Quilt4Net are there to notice.
/// </remarks>
internal class HeartbeatReporter : BackgroundService
{
    public const string HttpClientName = "Quilt4Net.HeartbeatReporting";

    private const string Root = "Api/Heartbeat";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly HeartbeatReportingOptions _options;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly ILogger<HeartbeatReporter> _logger;
    private readonly HeartbeatMetricReader _metrics;

    public HeartbeatReporter(IHttpClientFactory httpClientFactory, IOptions<HeartbeatReportingOptions> options, ILogger<HeartbeatReporter> logger, IHostEnvironment hostEnvironment = null)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _hostEnvironment = hostEnvironment;
        _logger = logger;
        _metrics = new HeartbeatMetricReader(_options, logger);
    }

    private static readonly TimeSpan ForbiddenLogInterval = TimeSpan.FromHours(1);
    private DateTime _forbiddenLoggedAt = DateTime.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await ReportOnceAsync(stoppingToken);

            try { await Task.Delay(_options.Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Sends one heartbeat. Returns whether Quilt4Net accepted it.</summary>
    internal async Task<bool> ReportOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Read at each heartbeat, so the heartbeat's interval is the metrics' frequency too.
            var report = BuildReport() with { Metrics = await _metrics.ReadAsync(cancellationToken) };
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.PostAsJsonAsync(Root, report, cancellationToken);
            if (response.IsSuccessStatusCode) return true;

            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                // The key lacks heartbeat:write, or the team has switched Monitoring off on the server - a
                // choice that refuses every heartbeat, so said once an hour with the server's sentence.
                if (DateTime.UtcNow - _forbiddenLoggedAt >= ForbiddenLogInterval)
                {
                    _forbiddenLoggedAt = DateTime.UtcNow;
                    var detail = Framework.ProblemResponse.DetailOf(await response.Content.ReadAsStringAsync(cancellationToken)) ?? response.ReasonPhrase;
                    _logger.LogWarning("Quilt4Net refused heartbeats (403): {Detail} Logged at most once an hour.", detail);
                }
                return false;
            }

            _logger.LogWarning("Quilt4Net refused a heartbeat with {StatusCode}.", (int)response.StatusCode);
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Could not send a heartbeat to Quilt4Net; will retry next interval.");
            return false;
        }
    }

    internal HeartbeatReport BuildReport()
    {
        var entry = Assembly.GetEntryAssembly()?.GetName();
        return new HeartbeatReport(
            _options.Application ?? entry?.Name,
            _options.Environment ?? _hostEnvironment?.EnvironmentName,
            _options.Instance ?? System.Environment.MachineName,
            entry?.Version?.ToString(),
            _options.Capabilities ?? []);
    }
}
