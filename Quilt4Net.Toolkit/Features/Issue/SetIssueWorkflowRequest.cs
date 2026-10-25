namespace Quilt4Net.Toolkit.Features.Issue;

/// <summary>
/// Replaces a team's issue workflow. Requires an API key carrying the <c>issue:manage</c> scope.
/// </summary>
/// <remarks>
/// The replacement is validated before anything is written: exactly one state must be initial, every
/// transition must name states that exist, and <b>every state currently in use by an issue must
/// still be defined</b>. A workflow that would orphan live issues is rejected and the error names
/// them, rather than leaving issues in a state the workflow no longer knows about.
/// </remarks>
public record SetIssueWorkflowRequest
{
    /// <summary>The states to define, in display order.</summary>
    public required IssueWorkflowStateResponse[] States { get; init; }

    /// <summary>The moves to permit.</summary>
    public required IssueWorkflowTransitionResponse[] Transitions { get; init; }

    /// <summary>
    /// The ways an issue may end. Validated like the states: names must be unique, and
    /// <b>a resolution currently held by an issue must still be defined</b>.
    /// </summary>
    /// <remarks>
    /// Optional so a caller that does not care about resolutions can keep sending the old two-field
    /// payload. Note what that means, because it is a replace rather than a merge: omitting this on a
    /// team that <i>has</i> resolutions asks to remove them, and will be refused while any issue
    /// still holds one — the same protection the states already get.
    /// </remarks>
    public IssueWorkflowResolutionResponse[] Resolutions { get; init; } = [];
}
