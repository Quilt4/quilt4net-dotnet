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
}
