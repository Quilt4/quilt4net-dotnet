namespace Quilt4Net.Toolkit.Features.Issue;

/// <summary>
/// One way an issue can end, declared by the team's workflow — <c>Delivered</c>, <c>Won't do</c>,
/// <c>No action</c>, and whatever else a team needs. The terminal state's sub-states, in effect.
/// </summary>
/// <remarks>
/// <para>
/// A workflow has one exit rather than one per outcome: <c>Todo → Doing → Done</c>, with the
/// <i>reason</i> held on the issue. That keeps "is this finished" a single question — a set of
/// terminal states has to be enumerated everywhere it is asked, and every place that forgets one
/// gives a silently wrong answer. Outcomes also multiply where states should not: adding
/// <c>Duplicate</c> as a resolution is one row, adding it as a state is a node plus a transition
/// from everything that can reach it.
/// </para>
/// <para>
/// The names are the team's own, for the same reason state names are. What is <b>not</b> the team's
/// own is whether an ending counts as a delivery, and that is what <see cref="IsSuccess"/> carries:
/// a roadmap has to draw an abandoned issue differently from a shipped one, and it cannot do that by
/// recognising particular words without rendering every team with a different vocabulary as one flat
/// colour. So the flag carries the meaning and the name carries the language.
/// </para>
/// <para>
/// Declared on the workflow rather than as a closed enum because an importer mirroring an external
/// tracker needs somewhere to put that tracker's term — GitHub's <c>not_planned</c>, Jira's
/// <c>Won't Fix</c>, a Trello list — and a closed set would force a Toolkit release for every new
/// source. Same reasoning as <see cref="IssueReferenceResponse.Kind"/>.
/// </para>
/// </remarks>
public record IssueWorkflowResolutionResponse
{
    /// <summary>
    /// The resolution's name, unique within the workflow, and what an issue stores.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>Display order, ascending.</summary>
    public required int Order { get; init; }

    /// <summary>
    /// Whether ending here counts as having delivered the work. <c>false</c> is the case this type
    /// exists for: an issue that was decided against is finished, but it did not ship, and a map
    /// that draws the two alike is telling a reader something untrue.
    /// </summary>
    public required bool IsSuccess { get; init; }
}
