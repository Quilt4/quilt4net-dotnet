using Quilt4Net.Toolkit.Features.HeartbeatReporting;

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
    /// Whether to report at all. Default is true. Set false in configuration to keep the registration
    /// in code but stop reporting on a machine or in an environment, such as a developer's laptop that
    /// would otherwise count as a live instance.
    /// </summary>
    public bool Enabled { get; set; } = true;

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

    /// <summary>
    /// Which processor use each heartbeat reports, as a percentage of all cores. Default is
    /// <see cref="CpuMetric.Process"/>: this application's own use, the same on every platform. Set
    /// <see cref="CpuMetric.None"/> to send none.
    /// </summary>
    public CpuMetric Cpu { get; set; } = CpuMetric.Process;

    /// <summary>
    /// Whether each heartbeat reports memory in use, as <c>memory</c> in percent of what is available to
    /// the process (the machine's, or the container's limit). Default is true.
    /// </summary>
    public bool Memory { get; set; } = true;

    /// <summary>The most metrics of the application's own: with the three built-in ones, Quilt4Net takes at most 20.</summary>
    public const int MaxCustomMetrics = 17;

    private const int MaxMetricNameLength = 64;
    private const int MaxUnitLength = 16;
    private static readonly string[] BuiltInMetrics = [HeartbeatMetricReader.ProcessCpu, HeartbeatMetricReader.MachineCpu, HeartbeatMetricReader.Memory];

    private readonly List<CustomHeartbeatMetric> _customMetrics = [];

    /// <summary>The application's own metrics, read on each heartbeat.</summary>
    internal IReadOnlyList<CustomHeartbeatMetric> CustomMetrics => _customMetrics;

    /// <summary>
    /// Adds a value of the application's own to every heartbeat, such as a queue's length. Read on each
    /// heartbeat; a read that throws is logged and left out of that heartbeat, never stopping it.
    /// </summary>
    /// <param name="name">Unique, at most 64 characters, such as <c>queue.length</c>.</param>
    /// <param name="value">Reads the value.</param>
    /// <param name="unit">What it counts, such as <c>percent</c> or <c>items</c>; the graph's axis label. Optional.</param>
    /// <exception cref="ArgumentException">The name is empty, too long, taken or built in, or there are too many.</exception>
    public HeartbeatReportingOptions AddMetric(string name, Func<double> value, string unit = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        return AddMetric(name, _ => Task.FromResult(value()), unit);
    }

    /// <inheritdoc cref="AddMetric(string, Func{double}, string)"/>
    public HeartbeatReportingOptions AddMetric(string name, Func<Task<double>> value, string unit = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        return AddMetric(name, _ => value(), unit);
    }

    /// <inheritdoc cref="AddMetric(string, Func{double}, string)"/>
    public HeartbeatReportingOptions AddMetric(string name, Func<CancellationToken, Task<double>> value, string unit = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed)) throw new ArgumentException("A metric needs a name.", nameof(name));
        if (trimmed.Length > MaxMetricNameLength) throw new ArgumentException($"A metric name is at most {MaxMetricNameLength} characters.", nameof(name));
        if (BuiltInMetrics.Contains(trimmed, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException($"'{trimmed}' is built in; set {nameof(Cpu)} or {nameof(Memory)} instead.", nameof(name));
        if (_customMetrics.Any(x => string.Equals(x.Name, trimmed, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException($"The metric '{trimmed}' is already added.", nameof(name));
        if (_customMetrics.Count >= MaxCustomMetrics) throw new ArgumentException($"At most {MaxCustomMetrics} metrics of your own.", nameof(name));
        if (unit?.Trim().Length > MaxUnitLength) throw new ArgumentException($"A unit is at most {MaxUnitLength} characters.", nameof(unit));

        _customMetrics.Add(new CustomHeartbeatMetric(trimmed, string.IsNullOrWhiteSpace(unit) ? null : unit.Trim(), value));
        return this;
    }
}

/// <summary>A metric of the application's own, as added with <see cref="HeartbeatReportingOptions.AddMetric(string, Func{double}, string)"/>.</summary>
internal sealed record CustomHeartbeatMetric(string Name, string Unit, Func<CancellationToken, Task<double>> Read);
