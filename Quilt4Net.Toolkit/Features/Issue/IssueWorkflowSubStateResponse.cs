namespace Quilt4Net.Toolkit.Features.Issue;

/// <summary>
/// One phase an issue can be in within a non-terminal state — <c>Doing · Testing</c>,
/// <c>Doing · Ready to deploy</c>. Declared by the team's workflow, per state.
/// </summary>
/// <remarks>
/// <para>
/// A sub-state rather than another state, because the rules that matter are keyed to the state:
/// claims follow it, the board draws one column per state, and every state needs its own transitions.
/// A sub-state adds the detail and leaves those alone. Moving between the sub-states of one state is
/// not a transition and needs no edge in the workflow.
/// </para>
/// <para>
/// Always optional: an issue may hold none. The terminal state has no sub-states of this kind —
/// how an issue ended is its <see cref="IssueResponse.Resolution"/>, declared by
/// <see cref="IssueWorkflowResponse.Resolutions"/>, and that one is required.
/// </para>
/// </remarks>
public record IssueWorkflowSubStateResponse
{
    /// <summary>The sub-state's name, unique within its state, and what an issue stores.</summary>
    public required string Name { get; init; }

    /// <summary>Display order, ascending. Only for display: any sub-state may follow any other.</summary>
    public required int Order { get; init; }
}
