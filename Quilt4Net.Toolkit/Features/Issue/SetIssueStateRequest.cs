namespace Quilt4Net.Toolkit.Features.Issue;

/// <summary>
/// Moves an issue to another state. Requires an API key carrying the <c>issue:write</c> scope.
/// </summary>
/// <remarks>
/// The move is checked against the team's workflow. A transition the workflow does not permit is
/// rejected, and the rejection names the states the issue <i>can</i> reach from where it is.
/// </remarks>
public record SetIssueStateRequest
{
    /// <summary>The state to move to. Must name a state defined by the team's workflow.</summary>
    public required string State { get; init; }

    /// <summary>
    /// Why the issue is ending, naming one of the workflow's resolutions. Required when
    /// <see cref="State"/> is terminal; rejected when it is not.
    /// </summary>
    /// <remarks>
    /// Required on the way in and cleared on the way out, deliberately. A closed issue with no
    /// reason is the ambiguity resolutions exist to remove, so the move is refused rather than
    /// defaulted — guessing <c>Done</c> would quietly record a delivery that may not have happened.
    /// Reopening clears it, so an issue that is closed, reopened and closed again cannot carry the
    /// first decision's reason.
    /// </remarks>
    public string Resolution { get; init; } = string.Empty;

    /// <summary>
    /// Which phase the issue is in within <see cref="State"/>, naming one of that state's
    /// sub-states. Rejected on a terminal state, whose outcome is <see cref="Resolution"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Moving to another state clears the sub-state unless this names one of the target's. Naming
    /// the state the issue is already in changes only the sub-state, and needs no transition:
    /// <c>null</c> leaves it as it is, an empty string clears it.
    /// </para>
    /// <para>
    /// <c>null</c> by default so a client built before sub-states existed — which re-sends the
    /// current state to take over a claim — cannot wipe a sub-state it never knew about.
    /// </para>
    /// </remarks>
    public string SubState { get; init; }
}
