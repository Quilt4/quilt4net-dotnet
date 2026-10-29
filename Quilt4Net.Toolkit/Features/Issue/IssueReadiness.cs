using System.Text.Json.Serialization;

namespace Quilt4Net.Toolkit.Features.Issue;

/// <summary>
/// The condition of open work (#131), as an <see cref="IssueFilter"/> selects it. An issue can be both
/// <see cref="Blocked"/> and <see cref="Waiting"/>; it is <see cref="Ready"/> only when it is neither.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IssueReadiness
{
    /// <summary>An open issue blocks it, through a <see cref="IssueLinkKind.Blocks"/> link.</summary>
    Blocked,

    /// <summary>It carries a waiting flag (<see cref="IssueResponse.Waiting"/>).</summary>
    Waiting,

    /// <summary>Neither blocked nor waiting.</summary>
    Ready
}
