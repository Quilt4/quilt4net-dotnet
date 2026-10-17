namespace Quilt4Net.Toolkit.Framework;

/// <summary>
/// Collects requests made close together and sends them as one batch.
/// </summary>
/// <remarks>
/// <para>
/// The first request starts a window; everything that arrives before it closes goes out together. A
/// batch that reaches <c>maxSize</c> goes out at once without waiting for the window.
/// </para>
/// <para>
/// Each caller awaits only its own answer. If the batch fails as a whole, every caller in it sees the
/// same exception, exactly as each would have seen its own failure before.
/// </para>
/// </remarks>
internal sealed class RequestBatcher<TRequest, TResponse>
{
    private readonly Func<IReadOnlyList<TRequest>, Task<IReadOnlyList<TResponse>>> _send;
    private readonly TimeSpan _window;
    private readonly int _maxSize;
    private readonly object _sync = new();
    private List<Pending> _pending = [];

    public RequestBatcher(Func<IReadOnlyList<TRequest>, Task<IReadOnlyList<TResponse>>> send, TimeSpan window, int maxSize)
    {
        if (maxSize < 1) throw new ArgumentOutOfRangeException(nameof(maxSize), "A batch holds at least one request.");

        _send = send;
        _window = window;
        _maxSize = maxSize;
    }

    public Task<TResponse> EnqueueAsync(TRequest request)
    {
        // Continuations run off the sending thread, so one slow caller cannot hold up the others'.
        var completion = new TaskCompletionSource<TResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        List<Pending> full = null;
        bool opensWindow;
        lock (_sync)
        {
            _pending.Add(new Pending(request, completion));
            opensWindow = _pending.Count == 1;
            if (_pending.Count >= _maxSize)
            {
                full = _pending;
                _pending = [];
            }
        }

        if (full != null) _ = SendAsync(full);
        else if (opensWindow) _ = FlushAfterWindowAsync();

        return completion.Task;
    }

    private async Task FlushAfterWindowAsync()
    {
        await Task.Delay(_window);

        // A batch that filled up meanwhile has already gone; this then sends whatever came after it,
        // a little early, which costs nothing.
        List<Pending> batch;
        lock (_sync)
        {
            if (_pending.Count == 0) return;
            batch = _pending;
            _pending = [];
        }

        await SendAsync(batch);
    }

    private async Task SendAsync(List<Pending> batch)
    {
        try
        {
            var responses = await _send(batch.Select(x => x.Request).ToArray());
            if (responses == null || responses.Count != batch.Count)
                throw new InvalidOperationException($"A batch of {batch.Count} requests was answered with {responses?.Count ?? 0} responses.");

            for (var i = 0; i < batch.Count; i++) batch[i].Completion.TrySetResult(responses[i]);
        }
        catch (Exception e)
        {
            foreach (var item in batch) item.Completion.TrySetException(e);
        }
    }

    private sealed record Pending(TRequest Request, TaskCompletionSource<TResponse> Completion);
}
