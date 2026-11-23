using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quilt4Net.Toolkit.Features.Api;
using Quilt4Net.Toolkit.Features.Probe;

namespace Quilt4Net.Toolkit.Features.Health;

internal class HealthService : IHealthService
{
    private const string CheckedAtDetail = "checkedAt";

    private readonly IHostEnvironment _hostEnvironment;
    private readonly IServiceProvider _serviceProvider;
    private readonly IHostedServiceProbeRegistry _hostedServiceProbeRegistry;
    private readonly Quilt4NetHealthApiOptions _apiOption;
    private readonly ComponentCheckCache _componentCheckCache;
    private readonly ComponentRefreshService _componentRefreshService;
    private readonly ComponentCheckRunner _runner;

    public HealthService(IHostEnvironment hostEnvironment, IServiceProvider serviceProvider, IHostedServiceProbeRegistry hostedServiceProbeRegistry, Quilt4NetHealthApiOptions apiOption, ILogger<HealthService> logger = null, ComponentCheckCache componentCheckCache = null, ComponentRefreshService componentRefreshService = null)
    {
        _hostEnvironment = hostEnvironment;
        _serviceProvider = serviceProvider;
        _hostedServiceProbeRegistry = hostedServiceProbeRegistry;
        _apiOption = apiOption;
        // In DI the singleton is injected; the fallback keeps direct construction (e.g. tests) working.
        // Per-component result caching only takes effect when this instance outlives a request, which
        // it does via the registered singleton.
        _componentCheckCache = componentCheckCache ?? new ComponentCheckCache(TimeProvider.System);
        // Without the refresher (direct construction) RefreshInBackground falls back to plain caching.
        _componentRefreshService = componentRefreshService;
        _runner = new ComponentCheckRunner(logger);
    }

    public async IAsyncEnumerable<KeyValuePair<string, HealthComponent>> GetStatusAsync(Func<Component, bool> filter, bool includeProbes, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (includeProbes)
        {
            await foreach (var probe in _hostedServiceProbeRegistry.GetProbesAsync().WithCancellation(cancellationToken))
            {
                yield return probe;
            }
        }

        var tasksFromServices = ComponentSource.FromServices(_apiOption, _serviceProvider)
            .Where(filter ?? (_ => true))
            .Select(x => RunTaskAsync(x, cancellationToken));
        var tasksFromAdd = _apiOption.Components.Select(x => RunTaskAsync(x, cancellationToken));
        var taskList = tasksFromServices.Concat(tasksFromAdd).ToList();

        while (taskList.Count > 0)
        {
            var completedTask = await Task.WhenAny(taskList); // Get the first task that completes
            cancellationToken.ThrowIfCancellationRequested();
            yield return BuildResponse(completedTask); // Return the completed task
            taskList.Remove(completedTask); // Remove the completed task from the list
        }
    }

    private KeyValuePair<string, HealthComponent> BuildResponse(Task<RunTaskResult> x)
    {
        var result = new KeyValuePair<string, HealthComponent>(x.Result.Name, new HealthComponent
        {
            Status = BuildStatus(x.Result.Result.Success, x.Result.Essential),
            Details = new Dictionary<string, string>
            {
                { "elapsed", $"{x.Result.Elapsed}" },
            }
        });

        if (!string.IsNullOrEmpty(x.Result.Result.Message))
        {
            result.Value.Details.TryAdd("message", x.Result.Result.Message);
        }

        if (x.Result.CheckedAt is { } checkedAt)
        {
            result.Value.Details.TryAdd(CheckedAtDetail, checkedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        }

        if (x.Result.Exception != null)
        {
            var correlationIdMessage = BuildCorrelationIdMessage(x.Result.CorrelationId);
            var exceptionDataLevel = _apiOption.ExceptionDetail ?? GetDefaultExceptionLevel();
            switch (exceptionDataLevel)
            {
                case ExceptionDetailLevel.Hidden:
                    result.Value.Details.TryAdd("exception.message", $"Hidden exception. {correlationIdMessage}");
                    break;
                case ExceptionDetailLevel.Message:
                    result.Value.Details.TryAdd("exception.message", $"{x.Result.Exception.Message} {correlationIdMessage}");
                    break;
                case ExceptionDetailLevel.StackTrace:
                    result.Value.Details.TryAdd("exception.message", $"{x.Result.Exception.Message} {correlationIdMessage}");
                    result.Value.Details.TryAdd("exception.stacktrace", x.Result.Exception.StackTrace);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(_apiOption.ExceptionDetail), _apiOption.ExceptionDetail, null);
            }
        }

        return result;
    }

    private ExceptionDetailLevel? GetDefaultExceptionLevel()
    {
        if (_hostEnvironment.IsProduction()) return ExceptionDetailLevel.Hidden;
        if (_hostEnvironment.IsDevelopment()) return ExceptionDetailLevel.StackTrace;
        return ExceptionDetailLevel.Message;
    }

    private static string BuildCorrelationIdMessage(Guid? correlationId)
    {
        if (correlationId == null)
        {
            return "This message has not been logged.";
        }

        return $"Logged with correlationId {correlationId}";
    }

    private static HealthStatus BuildStatus(bool success, bool essential)
    {
        if (success) return HealthStatus.Healthy;

        if (!essential) return HealthStatus.Degraded;

        return HealthStatus.Unhealthy;
    }

    private Task<RunTaskResult> RunTaskAsync(Component component, CancellationToken cancellationToken)
    {
        var name = ComponentSource.NameOf(component);

        if (_componentRefreshService != null && ComponentSource.IsRefreshedInBackground(component))
        {
            return _componentRefreshService.GetAsync(component, name, cancellationToken);
        }

        // Reuse a fresh cached result within CacheDuration (and coalesce concurrent runs); when
        // caching is off this runs the check directly.
        return _componentCheckCache.GetOrRunAsync(name, component.CacheDuration, () => _runner.RunAsync(component, name, _serviceProvider, cancellationToken));
    }
}
