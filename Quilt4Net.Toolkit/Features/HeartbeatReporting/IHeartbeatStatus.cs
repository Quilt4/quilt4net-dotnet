namespace Quilt4Net.Toolkit.Features.HeartbeatReporting;

/// <summary>
/// What this instance's heartbeat reporting is doing (#144), for a status page: what it reports, when
/// it last sent, and what Quilt4Net answered. Registered by <c>AddQuilt4NetHeartbeatReporting</c>, also
/// when reporting is switched off, so a page can say so.
/// </summary>
public interface IHeartbeatStatus
{
    /// <summary>The state after the latest heartbeat.</summary>
    HeartbeatState Current { get; }

    /// <summary>Sends a heartbeat now, outside the interval. False when reporting is off or Quilt4Net did not accept it.</summary>
    Task<bool> SendNowAsync(CancellationToken cancellationToken = default);
}

/// <summary>Heartbeat reporting as it stands.</summary>
public sealed record HeartbeatState
{
    /// <summary>Reporting is on (<c>Quilt4Net:HeartbeatReporting:Enabled</c>).</summary>
    public bool Enabled { get; init; }

    public string Quilt4NetAddress { get; init; }

    /// <summary>A key is configured. The key itself is never exposed.</summary>
    public bool HasApiKey { get; init; }

    public TimeSpan Interval { get; init; }

    /// <summary>The report last sent: application, environment, instance, version, capabilities, details and metrics. Null before the first.</summary>
    public HeartbeatReport LastReport { get; init; }

    public DateTime? LastAttemptUtc { get; init; }

    /// <summary>When Quilt4Net last accepted one.</summary>
    public DateTime? LastAcceptedUtc { get; init; }

    /// <summary>The HTTP status of the last answer; null when the call did not complete.</summary>
    public int? LastStatusCode { get; init; }

    /// <summary>Why the last heartbeat was not accepted: the server's sentence, or the error. Null when it was.</summary>
    public string LastProblem { get; init; }

    /// <summary>When the next one goes, on the interval.</summary>
    public DateTime? NextDueUtc { get; init; }
}

/// <summary>Holds <see cref="HeartbeatState"/>; the reporter writes it after each heartbeat.</summary>
internal sealed class HeartbeatStatus : IHeartbeatStatus
{
    private HeartbeatState _current;

    public HeartbeatStatus(HeartbeatReportingOptions options)
    {
        _current = new HeartbeatState
        {
            Enabled = options.Enabled,
            Quilt4NetAddress = options.Quilt4NetAddress,
            HasApiKey = !string.IsNullOrEmpty(options.ApiKey),
            Interval = options.Interval
        };
    }

    public HeartbeatState Current => Volatile.Read(ref _current);

    /// <summary>Set by the reporter when it starts: sends one heartbeat.</summary>
    internal Func<CancellationToken, Task<bool>> Sender { get; set; }

    public Task<bool> SendNowAsync(CancellationToken cancellationToken = default)
        => Sender?.Invoke(cancellationToken) ?? Task.FromResult(false);

    internal void Update(Func<HeartbeatState, HeartbeatState> change)
    {
        HeartbeatState before, after;
        do
        {
            before = Volatile.Read(ref _current);
            after = change(before);
        } while (Interlocked.CompareExchange(ref _current, after, before) != before);
    }
}
