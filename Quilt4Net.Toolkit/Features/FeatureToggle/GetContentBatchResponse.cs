namespace Quilt4Net.Toolkit.Features.FeatureToggle;

/// <summary>
/// The answer to a <see cref="GetContentBatchRequest"/>.
/// </summary>
/// <remarks>
/// Matched to the request by position: <c>Items[i]</c> answers <c>request.Items[i]</c>, and there are
/// always as many. Position rather than key, because one batch may hold the same key for two
/// languages or applications.
/// </remarks>
public record GetContentBatchResponse
{
    public required GetContentResponse[] Items { get; init; }
}
