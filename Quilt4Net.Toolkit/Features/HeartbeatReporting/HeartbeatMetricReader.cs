using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Quilt4Net.Toolkit.Features.HeartbeatReporting;

/// <summary>
/// Reads the metrics a heartbeat carries: the built-in processor and memory use, and the application's
/// own, from <see cref="HeartbeatReportingOptions.AddMetric(string, Func{double}, string)"/> and from every
/// <see cref="IHeartbeatMetricProvider"/>. Processor use is measured between two heartbeats, so the
/// reader keeps the previous reading.
/// </summary>
/// <remarks>
/// A metric that cannot be read, is late, or breaks a rule Quilt4Net would refuse the whole report for,
/// is left out of that heartbeat and logged; it never stops the heartbeat, which is what tells Quilt4Net
/// the instance is alive.
/// </remarks>
internal sealed class HeartbeatMetricReader
{
    public const string ProcessCpu = "cpu.process";
    public const string MachineCpu = "cpu.machine";
    public const string ProcessMemory = "memory.process";
    public const string MachineMemory = "memory.machine";
    public const string Percent = "percent";

    /// <summary>What Quilt4Net takes in one heartbeat; more would refuse the report.</summary>
    public const int MaxMetrics = 20;
    private const int MaxNameLength = 64;
    private const int MaxUnitLength = 16;

    private readonly HeartbeatReportingOptions _options;
    private readonly ILogger _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    private TimeSpan? _lastProcessorTime;
    private DateTime _lastProcessWall;
    private (ulong Idle, ulong Total)? _lastMachine;

    public HeartbeatMetricReader(HeartbeatReportingOptions options, ILogger logger, IServiceScopeFactory scopeFactory = null)
    {
        _options = options;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    public async Task<HeartbeatMetric[]> ReadAsync(CancellationToken cancellationToken)
    {
        var metrics = new List<HeartbeatMetric>();

        if (_options.Cpu.HasFlag(CpuMetric.Process)) Add(metrics, ProcessCpu, ReadProcessCpu);
        if (_options.Cpu.HasFlag(CpuMetric.Machine)) Add(metrics, MachineCpu, ReadMachineCpu);
        if (_options.Memory.HasFlag(MemoryMetric.Process)) Add(metrics, ProcessMemory, ReadProcessMemory);
        if (_options.Memory.HasFlag(MemoryMetric.Machine)) Add(metrics, MachineMemory, ReadMachineMemory);

        // Every source at once, sharing one timeout: a slow service call holds up nothing but itself.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.MetricTimeout);

        await using var scope = _scopeFactory?.CreateAsyncScope();
        var providers = scope?.ServiceProvider.GetServices<IHeartbeatMetricProvider>().ToArray() ?? [];

        var sources = _options.CustomMetrics
            .Select(c => Run(c.Name, async ct => (IEnumerable<HeartbeatMetric>)[new HeartbeatMetric(c.Name, await c.Read(ct), c.Unit)], timeout.Token, cancellationToken))
            .Concat(providers.Select(p => Run(p.GetType().Name, p.GetMetricsAsync, timeout.Token, cancellationToken)))
            .ToArray();

        foreach (var result in await Task.WhenAll(sources))
        {
            foreach (var metric in result) Accept(metrics, metric);
        }

        return metrics.ToArray();
    }

    /// <summary>One source, given the shared timeout. Late or failing, it gives nothing, and says so.</summary>
    private async Task<HeartbeatMetric[]> Run(string source, Func<CancellationToken, Task<IEnumerable<HeartbeatMetric>>> read, CancellationToken timeout, CancellationToken stopping)
    {
        try
        {
            // WaitAsync as well as the token: a source that ignores the token is still not waited for.
            return (await read(timeout).WaitAsync(timeout))?.Where(x => x != null).ToArray() ?? [];
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            _logger.LogWarning("The heartbeat metrics from '{Source}' did not come within {Timeout} and were left out.", source, _options.MetricTimeout);
            return [];
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "The heartbeat metrics from '{Source}' could not be read and were left out.", source);
            return [];
        }
    }

    /// <summary>
    /// Adds a metric unless Quilt4Net would refuse it, and with it the whole heartbeat: no name, too
    /// long, not finite, a name already taken, or one too many.
    /// </summary>
    private void Accept(List<HeartbeatMetric> metrics, HeartbeatMetric metric)
    {
        var name = metric.Name?.Trim();
        string problem = null;
        if (string.IsNullOrEmpty(name)) problem = "has no name";
        else if (name.Length > MaxNameLength) problem = $"has a name over {MaxNameLength} characters";
        else if (!double.IsFinite(metric.Value)) problem = "is not a finite number";
        else if (metrics.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))) problem = "has a name that is already taken";
        else if (metrics.Count >= MaxMetrics) problem = $"is one more than the {MaxMetrics} a heartbeat carries";

        if (problem != null)
        {
            _logger.LogWarning("The heartbeat metric '{Metric}' {Problem} and was left out.", metric.Name, problem);
            return;
        }

        var unit = metric.Unit?.Trim();
        metrics.Add(metric with { Name = name, Unit = string.IsNullOrEmpty(unit) ? null : unit.Length > MaxUnitLength ? unit[..MaxUnitLength] : unit });
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
    /// This process's working set over the memory available to it: the machine's, or the container's
    /// limit, as the garbage collector sees it.
    /// </summary>
    private static double? ReadProcessMemory()
    {
        var available = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        if (available <= 0) return null;

        using var process = System.Diagnostics.Process.GetCurrentProcess();
        return process.WorkingSet64 * 100.0 / available;
    }

    /// <summary>
    /// All memory in use over what is available: the machine's, or the container's limit. From the
    /// garbage collector, so it is the same on every platform; empty until the first collection.
    /// </summary>
    private static double? ReadMachineMemory()
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
