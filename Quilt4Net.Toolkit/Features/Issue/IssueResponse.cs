namespace Quilt4Net.Toolkit.Features.Issue;

/// <summary>
/// One issue on a team's tracker.
/// </summary>
public record IssueResponse
{
    /// <summary>
    /// Per-team sequential number, and the issue's stable human reference (<c>#12</c>). Assigned by
    /// the server on creation and never reused, including after a delete.
    /// </summary>
    public required int Number { get; init; }

    /// <summary>Short one-line summary.</summary>
    public required string Title { get; init; }

    /// <summary>Body text. May be empty.</summary>
    public required string Content { get; init; }

    /// <summary>
    /// How the issue will be delivered, as markdown. Empty when no plan has been written.
    /// </summary>
    /// <remarks>
    /// Not <c>required</c>, for the reason spelled out on <see cref="References"/>: this ships in the
    /// Toolkit before the Server populates it, and empty reads as "no plan", which is true of every
    /// issue an older server returns.
    /// </remarks>
    public string Plan { get; init; } = string.Empty;

    /// <summary>
    /// The named route this issue belongs to — the roadmap's lane. Empty when the issue belongs to
    /// no route, in which case it stays off the map.
    /// </summary>
    public required string Route { get; init; }

    /// <summary>Where the issue sits on the roadmap's order axis.</summary>
    public required RoadmapBand Band { get; init; }

    /// <summary>
    /// Current state, naming one of the states in the team's workflow. Changed through the
    /// state endpoint rather than a general update, so the transition can be validated.
    /// </summary>
    public required string State { get; init; }

    /// <summary>
    /// Why the issue ended, naming one of the workflow's resolutions. Empty unless
    /// <see cref="State"/> is terminal.
    /// </summary>
    /// <remarks>
    /// Not <c>required</c>, for the reason spelled out on <see cref="References"/>: this ships in the
    /// Toolkit before the Server populates it. Empty against a newer server means the issue is open;
    /// empty against an older one means the server cannot say. Both read as "no decision recorded",
    /// which is the safe interpretation of each.
    /// </remarks>
    public string Resolution { get; init; } = string.Empty;

    /// <summary>
    /// Which phase the issue is in within <see cref="State"/>, naming one of that state's
    /// <see cref="IssueWorkflowStateResponse.SubStates"/>, or empty when none is set.
    /// </summary>
    /// <remarks>
    /// Not <c>required</c>, for the reason spelled out on <see cref="References"/>. Always empty on a
    /// terminal state, where <see cref="Resolution"/> says how the issue ended.
    /// </remarks>
    public string SubState { get; init; } = string.Empty;

    /// <summary>
    /// When the issue entered <see cref="SubState"/> (UTC), or <c>null</c> when it holds none — so a
    /// card can say "Testing for 3 days".
    /// </summary>
    public DateTime? SubStateSinceUtc { get; init; }

    /// <summary>Key of the assigned team member, or empty when unassigned.</summary>
    public required string AssignedUserKey { get; init; }

    /// <summary>Rough size, or <c>null</c> when unsized.</summary>
    public required IssueEffort? Effort { get; init; }

    /// <summary>
    /// How much this matters, or <c>null</c> when nobody has graded it yet. Paired with
    /// <see cref="Effort"/> for the importance-then-effort ordering.
    /// </summary>
    public required IssueImportance? Importance { get; init; }

    /// <summary>
    /// Dependencies declared <b>from</b> this issue. An issue does not carry its inbound links;
    /// read the whole set, or the roadmap projection, to see those.
    /// </summary>
    public required IssueLinkResponse[] Links { get; init; }

    /// <summary>
    /// Where this issue came from, and the tickets it is. Empty when nothing was recorded.
    /// </summary>
    /// <remarks>
    /// Deliberately <b>not</b> <c>required</c>, unlike every other property here. The Toolkit
    /// publishes before the Server that populates it — the csproj comment in <c>Quilt4Net.Server</c>
    /// spells out why that order cannot be reversed — so for the length of that window a client on
    /// this version talks to a server that sends no <c>references</c> at all. A required member would
    /// turn that window into a deserialization failure on every read; defaulting to empty makes it a
    /// non-event.
    /// </remarks>
    public IssueReferenceResponse[] References { get; init; } = [];

    /// <summary>
    /// Who says they are working on this, and from which machine; <c>null</c> when nobody holds it.
    /// </summary>
    /// <remarks>
    /// Not <c>required</c>, for the reason given on <see cref="References"/>. <c>null</c> against an
    /// older server means it cannot say, which reads the same as unclaimed — the safe interpretation,
    /// since claiming an issue somebody else holds only ever warns.
    /// </remarks>
    public IssueClaimResponse Claim { get; init; }

    /// <summary>
    /// Set when the issue is flagged as waiting (#131), with what it waits for; <c>null</c> when it is
    /// not. Not <c>required</c>, for the reason given on <see cref="References"/>.
    /// </summary>
    public IssueWaitingResponse Waiting { get; init; }

    /// <summary>When the issue was created (UTC).</summary>
    public required DateTime CreatedUtc { get; init; }

    /// <summary>When the issue was last changed (UTC).</summary>
    public required DateTime UpdatedUtc { get; init; }
}
