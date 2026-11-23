namespace Quilt4Net.Toolkit.Features.Health;

/// <summary>
/// Component for availability checking.
/// </summary>
public record Component
{
    /// <summary>
    /// Name of the component. This name needs to be unique.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Non-essential component will be considered Degraded if they fail.
    /// Essential components will be considered to Unhealthy/Unready that will result in 503 response.
    /// Default is true.
    /// </summary>
    public bool Essential { get; init; } = true;

    ///// <summary>
    ///// Used by Readyness to check if a component is needed for the system to be considered to be ready.
    ///// By default, the parameter Essential is used do determine if the component is needed to be considered to be ready.
    ///// This value can be set to true or false to override the Essential parameter.
    ///// </summary>
    //public bool? NeededToBeReady { get; init; }

    /// <summary>
    /// Method that performs the check for the component.
    /// </summary>
    public required Func<IServiceProvider, Task<CheckResult>> CheckAsync { get; init; }

    /// <summary>
    /// Optional. Cancellation-aware check. When set it is used <b>instead of</b> <see cref="CheckAsync"/>
    /// and receives a token that is cancelled when <see cref="Timeout"/> elapses (or the request is
    /// aborted). Prefer this over <see cref="CheckAsync"/> when the check does cancellable I/O: on
    /// timeout the check can actually abort and free its resources (e.g. an HTTP connection), rather
    /// than being left running while only the wait is abandoned.
    /// Default is null (use <see cref="CheckAsync"/>).
    /// </summary>
    public Func<IServiceProvider, CancellationToken, Task<CheckResult>> CheckWithCancellationAsync { get; init; }

    /// <summary>
    /// Optional. How long a successful-or-failed <see cref="CheckResult"/> is cached (keyed by
    /// <see cref="Name"/>) before the check is run again. Repeated polls within this window reuse the
    /// cached result and concurrent runs are coalesced into a single check — so sustained polling of a
    /// deep endpoint runs each check at most once per window instead of on every request. A cached
    /// component's details carry <c>checkedAt</c>, the UTC time its result was produced. A probe that finds
    /// the result expired waits for a new one, unless <see cref="RefreshInBackground"/> is set.
    /// Set to null (default) or <see cref="TimeSpan.Zero"/> to disable caching and run every time.
    /// Relies on <see cref="Name"/> being unique.
    /// </summary>
    public TimeSpan? CacheDuration { get; init; }

    /// <summary>
    /// Optional. Answer a probe from this component's last result at once, whatever its age, and refresh the
    /// result in the background instead of making the probe wait for the check. A refresh starts when a probe
    /// finds the result <see cref="CacheDuration"/> old, and a hosted refresher runs the check once at startup
    /// and again whenever the result reaches <see cref="CacheDuration"/>, so it stays recent even when probes are
    /// rare. Only a probe that arrives before the first result waits. <see cref="Timeout"/> then bounds a check
    /// nobody is waiting on, so a slow but working dependency can be given longer than a probe would tolerate.
    /// A result is at most about <see cref="CacheDuration"/> plus <see cref="Timeout"/> old while the app runs,
    /// and its <c>checkedAt</c> detail says when it was produced.
    /// Requires a positive <see cref="CacheDuration"/>; without one it has no effect. Best suited to non-essential
    /// components: on an essential one a stale Healthy result can hide an outage for that long.
    /// Default is false.
    /// </summary>
    public bool RefreshInBackground { get; init; }

    /// <summary>
    /// Optional. Bounds how long the check may run. On timeout the component is reported as a failed
    /// <see cref="CheckResult"/> (mapped to Degraded/Unhealthy per <see cref="Essential"/>) with a
    /// clear message, instead of hanging and holding resources. The bound holds on both paths and
    /// counts from before the check is invoked, so time a check spends blocking before its first
    /// <c>await</c> counts too. With <see cref="CheckWithCancellationAsync"/> the token is also
    /// cancelled, so a cooperative check can abort and free its resources; a check that ignores it is
    /// left to finish on its own while the component is already reported as timed out.
    /// Set to null (default) or <see cref="TimeSpan.Zero"/> for no timeout.
    /// </summary>
    public TimeSpan? Timeout { get; init; }
}