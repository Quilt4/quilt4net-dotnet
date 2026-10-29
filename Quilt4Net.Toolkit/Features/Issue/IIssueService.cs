namespace Quilt4Net.Toolkit.Features.Issue;

/// <summary>
/// Reads and writes a team's issues on the Quilt4Net server.
/// </summary>
/// <remarks>
/// Every call is scoped to the team owning the configured API key — there is no team argument,
/// because a team key cannot reach another team's issues. Reads need <c>issue:read</c>; writes need
/// <c>issue:write</c>, and replacing the workflow needs <c>issue:manage</c>. Both come with an
/// Administrator key, or with the Issue Manager role.
/// <para>
/// Unlike <see cref="FeatureToggle.IRemoteConfigurationService"/>, these calls do not cache and do
/// not fall back to a default on failure: a tracker read that silently returned stale or empty data
/// would be indistinguishable from a team with no issues.
/// </para>
/// </remarks>
public interface IIssueService
{
    /// <summary>Lists every issue on the team.</summary>
    Task<IssueResponse[]> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads one issue by its per-team number, or <c>null</c> when no such issue exists.</summary>
    /// <param name="number">The issue number.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IssueResponse> GetAsync(int number, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the roadmap projection — lanes, bands and edges, already laid out for drawing.
    /// </summary>
    Task<RoadmapResponse> GetRoadmapAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads the team's workflow.</summary>
    Task<IssueWorkflowResponse> GetWorkflowAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates an issue and returns it, including its assigned number.</summary>
    /// <param name="request">The issue to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IssueResponse> CreateAsync(CreateIssueRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces an issue's editable fields. See <see cref="UpdateIssueRequest"/> — this is a
    /// replace, so send the whole issue back rather than only what changed.
    /// </summary>
    /// <param name="number">The issue to update.</param>
    /// <param name="request">The replacement values.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IssueResponse> UpdateAsync(int number, UpdateIssueRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves an issue to another state, if the team's workflow permits the transition.
    /// </summary>
    /// <param name="number">The issue to move.</param>
    /// <param name="request">The state to move to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IssueResponse> SetStateAsync(int number, SetIssueStateRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Flags an issue as waiting (#131), or changes its flag. The issue keeps its state and claim.
    /// </summary>
    /// <param name="number">The issue to flag.</param>
    /// <param name="request">What it waits for, on what, and until when.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IssueResponse> SetWaitingAsync(int number, SetIssueWaitingRequest request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException($"{GetType().Name} does not implement {nameof(SetWaitingAsync)}.");

    /// <summary>Clears an issue's waiting flag. Clearing an issue that has none changes nothing.</summary>
    /// <param name="number">The issue to clear.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IssueResponse> ClearWaitingAsync(int number, CancellationToken cancellationToken = default)
        => throw new NotSupportedException($"{GetType().Name} does not implement {nameof(ClearWaitingAsync)}.");

    /// <summary>Declares a dependency from one issue to another.</summary>
    /// <param name="number">The issue the link leaves.</param>
    /// <param name="request">What the link points at, what it asserts, and why.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IssueResponse> AddLinkAsync(int number, AddIssueLinkRequest request, CancellationToken cancellationToken = default);

    /// <summary>Removes a dependency.</summary>
    /// <param name="number">The issue the link leaves.</param>
    /// <param name="targetNumber">The issue the link points at.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IssueResponse> RemoveLinkAsync(int number, int targetNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an issue, and every link pointing at it. The number is not reused.
    /// </summary>
    /// <param name="number">The issue to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteAsync(int number, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the team's workflow. Rejected when the replacement would orphan issues that are in a
    /// state it no longer defines.
    /// </summary>
    /// <param name="request">The workflow to install.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IssueWorkflowResponse> SetWorkflowAsync(SetIssueWorkflowRequest request, CancellationToken cancellationToken = default);
}
