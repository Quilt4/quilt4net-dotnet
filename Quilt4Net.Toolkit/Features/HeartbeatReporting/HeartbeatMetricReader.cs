using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Quilt4Net.Toolkit.Features.HeartbeatReporting;

/// <summary>
/// Reads the metrics a heartbeat carries: the built-in processor and memory use, and the application's
/// own. Processor use is measured between two heartbeats, so the reader keeps the previous reading.
/// </summary>
/// <remarks>
/// A metric that cannot be read is left out of that heartbeat and logged; it never stops the heartbeat,
/// which is what tells Quilt4Net the instance is alive.
/// </remarks>
internal sealed class HeartbeatMetricReader
{
    public const string ProcessCpu = "cpu.process";
    public const string MachineCpu = "cpu.machine";
    public const string Memory = "memory";
    public const string Percent = "percent";

    private readonly HeartbeatReportingOptions _options;
    private readonly ILogger _logger;

    private TimeSpan? _lastProcessorTime;
    private DateTime _lastProcessWall;
    private (ulong Idle, ulong Total)? _lastMachine;

    public HeartbeatMetricReader(HeartbeatReportingOptions options, ILogger logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<HeartbeatMetric[]> ReadAsync(CancellationToken cancellationToken)
    {
        var metrics = new List<HeartbeatMetric>();

        if (_options.Cpu.HasFlag(CpuMetric.Process)) Add(metrics, ProcessCpu, ReadProcessCpu);
        if (_options.Cpu.HasFlag(CpuMetric.Machine)) Add(metrics, MachineCpu, ReadMachineCpu);
        if (_options.Memory) Add(metrics, Memory, ReadMemory);

        foreach (var custom in _options.CustomMetrics)
        {
            try
            {
                var value = await custom.Read(cancellationToken);
                if (double.IsFinite(value)) metrics.Add(new HeartbeatMetric(custom.Name, value, custom.Unit));
                else _logger.LogWarning("The heartbeat metric '{Metric}' was not a finite number and was left out.", custom.Name);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "The heartbeat metric '{Metric}' could not be read and was left out.", custom.Name);
            }
        }

        return metrics.ToArray();
    }

    private void Add(List<HeartbeatMetric> metrics, string name, Func<double?> read)
    {
        try
        {
            if (read() is { } value && double.IsFinite(value)) metrics.Add(new HeartbeatMetric(name, Math.Round(value, 1), Percent));
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "The heartbeat metric '{Metric}' could not be read and was left out.", name);
        }
    }

    /// <summary>
    /// This process's processor use since the previous heartbeat, over all cores. The first heartbeat
    /// measures from the process start.
    /// </summary>
    private double? ReadProcessCpu()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        var used = process.TotalProcessorTime;
        var now = DateTime.UtcNow;
        var since = _lastProcessorTime ?? TimeSpan.Zero;
        var from = _lastProcessorTime == null ? process.StartTime.ToUniversalTime() : _lastProcessWall;

        _lastProcessorTime = used;
        _lastProcessWall = now;

        var wall = (now - from).TotalMilliseconds * Environment.ProcessorCount;
        if (wall <= 0) return null;
        return Math.Clamp((used - since).TotalMilliseconds / wall * 100, 0, 100);
    }

    /// <summary>The machine's processor use since the previous heartbeat; nothing on the first, or off Windows and Linux.</summary>
    private double? ReadMachineCpu()
    {
        var current = OperatingSystem.IsWindows() ? WindowsTimes() : OperatingSystem.IsLinux() ? LinuxTimes() : null;
        if (current is not { } now) return null;

        var previous = _lastMachine;
        _lastMachine = now;
        if (previous is not { } before || now.Total <= before.Total) return null;

        var total = now.Total - before.Total;
        var idle = now.Idle - before.Idle;
        return Math.Clamp((total - idle) * 100.0 / total, 0, 100);
    }

    /// <summary>
    /// Memory in use over what is available to this process: the machine's, or the container's limit.
    /// From the garbage collector, so it is the same on every platform; empty until the first collection.
    /// </summary>
    private static double? ReadMemory()
    {
        var info = GC.GetGCMemoryInfo();
        return info.TotalAvailableMemoryBytes > 0 && info.MemoryLoadBytes > 0
            ? info.MemoryLoadBytes * 100.0 / info.TotalAvailableMemoryBytes
            : null;
    }

    /// <summary>The first line of <c>/proc/stat</c>: idle (with I/O wait) and total jiffies since boot.</summary>
    private static (ulong Idle, ulong Total)? LinuxTimes()
    {
        var line = File.ReadLines("/proc/stat").FirstOrDefault(x => x.StartsWith("cpu ", StringComparison.Ordinal));
        if (line == null) return null;

        var values = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Take(8)
            .Select(x => ulong.Parse(x, CultureInfo.InvariantCulture)).ToArray();
        if (values.Length < 5) return null;

        return (values[3] + values[4], values.Aggregate(0UL, (a, b) => a + b));
    }

    /// <summary>Idle and total processor time since boot; the kernel time includes the idle time.</summary>
    private static (ulong Idle, ulong Total)? WindowsTimes()
        => GetSystemTimes(out var idle, out var kernel, out var user) ? ((ulong)idle, (ulong)(kernel + user)) : null;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);
}
