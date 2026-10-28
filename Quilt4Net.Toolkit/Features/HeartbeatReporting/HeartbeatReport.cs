namespace Quilt4Net.Toolkit.Features.HeartbeatReporting;

/// <summary>
/// One heartbeat, as sent to Quilt4Net.
/// </summary>
/// <param name="Application">The reporting application.</param>
/// <param name="Environment">The reporting environment, if known.</param>
/// <param name="Instance">The reporting machine or instance.</param>
/// <param name="Version">The application's version, if known.</param>
/// <param name="Capabilities">What this instance can do.</param>
public record HeartbeatReport(string Application, string Environment, string Instance, string Version, string[] Capabilities)
{
    /// <summary>
    /// Values measured at this heartbeat: the built-in processor and memory use, and the application's
    /// own. Empty when none are configured.
    /// </summary>
    public HeartbeatMetric[] Metrics { get; init; } = [];

    /// <summary>The machine's own name, even when <see cref="Instance"/> is set to something else.</summary>
    public string MachineName { get; init; }

    /// <summary>The runtime, such as <c>.NET 10.0.1</c>.</summary>
    public string Runtime { get; init; }

    /// <summary>The operating system, such as <c>Microsoft Windows 10.0.26100</c>.</summary>
    public string OperatingSystem { get; init; }

    /// <summary>When the process started, so Quilt4Net shows its uptime and its restarts.</summary>
    public DateTime? StartedUtc { get; init; }

    /// <summary>How often this instance reports, so Quilt4Net knows when it has gone silent.</summary>
    public int? IntervalSeconds { get; init; }

    /// <summary>The Quilt4Net Toolkit version sending the heartbeat.</summary>
    public string ToolkitVersion { get; init; }

    /// <summary>How many processors the instance sees.</summary>
    public int? ProcessorCount { get; init; }
}
