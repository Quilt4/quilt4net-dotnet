namespace Quilt4Net.Toolkit.Features.Issue;

/// <summary>
/// Flags an issue as waiting (#131), or changes the flag it has. Requires an API key carrying the
/// <c>issue:write</c> scope.
/// </summary>
/// <remarks>
/// Allowed on any open state; refused on a terminal one, and closing an issue clears its flag.
/// Moving between open states keeps it, so a <c>Todo</c> issue put down with a date to resume
/// carries that date with it.
/// </remarks>
public record SetIssueWaitingRequest
{
    /// <summary>What the issue is waiting for. Required: a flag with no reason is what it replaces.</summary>
    public required string Reason { get; init; }

    /// <summary>What kind of thing it waits on. Optional.</summary>
    public IssueWaitsOn? WaitsOn { get; init; }

    /// <summary>When to look at it again (UTC). Optional; once passed, the flag reads as due.</summary>
    public DateTime? Until { get; init; }
}
