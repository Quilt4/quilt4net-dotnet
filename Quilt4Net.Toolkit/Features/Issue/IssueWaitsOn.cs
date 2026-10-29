using System.Text.Json.Serialization;

namespace Quilt4Net.Toolkit.Features.Issue;

/// <summary>
/// What a waiting issue (#131) is held up by, so open work can be read by who has to move next
/// without opening every issue.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IssueWaitsOn
{
    /// <summary>A person: the owner, a reviewer, a customer.</summary>
    Person,

    /// <summary>A date, given in <see cref="IssueWaitingResponse.Until"/>.</summary>
    Date,

    /// <summary>
    /// Another issue that is not modelled as a <see cref="IssueLinkKind.Blocks"/> link. An open
    /// incoming <c>Blocks</c> link already says the issue waits, and needs no flag.
    /// </summary>
    Issue,

    /// <summary>Something outside the team: an upstream release, a vendor, another system.</summary>
    External
}
