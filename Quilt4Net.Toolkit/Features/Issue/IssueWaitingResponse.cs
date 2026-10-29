namespace Quilt4Net.Toolkit.Features.Issue;

/// <summary>
/// An issue flagged as waiting (#131): held up, with the reason saying by what. The flag is not a
/// state, so the issue keeps its state, its phase and its claim.
/// </summary>
public record IssueWaitingResponse
{
    /// <summary>What the issue is waiting for, in a sentence.</summary>
    public required string Reason { get; init; }

    /// <summary>What kind of thing it waits on, or <c>null</c> when not said.</summary>
    public IssueWaitsOn? WaitsOn { get; init; }

    /// <summary>
    /// When to look at it again (UTC), or <c>null</c>. Passing it does not clear the flag: somebody
    /// confirms the wait is over. See <see cref="IsDue"/>.
    /// </summary>
    public DateTime? Until { get; init; }

    /// <summary>Who set the flag, as the caller stated it. Empty when nobody was stated.</summary>
    public string SetBy { get; init; } = string.Empty;

    /// <summary>When the flag was set (UTC).</summary>
    public required DateTime SetUtc { get; init; }

    /// <summary>
    /// <see cref="Until"/> has passed, so the wait should be checked. Judged by the server, so every
    /// client agrees on it.
    /// </summary>
    public bool IsDue { get; init; }
}
