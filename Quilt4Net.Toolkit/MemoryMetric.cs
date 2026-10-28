namespace Quilt4Net.Toolkit;

/// <summary>
/// Which memory use a heartbeat reports, as a percentage of the memory available to the process: the
/// machine's, or the container's limit. See <see cref="HeartbeatReportingOptions.Memory"/>.
/// </summary>
[Flags]
public enum MemoryMetric
{
    /// <summary>No memory use is reported.</summary>
    None = 0,

    /// <summary>This application's own working set, as <c>memory.process</c>.</summary>
    Process = 1,

    /// <summary>
    /// All memory in use, as <c>memory.machine</c>. Unlike <see cref="CpuMetric.Machine"/>, inside a
    /// container this is measured against the container's limit.
    /// </summary>
    Machine = 2,

    /// <summary>Both <see cref="Process"/> and <see cref="Machine"/>.</summary>
    Both = Process | Machine
}
