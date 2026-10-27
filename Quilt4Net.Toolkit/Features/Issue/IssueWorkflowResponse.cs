namespace Quilt4Net.Toolkit.Features.Issue;

/// <summary>
/// A team's issue workflow: the states an issue may be in, and the moves between them that are
/// allowed. One workflow governs every issue on the team.
/// </summary>
public record IssueWorkflowResponse
{
    /// <summary>The defined states, in display order.</summary>
    public required IssueWorkflowStateResponse[] States { get; init; }

    /// <summary>The permitted moves. A move not listed here is rejected.</summary>
    public required IssueWorkflowTransitionResponse[] Transitions { get; init; }

    /// <summary>
    /// The ways an issue may end, in display order. A move into a terminal state names one of these.
    /// </summary>
    /// <remarks>
    /// Not <c>required</c>, for the same reason as <see cref="IssueResponse.References"/>: the
    /// Toolkit publishes before the Server that populates it, so for the length of that window a
    /// client on this version talks to a server that sends no <c>resolutions</c> at all. Empty means
    /// "this server does not do resolutions", which a caller should read as *do not offer a choice*
    /// rather than as *this team has no way to close an issue*.
    /// </remarks>
    public IssueWorkflowResolutionResponse[] Resolutions { get; init; } = [];
}

/// <summary>One state in an <see cref="IssueWorkflowResponse"/>.</summary>
public record IssueWorkflowStateResponse
{
    /// <summary>State name, unique within the workflow, and what an issue stores.</summary>
    public required string Name { get; init; }

    /// <summary>Display order, ascending.</summary>
    public required int Order { get; init; }

    /// <summary>
    /// Whether a new issue starts here. Exactly one state is the initial one.
    /// </summary>
    public required bool IsInitial { get; init; }

    /// <summary>
    /// Whether an issue in this state counts as finished. Terminal issues are hidden from the
    /// roadmap unless they are an endpoint of a drawn edge, where they explain the edge.
    /// </summary>
    public required bool IsTerminal { get; init; }

    /// <summary>
    /// The phases an issue may be in within this state, in display order. Empty on a terminal state,
    /// whose outcomes are <see cref="IssueWorkflowResponse.Resolutions"/>.
    /// </summary>
    /// <remarks>
    /// Not <c>required</c>, for the same reason as <see cref="IssueResponse.References"/>: the Toolkit
    /// publishes before the Server that populates it. Empty reads as "no sub-states", which is what
    /// an older server means.
    /// </remarks>
    public IssueWorkflowSubStateResponse[] SubStates { get; init; } = [];
}

/// <summary>One permitted move in an <see cref="IssueWorkflowResponse"/>.</summary>
public record IssueWorkflowTransitionResponse
{
    /// <summary>State being moved from.</summary>
    public required string From { get; init; }

    /// <summary>State being moved to.</summary>
    public required string To { get; init; }
}
