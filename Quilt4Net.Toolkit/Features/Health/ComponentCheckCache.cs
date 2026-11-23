using System.Collections.Concurrent;

namespace Quilt4Net.Toolkit.Features.Health;

/// <summary>
/// Outcome of running a single component check. Shared between <see cref="HealthService"/> and
/// <see cref="ComponentCheckCache"/> so cached results can be reused across health polls.
/// </summary>
internal record RunTaskResult
{
    public required string Name { get; init; }
    public required bool Essential { get; init; }
    public required CheckResult Result { get; init; }
    public required TimeSpan Elapsed { get; init; }
    public Exception Exception { get; init; }
    public Guid? CorrelationId { get; init; }

    /// <summary>
    /// When the cache stored this result. Null for a result that was never cached.
    /// </summary>
    public DateTimeOffset? CheckedAt { get; init; }
}

/// <summary>
/// What the cache holds for one component: when its last result was produced (null if there is none yet) and
/// whether a background refresh is running.
/// </summary>
internal readonly record struct ComponentCacheState(DateTimeOffset? CheckedAt, bool Refreshing);

/// <summary>
/// Singleton cache for deep <see cref="Component"/> check results, keyed by component name. Mirrors
/// the Dependency-probe caching added in #119: within a component's <see cref="Component.CacheDuration"/>
/// the last <see cref="RunTaskResult"/> is reused instead of re-running the check, and concurrent runs
/// for the same component are coalesced through a per-key gate so a burst of polls triggers a single
/// check. Lives as a singleton because <see cref="HealthService"/> is transient — the cache must
/// outlive a single request to be effective. For a component with <see cref="Component.RefreshInBackground"/>
/// it also answers from the last result whatever its age and refreshes it in the background, one refresh at a time.
/// </summary>
internal sealed class ComponentCheckCache
{
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, CacheSlot> _cache = new();

    public ComponentCheckCache(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Return a cached result for <paramref name="name"/> if one is still fresh for
    /// <paramref name="cacheDuration"/>; otherwise run <paramref name="run"/> once (coalescing
    /// concurrent callers) and cache it. When <paramref name="cacheDuration"/> is null or
    /// non-positive, caching is bypassed entirely.
    /// </summary>
    public async Task<RunTaskResult> GetOrRunAsync(string name, TimeSpan? cacheDuration, Func<Task<RunTaskResult>> run)
    {
        if (cacheDuration is not { } duration || duration <= TimeSpan.Zero)
        {
            return await run();
        }

        var slot = GetSlot(name);

        if (TryGetFresh(slot, duration, out var cached))
        {
            return cached;
        }

        await slot.Gate.WaitAsync();
        try
        {
            if (TryGetFresh(slot, duration, out cached))
            {
                return cached;
            }

            return Store(slot, await run());
        }
        finally
        {
            slot.Gate.Release();
        }
    }

    /// <summary>
    /// Stale-while-refresh. Return the last result for <paramref name="name"/> at once, whatever its age, and
    /// start a refresh through <paramref name="run"/> when it is <paramref name="cacheDuration"/> old or older.
    /// Only when there is no result yet does the caller wait, for the refresh already running or a new one;
    /// <paramref name="waitToken"/> ends that wait, not the refresh.
    /// </summary>
    public Task<RunTaskResult> GetOrRefreshAsync(string name, TimeSpan cacheDuration, Func<Task<RunTaskResult>> run, CancellationToken waitToken)
    {
        var slot = GetSlot(name);
        var last = slot.Result;

        if (last == null)
        {
            return StartRefresh(slot, run).WaitAsync(waitToken);
        }

        if (!IsFresh(last, cacheDuration))
        {
            StartRefresh(slot, run);
        }

        return Task.FromResult(last);
    }

    /// <summary>
    /// Start a refresh of <paramref name="name"/> unless one is already running. Does not wait for it.
    /// </summary>
    public void Refresh(string name, Func<Task<RunTaskResult>> run)
    {
        StartRefresh(GetSlot(name), run);
    }

    public ComponentCacheState GetState(string name)
    {
        if (!_cache.TryGetValue(name, out var slot))
        {
            return new ComponentCacheState(null, false);
        }

        return new ComponentCacheState(slot.Result?.CheckedAt, slot.IsRefreshing);
    }

    private CacheSlot GetSlot(string name) => _cache.GetOrAdd(name, _ => new CacheSlot());

    private Task<RunTaskResult> StartRefresh(CacheSlot slot, Func<Task<RunTaskResult>> run)
    {
        lock (slot.Sync)
        {
            if (slot.Refreshing is { IsCompleted: false } running)
            {
                return running;
            }

            var refresh = Task.Run(async () => Store(slot, await run()));
            ComponentCheckRunner.ObserveFault(refresh);
            slot.Refreshing = refresh;
            return refresh;
        }
    }

    private RunTaskResult Store(CacheSlot slot, RunTaskResult result)
    {
        var stored = result with { CheckedAt = _timeProvider.GetUtcNow() };
        slot.Result = stored;
        return stored;
    }

    private bool IsFresh(RunTaskResult result, TimeSpan cacheDuration)
        => result.CheckedAt is { } checkedAt && _timeProvider.GetUtcNow() - checkedAt < cacheDuration;

    private bool TryGetFresh(CacheSlot slot, TimeSpan cacheDuration, out RunTaskResult result)
    {
        var last = slot.Result;
        if (last != null && IsFresh(last, cacheDuration))
        {
            result = last;
            return true;
        }

        result = null;
        return false;
    }

    private sealed class CacheSlot
    {
        private volatile RunTaskResult _result;

        public object Sync { get; } = new();

        public SemaphoreSlim Gate { get; } = new(1, 1);

        public RunTaskResult Result
        {
            get => _result;
            set => _result = value;
        }

        public Task<RunTaskResult> Refreshing { get; set; }

        public bool IsRefreshing
        {
            get
            {
                lock (Sync)
                {
                    return Refreshing is { IsCompleted: false };
                }
            }
        }
    }
}
