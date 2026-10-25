using System.Text.Json;

namespace Quilt4Net.Toolkit.Framework;

/// <summary>
/// Reads the sentence a Quilt4Net problem response carries.
/// </summary>
/// <remarks>
/// Every failure from <c>/Api</c> is <c>application/problem+json</c>, and its <c>detail</c> is the part
/// worth showing or logging: which scope is missing, or that the team has switched the feature off.
/// Shared by the issue and release-note clients so a failure reads the same from both.
/// </remarks>
internal static class ProblemResponse
{
    /// <summary>
    /// The problem's <c>detail</c>, else its <c>title</c>; null when the body is not a problem object.
    /// </summary>
    public static string DetailOf(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            foreach (var name in new[] { "detail", "title" })
            {
                if (document.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))
                    return value.GetString().Trim();
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
