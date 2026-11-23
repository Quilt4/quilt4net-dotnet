using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quilt4Net.Toolkit.Features.Api;

namespace Quilt4Net.Toolkit.Features.Health;

/// <summary>
/// Keeps the results of components with <see cref="Component.RefreshInBackground"/> fresh, so a probe can answer
/// from them without waiting. At start it runs each such component once, then re-runs each whenever its result
/// reaches <see cref="Component.CacheDuration"/>. Every refresh, including one a probe starts, runs in its own
/// service scope and on the host's lifetime, never the request's, since the request scope is gone once the
/// response is sent. When no component opts in it stops at once.
/// </summary>
internal sealed class ComponentRefreshService : BackgroundService
{
    private static readonly TimeSpan MinimumWait = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan RetryWait = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Quilt4NetHealthApiOptions _options;
    private readonly ComponentCheckCache _cache;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ComponentRefreshService> _logger;
    private readonly ComponentCheckRunner _runner;
    private readonly CancellationTokenSource _stopping = new();
    private readonly CancellationToken _stoppingToken;

    public ComponentRefreshService(IServiceScopeFactory scopeFactory, Quilt4NetHealthApiOptions options, ComponentCheckCache cache, TimeProvider timeProvider, ILogger<ComponentRefreshService> logger = null)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _cache = cache;
        _timeProvider = timeProvider;
        _logger = logger;
        _runner = new ComponentCheckRunner(logger);
        _stoppingToken = _stopping.Token;
    }

    /// <summary>
    /// The probe's way in: the last result at once, a refresh started when it is stale, and a wait only when
    /// there is no result yet. <paramref name="waitToken"/> ends that wait, never the refresh.
    /// </summary>
    internal Task<RunTaskResult> GetAsync(Component component, string name, CancellationToken waitToken)
    {
        return _cache.GetOrRefreshAsync(name, component.CacheDuration.Value, () => RunInOwnScopeAsync(component, name), waitToken);
    }

    /// <summary>
    /// One pass of the refresher: start a refresh for every opted-in component with no result or a result
    /// <see cref="Component.CacheDuration"/> old, and say how long to wait before the next one is due.
    /// </summary>
    internal TimeSpan RefreshDue(IReadOnlyCollection<Component> components)
    {
        var now = _timeProvider.GetUtcNow();
        var wait = components.Min(x => x.CacheDuration.Value);

        foreach (var component in components)
        {
            var name = ComponentSource.NameOf(component);
            var duration = component.CacheDuration.Value;
            var state = _cache.GetState(name);
            if (state.Refreshing)
            {
                continue;
            }

            var dueIn = state.CheckedAt is { } checkedAt ? checkedAt + duration - now : TimeSpan.Zero;
            if (dueIn <= TimeSpan.Zero)
            {
                _cache.Refresh(name, () => RunInOwnScopeAsync(component, name));
            }
            else if (dueIn < wait)
            {
                wait = dueIn;
            }
        }

        return wait < MinimumWait ? MinimumWait : wait;
    }

    /// <summary>
    /// The components that opt in, one per name, resolved in a scope of their own.
    /// </summary>
    internal IReadOnlyCollection<Component> ListOptedIn()
    {
        using var scope = _scopeFactory.CreateScope();
        return ComponentSource.All(_options, scope.ServiceProvider)
            .Where(ComponentSource.IsRefreshedInBackground)
            .GroupBy(ComponentSource.NameOf)
            .Select(x => x.First())
            .ToArray();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        await using var stopping = stoppingToken.Register(_stopping.Cancel);

        var listed = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan wait;
            try
            {
                var components = ListOptedIn();
                if (components.Count == 0 && !listed)
                {
                    _logger?.LogDebug("No health component refreshes in the background, so the refresher stops.");
                    return;
                }

                listed = true;
                wait = components.Count == 0 ? RetryWait : RefreshDue(components);
            }
            catch (Exception exception)
            {
                _logger?.LogWarning(exception, "Could not refresh the background health components. Trying again in {wait}.", RetryWait);
                wait = RetryWait;
            }

            try
            {
                await Task.Delay(wait, _timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stopping.CancelAsync();
        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        // The base cancels the stopping token, which cancels _stopping through ExecuteAsync's registration.
        base.Dispose();
        _stopping.Dispose();
    }

    private async Task<RunTaskResult> RunInOwnScopeAsync(Component fallback, string name)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var component = FindIn(scope.ServiceProvider, name) ?? fallback;
        return await _runner.RunAsync(component, name, scope.ServiceProvider, _stoppingToken);
    }

    private Component FindIn(IServiceProvider serviceProvider, string name)
    {
        try
        {
            return ComponentSource.All(_options, serviceProvider)
                .FirstOrDefault(x => ComponentSource.NameOf(x) == name && ComponentSource.IsRefreshedInBackground(x));
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "Could not resolve health component {name} for a background refresh; using the one the probe had.", name);
            return null;
        }
    }
}
