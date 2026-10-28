namespace Quilt4Net.Toolkit.Framework;

/// <summary>
/// Response from the WhoAmI endpoint containing team info and API key capabilities.
/// </summary>
public record WhoAmIResponse
{
    public string TeamKey { get; init; }
    public string TeamName { get; init; }
    public string AccessLevel { get; init; }
    public string[] Scopes { get; init; } = [];

    /// <summary>
    /// The team's areas that are on, such as <c>Content</c> or <c>Monitoring</c> (#144). Null from a
    /// server that does not say, which is not the same as none.
    /// </summary>
    public string[] Areas { get; init; }

    public bool HasScope(string scope) => Scopes.Contains(scope);

    /// <summary>Whether the team has the area on; null when the server did not say.</summary>
    public bool? IsAreaOn(string area) => Areas?.Contains(area, StringComparer.OrdinalIgnoreCase);
}
