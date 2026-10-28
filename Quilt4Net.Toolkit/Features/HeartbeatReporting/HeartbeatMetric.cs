namespace Quilt4Net.Toolkit.Features.HeartbeatReporting;

/// <summary>
/// One value measured at a heartbeat, such as <c>cpu.process</c> or <c>memory</c> in percent, or one of
/// the application's own added with <see cref="HeartbeatReportingOptions.AddMetric(string, Func{double}, string)"/>.
/// Quilt4Net keeps them for 100 days and graphs them per instance.
/// </summary>
/// <param name="Name">Unique within a report, at most 64 characters.</param>
/// <param name="Value">A finite number.</param>
/// <param name="Unit">What the value counts, such as <c>percent</c>. Optional.</param>
public record HeartbeatMetric(string Name, double Value, string Unit = null);
