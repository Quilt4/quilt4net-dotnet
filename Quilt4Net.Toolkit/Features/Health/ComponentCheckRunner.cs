using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Quilt4Net.Toolkit.Features.Health;

/// <summary>
/// Runs one <see cref="Component"/> check and turns its outcome into a <see cref="RunTaskResult"/>. The check
/// starts off the calling thread and is bounded by <see cref="Component.Timeout"/> on both the plain and the
/// cancellation-aware path. Shared by the probe path (<see cref="HealthService"/>) and the background refresh
/// (<see cref="ComponentRefreshService"/>), which differ only in the service provider and token they pass.
/// </summary>
internal sealed class ComponentCheckRunner
{
    private readonly ILogger _logger;

    public ComponentCheckRunner(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Run the check. A timeout or a failure becomes a failed result; cancellation of
    /// <paramref name="cancellationToken"/> itself is rethrown, so nothing is produced (or cached) for a caller
    /// that went away.
    /// </summary>
    public async Task<RunTaskResult> RunAsync(Component component, string name, IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger?.LogTrace("Starting check for {name} component.", name);
            var status = await InvokeCheckAsync(component, serviceProvider, cancellationToken);
            stopwatch.Stop();
            _logger?.LogTrace("Complete check for {name} component after {elapsed}.", name, stopwatch.Elapsed);
            return new RunTaskResult { Name = name, Essential = component.Essential, Result = status, Elapsed = stopwatch.Elapsed };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is OperationCanceledException or TimeoutException)
        {
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

    private static async Task<CheckResult> InvokeCheckAsync(Component component, IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var timeout = component.Timeout is { } value && value > TimeSpan.Zero ? value : (TimeSpan?)null;

        using var boundCts = timeout != null || cancellationToken.CanBeCanceled ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken) : null;
        if (timeout != null)
        {
            boundCts.CancelAfter(timeout.Value);
        }

        var token = boundCts?.Token ?? CancellationToken.None;

        var bound = boundCts == null ? null : Task.Delay(System.Threading.Timeout.Infinite, token);
        var checkTask = StartCheck(component, serviceProvider, token);
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

    private static Task<CheckResult> StartCheck(Component component, IServiceProvider serviceProvider, CancellationToken token)
    {
        return component.CheckWithCancellationAsync != null
            ? Task.Run(() => component.CheckWithCancellationAsync(serviceProvider, token), CancellationToken.None)
            : Task.Run(() => component.CheckAsync(serviceProvider), CancellationToken.None);
    }

    internal static void ObserveFault(Task task)
    {
        _ = task.ContinueWith(
            static t => { _ = t.Exception; },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
