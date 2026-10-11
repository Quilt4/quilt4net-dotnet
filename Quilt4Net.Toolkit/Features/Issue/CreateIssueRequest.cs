namespace Quilt4Net.Toolkit.Features.Issue;

/// <summary>
/// Creates an issue. Requires an API key carrying the <c>issue:write</c> scope.
/// </summary>
public record CreateIssueRequest
{
    /// <summary>Short one-line summary. Required.</summary>
    public required string Title { get; init; }

    /// <summary>Body text. Optional.</summary>
    public string Content { get; init; }

    /// <summary>
    /// How the issue will be delivered, as markdown: spec, step checklist, decisions, session log.
    /// Optional. <see cref="Content"/> stays the short what and why; the plan is the long part.
    /// </summary>
    public string Plan { get; init; }

    /// <summary>
    /// The route (roadmap lane) this issue belongs to. Optional — an issue with no route is tracked
    /// but does not appear on the map.
    /// </summary>
    public string Route { get; init; }

    /// <summary>Order-axis band. Defaults to <see cref="RoadmapBand.Later"/>.</summary>
    public RoadmapBand Band { get; init; } = RoadmapBand.Later;

    /// <summary>
    /// Starting state. When <c>null</c> or empty the workflow's initial state is used, which is the
    /// normal case; supplying one that the workflow does not define is rejected.
    /// </summary>
    public string State { get; init; }

    /// <summary>
    /// Key of the team member to assign. Optional. A key that is not a member of the team is
    /// rejected.
    /// </summary>
    public string AssignedUserKey { get; init; }

    /// <summary>Rough size. Optional.</summary>
    public IssueEffort? Effort { get; init; }

    /// <summary>How much this matters. Optional — leaving it unset means it still needs triage.</summary>
    public IssueImportance? Importance { get; init; }

    /// <summary>
    /// Where this issue came from, and the tickets it is. Optional.
    /// </summary>
    /// <remarks>
    /// An importer should set an <see cref="IssueReferenceRole.Identity"/> reference here for the
    /// ticket it is importing. That is what makes a second run of the same import an update rather
    /// than a duplicate — the server refuses a second issue claiming an identity another already
    /// holds, and names the issue that holds it.
    /// </remarks>
    public IssueReferenceRequest[] References { get; init; } = [];
}
