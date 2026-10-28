namespace Quilt4Net.Toolkit;

/// <summary>
/// Which processor use a heartbeat reports, as a percentage of all cores. See
/// <see cref="HeartbeatReportingOptions.Cpu"/>.
/// </summary>
[Flags]
public enum CpuMetric
{
    /// <summary>No processor use is reported.</summary>
    None = 0,

    /// <summary>
    /// This application's own use, as <c>cpu.process</c>. Works the same on Windows, Linux and macOS, and
    /// inside a container.
    /// </summary>
    Process = 1,

    /// <summary>
    /// The whole machine's use, as <c>cpu.machine</c>, on Windows and Linux. Inside a container this is
    /// the host's, not the container's. Reported from the second heartbeat on, as it is measured between
    /// two heartbeats.
    /// </summary>
    Machine = 2,

    /// <summary>Both <see cref="Process"/> and <see cref="Machine"/>.</summary>
    Both = Process | Machine
}
