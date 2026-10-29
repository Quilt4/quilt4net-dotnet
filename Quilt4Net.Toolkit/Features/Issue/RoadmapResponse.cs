namespace Quilt4Net.Toolkit.Features.Issue;

/// <summary>
/// The roadmap, already laid out. This is the shape the view component draws.
/// </summary>
/// <remarks>
/// The projection is built on the server so the component does not have to re-derive lanes and bands
/// from a flat issue list — two consumers deriving the same layout is two places for it to differ.
/// </remarks>
public record RoadmapResponse
{
    /// <summary>The lanes, in display order. Each is one route.</summary>
    public required RoadmapRouteResponse[] Routes { get; init; }

    /// <summary>
    /// Every edge worth drawing, already filtered to links whose endpoints both appear in
    /// <see cref="Routes"/>.
    /// </summary>
    public required RoadmapEdgeResponse[] Edges { get; init; }

    /// <summary>
    /// Issues that carry no route at all, and so belong in no lane.
    /// </summary>
    public required int UnroutedCount { get; init; }

    /// <summary>
    /// Issues that <b>do</b> carry a route but are still not drawn — their route fell outside the
    /// lane cap.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Always 0 from a current server, which draws every route. Kept, and still reported by the
    /// component, for servers that predate that and capped the map at six lanes.
    /// </para>
    /// Counted separately from <see cref="UnroutedCount"/> because the two have different fixes and
    /// conflating them produces a false statement. The first version reported both as "carry no
    /// route", which was wrong the first time a real import exceeded the lane cap: three parked
    /// issues with a perfectly good route were described as having none.
    /// </remarks>
    public required int HiddenCount { get; init; }

    /// <summary>When this projection was built (UTC).</summary>
    public required DateTime GeneratedUtc { get; init; }
}

/// <summary>One lane of the roadmap.</summary>
public record RoadmapRouteResponse
{
    /// <summary>Route name, as carried by the issues in it.</summary>
    public required string Name { get; init; }

    /// <summary>Items in the <see cref="RoadmapBand.Now"/> band.</summary>
    public required RoadmapItemResponse[] Now { get; init; }

    /// <summary>Items in the <see cref="RoadmapBand.Next"/> band.</summary>
    public required RoadmapItemResponse[] Next { get; init; }

    /// <summary>Items in the <see cref="RoadmapBand.Later"/> band.</summary>
    public required RoadmapItemResponse[] Later { get; init; }

    /// <summary>
    /// Items in a terminal state, whatever band they last held. They have moved past Now, Next and
    /// Later, so they are drawn in a column of their own rather than in the band they left.
    /// </summary>
    /// <remarks>
    /// Not <c>required</c>: a server that predates this field leaves it empty and puts terminal
    /// items in their band instead, marked <see cref="RoadmapItemResponse.IsTerminal"/>. The view
    /// component moves those here itself, so both servers draw the same map.
    /// </remarks>
    public RoadmapItemResponse[] Closed { get; init; } = [];
}

/// <summary>One issue as it appears on the map.</summary>
public record RoadmapItemResponse
{
    /// <summary>The issue's per-team number.</summary>
    public required int Number { get; init; }

    /// <summary>Short one-line summary.</summary>
    public required string Title { get; init; }

    /// <summary>Current workflow state, by its team-defined name.</summary>
    public required string State { get; init; }

    /// <summary>
    /// Where <see cref="State"/> sits in the workflow, for a map that cannot know a team's state
    /// names. See <see cref="RoadmapStateKind"/>.
    /// </summary>
    /// <remarks>
    /// Not <c>required</c>: against a server that predates this field every item reads
    /// <see cref="RoadmapStateKind.NotStarted"/>, which asserts no progress rather than inventing
    /// any, and <see cref="IsTerminal"/> still fades finished items correctly.
    /// </remarks>
    public RoadmapStateKind StateKind { get; init; } = RoadmapStateKind.NotStarted;

    /// <summary>
    /// Why the issue ended, by its team-defined name, or empty when it has not ended.
    /// </summary>
    public string Resolution { get; init; } = string.Empty;

    /// <summary>
    /// Whether <see cref="Resolution"/> counts as having delivered the work, or <c>null</c> when
    /// there is no resolution to judge.
    /// </summary>
    /// <remarks>
    /// Three-valued on purpose, and the distinction is the whole point of the field. <c>null</c>
    /// covers both "still open" and "a server that predates resolutions", and a renderer must treat
    /// it as it treated everything before this existed — a terminal item drawn as delivered. Only an
    /// explicit <c>false</c> licenses drawing an ending as abandoned, because inferring that from
    /// absence would repaint every finished issue on every older server.
    /// </remarks>
    public bool? ResolutionIsSuccess { get; init; }

    /// <summary>Assigned team member key, or empty when unassigned.</summary>
    public required string AssignedUserKey { get; init; }

    /// <summary>
    /// The assignee's display name, or empty when unassigned.
    /// </summary>
    /// <remarks>
    /// Resolved on the server, because the roster lives there. The component is embeddable by a
    /// remote project that holds no team membership at all, so a key is the only thing it could
    /// otherwise render — and <c>VdvTrH-RnYRCXeg1OoFB8i…</c> on a card tells a reader nothing.
    /// Falls back to the key rather than to blank when a member has since left, for the same reason
    /// the board does: an issue parked on somebody who will never see it should look wrong.
    /// </remarks>
    public string AssignedUserName { get; init; } = string.Empty;

    /// <summary>
    /// An absolute URL of the assignee's picture, or empty when unassigned, when the member has no
    /// picture, or from a server that predates the field.
    /// </summary>
    /// <remarks>
    /// Resolved on the server for the same reason as <see cref="AssignedUserName"/>: an embedding
    /// project holds no roster and no icon store. Absolute because a relative path would resolve
    /// against the embedding site, not Quilt4Net. Empty draws the assignee's initials instead.
    /// </remarks>
    public string AssignedUserIconUrl { get; init; } = string.Empty;

    /// <summary>
    /// Who says they are working on this, and from which machine; <c>null</c> when nobody holds it
    /// or the server predates claims.
    /// </summary>
    /// <remarks>
    /// Carried separately from the assignee because they answer different questions: the assignee is
    /// who the issue is <i>for</i>, the claim is which session has it open <i>right now</i>. The card
    /// draws the claim where the assignee otherwise goes, because a live claim is the more urgent of
    /// the two to see.
    /// </remarks>
    public IssueClaimResponse Claim { get; init; }

    /// <summary>The issue's waiting flag (#131), or <c>null</c> when it is not flagged.</summary>
    public IssueWaitingResponse Waiting { get; init; }

    /// <summary>
    /// The band the issue sits in, also for an item listed under <see cref="RoadmapRouteResponse.Closed"/>,
    /// so a band filter treats an open and a finished issue alike. <c>null</c> from a server that
    /// predates the field.
    /// </summary>
    public RoadmapBand? Band { get; init; }

    /// <summary>
    /// How much the issue matters, or <c>null</c> when untriaged — or when the server predates the
    /// field, in which case an importance filter shows nothing.
    /// </summary>
    public IssueImportance? Importance { get; init; }

    /// <summary>
    /// Where the issue stands in the team's one automatic order over every open issue, 1 being the
    /// one to pick up first: blockers before what they block, then band, importance and effort. The
    /// same number the issue list and the board show. <c>null</c> for a finished issue, and from a
    /// server that predates the field.
    /// </summary>
    public int? Position { get; init; }

    /// <summary>Rough size, rendered on the item as <c>· S</c>, <c>· M</c> or <c>· L</c>.</summary>
    public required IssueEffort? Effort { get; init; }

    /// <summary>
    /// Whether the issue is in a terminal state. A terminal item is drawn in the Closed column, faded,
    /// as context rather than as work.
    /// </summary>
    public required bool IsTerminal { get; init; }

    /// <summary>
    /// Whether this is a quick win — small, and with nothing pointing at it. These are what someone
    /// picks up with an hour free, and they are invisible in any list sorted by importance.
    /// </summary>
    public required bool IsQuickWin { get; init; }
}

/// <summary>One drawn dependency between two items on the map.</summary>
public record RoadmapEdgeResponse
{
    /// <summary>Number of the issue the edge leaves.</summary>
    public required int FromNumber { get; init; }

    /// <summary>Number of the issue the edge points at.</summary>
    public required int ToNumber { get; init; }

    /// <summary>Which of the three edge kinds this is, and so how it is drawn.</summary>
    public required IssueLinkKind Kind { get; init; }

    /// <summary>Why the edge exists. Always present, and always rendered.</summary>
    public required string Reason { get; init; }
}
