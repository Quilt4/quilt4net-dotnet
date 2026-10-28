using System.Net.Http.Json;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly HeartbeatStatus _status;

    public HeartbeatReporter(IHttpClientFactory httpClientFactory, IOptions<HeartbeatReportingOptions> options, ILogger<HeartbeatReporter> logger, IHostEnvironment hostEnvironment = null, IServiceScopeFactory scopeFactory = null,
        HeartbeatStatus status = null)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _hostEnvironment = hostEnvironment;
        _logger = logger;
        _metrics = new HeartbeatMetricReader(_options, logger, scopeFactory);
        _status = status;
        if (_status != null) _status.Sender = ReportOnceAsync;
    }

    private static readonly TimeSpan ForbiddenLogInterval = TimeSpan.FromHours(1);
    private DateTime _forbiddenLoggedAt = DateTime.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await ReportOnceAsync(stoppingToken);

            var next = DateTime.UtcNow.Add(_options.Interval);
            _status?.Update(s => s with { NextDueUtc = next });
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
            var attempt = DateTime.UtcNow;
            _status?.Update(s => s with { LastReport = report, LastAttemptUtc = attempt });
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.PostAsJsonAsync(Root, report, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                _status?.Update(s => s with { LastAcceptedUtc = attempt, LastStatusCode = (int)response.StatusCode, LastProblem = null });
                return true;
            }

            var detail = Framework.ProblemResponse.DetailOf(await response.Content.ReadAsStringAsync(cancellationToken)) ?? response.ReasonPhrase;
            _status?.Update(s => s with { LastStatusCode = (int)response.StatusCode, LastProblem = detail });
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                // The key lacks heartbeat:write, or the team has switched Monitoring off on the server - a
                // choice that refuses every heartbeat, so said once an hour with the server's sentence.
                if (DateTime.UtcNow - _forbiddenLoggedAt >= ForbiddenLogInterval)
                {
                    _forbiddenLoggedAt = DateTime.UtcNow;
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
            _status?.Update(s => s with { LastStatusCode = null, LastProblem = e.Message });
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
            _options.Capabilities ?? [])
        {
            MachineName = System.Environment.MachineName,
            Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            OperatingSystem = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            StartedUtc = ProcessStartUtc.Value,
            IntervalSeconds = (int)Math.Round(_options.Interval.TotalSeconds),
            ToolkitVersion = ToolkitVersion.Value,
            ProcessorCount = System.Environment.ProcessorCount
        };
    }

    private static readonly Lazy<DateTime?> ProcessStartUtc = new(() =>
    {
        try
        {
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            return process.StartTime.ToUniversalTime();
        }
        catch (Exception e) when (e is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    });

    /// <summary>The informational version, without the build metadata after <c>+</c>, which is the commit.</summary>
    private static readonly Lazy<string> ToolkitVersion = new(() =>
    {
        var assembly = typeof(HeartbeatReporter).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return informational?.Split('+')[0] ?? assembly.GetName().Version?.ToString();
    });
}
