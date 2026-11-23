using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quilt4Net.Toolkit.Features.Api;
using Quilt4Net.Toolkit.Features.Probe;

namespace Quilt4Net.Toolkit.Features.Health;

internal class HealthService : IHealthService
{
    private readonly IHostEnvironment _hostEnvironment;
    private readonly IServiceProvider _serviceProvider;
    private readonly IHostedServiceProbeRegistry _hostedServiceProbeRegistry;
    private readonly Quilt4NetHealthApiOptions _apiOption;
    private readonly ILogger<HealthService> _logger;
    private readonly ComponentCheckCache _componentCheckCache;

    public HealthService(IHostEnvironment hostEnvironment, IServiceProvider serviceProvider, IHostedServiceProbeRegistry hostedServiceProbeRegistry, Quilt4NetHealthApiOptions apiOption, ILogger<HealthService> logger = null, ComponentCheckCache componentCheckCache = null)
    {
        _hostEnvironment = hostEnvironment;
        _serviceProvider = serviceProvider;
        _hostedServiceProbeRegistry = hostedServiceProbeRegistry;
        _apiOption = apiOption;
        _logger = logger;
        // In DI the singleton is injected; the fallback keeps direct construction (e.g. tests) working.
        // Per-component result caching only takes effect when this instance outlives a request, which
        // it does via the registered singleton.
        _componentCheckCache = componentCheckCache ?? new ComponentCheckCache(TimeProvider.System);
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

        var tasksFromServices = _apiOption.ComponentServices.SelectMany(x => ((IComponentService)_serviceProvider.GetService(x))?.GetComponents())
            .Where(filter ?? (_ => true))
            .Select(x => RunTaskAsync(x, cancellationToken));
        var tasksFromAdd = _apiOption.Components.Select(x => RunTaskAsync(x, cancellationToken));
        var taskList = tasksFromServices.Union(tasksFromAdd).ToList();

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
        var name = string.IsNullOrEmpty(component.Name) ? "Component" : component.Name;

        // Reuse a fresh cached result within CacheDuration (and coalesce concurrent runs); when
        // caching is off this runs the check directly.
        return _componentCheckCache.GetOrRunAsync(name, component.CacheDuration, () => RunCheckAsync(component, name, cancellationToken));
    }

    private async Task<RunTaskResult> RunCheckAsync(Component component, string name, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger?.LogTrace("Starting check for {name} component.", name);
            var status = await InvokeCheckAsync(component, cancellationToken);
            stopwatch.Stop();
            _logger?.LogTrace("Complete check for {name} component after {elapsed}.", name, stopwatch.Elapsed);
            return new RunTaskResult { Name = name, Essential = component.Essential, Result = status, Elapsed = stopwatch.Elapsed };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller went away: no result, so nothing is cached for the next caller.
            throw;
        }
        catch (Exception exception) when (exception is OperationCanceledException or TimeoutException)
        {
            // Timed out (or the request was aborted). Report a failed result — mapped to
            // Degraded/Unhealthy per Essential by BuildStatus — instead of hanging. No exception
            // detail: a timeout is an expected, self-describing outcome, not a crash.
            stopwatch.Stop();
            _logger?.LogWarning("Timed out check for {name} component after {elapsed}.", name, stopwatch.Elapsed);
            return new RunTaskResult
            {
                Name = name,
                Essential = component.Essential,
                Result = new CheckResult { Success = false, Message = $"Check timed out after {stopwatch.Elapsed}." },
                Elapsed = stopwatch.Elapsed
            };
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            Guid? correlationId = null;
            if (_logger != null)
            {
                correlationId = Guid.NewGuid();
                _logger.LogError("Failed check for {name} component after {elapsed}. {message} [CorrelationId: {correlationId}]", name, stopwatch.Elapsed, exception.Message, correlationId);
            }

            return new RunTaskResult { Name = name, Essential = component.Essential, Result = new CheckResult { Success = false }, Elapsed = stopwatch.Elapsed, Exception = exception, CorrelationId = correlationId };
        }
    }

    private async Task<CheckResult> InvokeCheckAsync(Component component, CancellationToken cancellationToken)
    {
        var timeout = component.Timeout is { } value && value > TimeSpan.Zero ? value : (TimeSpan?)null;

        using var boundCts = timeout != null || cancellationToken.CanBeCanceled ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken) : null;
        if (timeout != null)
        {
            boundCts.CancelAfter(timeout.Value);
        }

        var token = boundCts?.Token ?? CancellationToken.None;

        // Created before the check starts, so a cooperative check sees its token cancel before the race ends.
        var bound = boundCts == null ? null : Task.Delay(System.Threading.Timeout.Infinite, token);
        var checkTask = StartCheck(component, token);
        if (bound == null)
        {
            return await checkTask;
        }

        var completed = await Task.WhenAny(checkTask, bound);
        if (completed == checkTask)
        {
            return await checkTask;
        }

        ObserveFault(checkTask);
        cancellationToken.ThrowIfCancellationRequested();
        throw new TimeoutException($"Health check '{component.Name}' did not complete within {timeout}.");
    }

    private Task<CheckResult> StartCheck(Component component, CancellationToken token)
    {
        return component.CheckWithCancellationAsync != null
            ? Task.Run(() => component.CheckWithCancellationAsync(_serviceProvider, token), CancellationToken.None)
            : Task.Run(() => component.CheckAsync(_serviceProvider), CancellationToken.None);
    }

    private static void ObserveFault(Task task)
    {
        _ = task.ContinueWith(
            static t => { _ = t.Exception; },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}