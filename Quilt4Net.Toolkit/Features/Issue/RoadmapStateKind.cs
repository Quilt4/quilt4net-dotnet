using System.Text.Json.Serialization;

namespace Quilt4Net.Toolkit.Features.Issue;

/// <summary>
/// Where an issue's state sits in the team's workflow, reduced to the three shapes a map can draw.
/// </summary>
/// <remarks>
/// The roadmap cannot colour by state name, because <b>state names are a team's own</b> — the
/// workflow is editable, so a component that special-cased "Todo" and "Doing" would render a team
/// with a different vocabulary as a wall of one colour. What is stable across every workflow is the
/// shape: something is the entry point, something is an exit, everything else is in between. That is
/// what this reduces to, on the server, where the workflow is already in hand.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RoadmapStateKind
{
    /// <summary>The workflow's initial state — nobody has picked this up.</summary>
    NotStarted,

    /// <summary>Neither initial nor terminal: somebody is on it.</summary>
    InProgress,

    /// <summary>A terminal state. Drawn in the Closed column, when the filter shows it.</summary>
    Done
}
