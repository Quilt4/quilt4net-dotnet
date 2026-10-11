namespace Quilt4Net.Toolkit.Features.Issue;

/// <summary>
/// Replaces an issue's editable fields. Requires an API key carrying the <c>issue:write</c> scope.
/// </summary>
/// <remarks>
/// <b>This is a replace, not a merge.</b> Every field is written as supplied, so an omitted optional
/// field is <i>cleared</i> rather than left alone — read the issue, change what you mean to change,
/// and send the whole thing back.
/// <para>
/// The alternative — treating <c>null</c> as "leave unchanged" — reads as friendlier but leaves no
/// way to clear a field, and makes every request ambiguous about which of the two it meant. Replace
/// semantics are what the HTTP verb already promises.
/// </para>
/// <para>
/// <see cref="IssueResponse.State"/> is deliberately absent: a state change has to be checked
/// against the team's workflow, so it goes through the state endpoint instead. The MCP
/// <c>quilt4net.issue.update</c> tool wraps this in a read-modify-write, so an agent there may send
/// only the fields it wants to change.
/// </para>
/// </remarks>
public record UpdateIssueRequest
{
    /// <summary>Short one-line summary. Required.</summary>
    public required string Title { get; init; }

    /// <summary>Body text. Cleared when omitted.</summary>
    public string Content { get; init; }

    /// <summary>
    /// The plan, as markdown. <b>The one exception to replace semantics:</b> <c>null</c> leaves the
    /// stored plan as it is, and an empty string clears it.
    /// </summary>
    /// <remarks>
    /// The plan was added after this contract shipped, so a client built before it cannot send one.
    /// Under replace semantics every edit from such a client would wipe the plan without it ever
    /// knowing the field existed. "Absent means unchanged" is the only reading that is safe for a
    /// field older clients do not know about, and the empty string still gives a way to clear it.
    /// </remarks>
    public string Plan { get; init; }

    /// <summary>Route (roadmap lane). Cleared when omitted, which takes the issue off the map.</summary>
    public string Route { get; init; }

    /// <summary>Order-axis band. Defaults to <see cref="RoadmapBand.Later"/>.</summary>
    public RoadmapBand Band { get; init; } = RoadmapBand.Later;

    /// <summary>Assigned team member key. Cleared when omitted, which unassigns the issue.</summary>
    public string AssignedUserKey { get; init; }

    /// <summary>Rough size. Cleared when omitted.</summary>
    public IssueEffort? Effort { get; init; }

    /// <summary>How much this matters. Cleared when omitted.</summary>
    public IssueImportance? Importance { get; init; }

    /// <summary>
    /// Where this issue came from, and the tickets it is. <b>Cleared when omitted</b>, like every
    /// other field here — read the issue, change what you mean to change, and send the whole
    /// collection back.
    /// </summary>
    public IssueReferenceRequest[] References { get; init; } = [];
}
