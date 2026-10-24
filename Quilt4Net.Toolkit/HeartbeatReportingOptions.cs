namespace Quilt4Net.Toolkit;

/// <summary>
/// Options for reporting heartbeats with capabilities to Quilt4Net.
/// </summary>
/// <remarks>
/// Not the Application Insights heartbeat in <c>Quilt4Net.Toolkit.Health</c>
/// (<c>Quilt4Net:HealthApi:Heartbeat</c>). That one sends availability telemetry; this one tells
/// Quilt4Net which capabilities this instance has, so a rule there can raise an alarm when a
/// capability goes silent.
/// </remarks>
public record HeartbeatReportingOptions
{
    /// <summary>
    /// Address of the Quilt4Net server. Defaulted on the type so an unbound
    /// <c>IOptions&lt;HeartbeatReportingOptions&gt;</c> still carries a usable URL.
    /// </summary>
    public string Quilt4NetAddress { get; set; } = "https://quilt4net.com/";

    /// <summary>
    /// Team API key used for every call. Reporting needs <c>heartbeat:write</c>, which an ordinary
    /// application key has.
    /// </summary>
    public string ApiKey { get; set; }

    /// <summary>
    /// What this instance can do, such as <c>import</c> or <c>export</c>. A rule on Quilt4Net names
    /// a capability and alarms when no instance with it has reported in time.
    /// </summary>
    public string[] Capabilities { get; set; } = [];

    /// <summary>
    /// How often to report. Default is 5 minutes. A rule's silence window should be a few intervals,
    /// so one late report does not raise an alarm.
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Application name to report. When not set, the entry assembly's name is used.</summary>
    public string Application { get; set; }

    /// <summary>Environment name to report. When not set, the host environment's name is used.</summary>
    public string Environment { get; set; }

    /// <summary>Instance name to report, which tells two copies of the application apart. When not set, the machine name is used.</summary>
    public string Instance { get; set; }
}
