namespace Quilt4Net.Toolkit.Features.FeatureToggle;

/// <summary>
/// Many single-key content requests sent as one call to <c>POST Api/Content/batch</c>.
/// </summary>
/// <remarks>
/// Each item is exactly what <c>GET Api/Content/{complexKey}</c> takes, so the server resolves - and,
/// for a key it has never seen, stores - every item the same way. The point is the number of calls: a
/// page that declares many new keys used to send one request per key, all at once, and each one did
/// its own reads and writes against the database.
/// </remarks>
public record GetContentBatchRequest
{
    public required GetContentRequest[] Items { get; init; }
}
