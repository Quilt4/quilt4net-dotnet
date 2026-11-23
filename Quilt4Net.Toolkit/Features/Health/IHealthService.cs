namespace Quilt4Net.Toolkit.Features.Health;

/// <summary>
/// Service for Health.
/// </summary>
public interface IHealthService
{
    /// <summary>
    /// Performs Health checks. Every component's check starts at once, off the calling thread, so a check
    /// that blocks does not hold up the others; each is bounded by its <see cref="Component.Timeout"/>.
    /// </summary>
    /// <param name="filter"></param>
    /// <param name="includeProbes"></param>
    /// <param name="cancellationToken">Ends every check still running; the sequence then throws <see cref="OperationCanceledException"/>.</param>
    /// <returns>The components in the order their checks complete, not the order they were registered.</returns>
    IAsyncEnumerable<KeyValuePair<string, HealthComponent>> GetStatusAsync(Func<Component, bool> filter = null, bool includeProbes = true, CancellationToken cancellationToken = default);
}