using Quilt4Net.Toolkit.Features.HeartbeatReporting;

namespace Quilt4Net.Toolkit;

/// <summary>
/// Supplies metrics of the application's own to every heartbeat, from whatever it can reach: a queue's
/// length, a database count, the answer of another service. Register it with
/// <see cref="HeartbeatReportingOptions.AddMetricProvider{T}"/>.
/// </summary>
/// <remarks>
/// <para>
/// Resolved from dependency injection in a scope of its own for each heartbeat, so it can take any
/// service in its constructor, a scoped one such as a database context included.
/// </para>
/// <para>
/// Called at each heartbeat, alongside every other provider, and given
/// <see cref="HeartbeatReportingOptions.MetricTimeout"/> to answer. The token is cancelled when the time
/// is up or the application stops; pass it on to the calls the provider makes. A provider that throws,
/// or does not answer in time, is logged and left out of that heartbeat; the heartbeat is still sent.
/// </para>
/// </remarks>
public interface IHeartbeatMetricProvider
{
    /// <summary>
    /// The metrics for this heartbeat. Names must be unique across every provider and the built-in
    /// metrics, at most 64 characters; values must be finite. A metric that breaks a rule is left out.
    /// </summary>
    /// <param name="cancellationToken">Cancelled when the provider runs out of time, or the application stops.</param>
    Task<IEnumerable<HeartbeatMetric>> GetMetricsAsync(CancellationToken cancellationToken);
}
