namespace Quilt4Net.Toolkit.Features.Issue;

/// <summary>
/// Which issues a view shows. One model, read by the board, the list and the roadmap alike, so the
/// three cannot disagree about what is being looked at.
/// </summary>
/// <remarks>
/// An empty selection means <i>everything</i> in that dimension, not nothing. An untouched filter
/// then reads as no choice rather than as a choice of every value, and "no opinion" stays
/// expressible.
/// </remarks>
public record IssueFilter
{
    /// <summary>A filter that lets every issue through.</summary>
    public static readonly IssueFilter None = new();

    /// <summary>Bands to show, or empty for all.</summary>
    public IReadOnlyCollection<RoadmapBand> Bands { get; init; } = [];

    /// <summary>
    /// Importances to show, or empty for all. An issue with no importance is excluded as soon as
    /// any importance is selected, because it has not been triaged into any of them.
    /// </summary>
    public IReadOnlyCollection<IssueImportance> Importances { get; init; } = [];

    /// <summary>Efforts to show, or empty for all. An issue with no effort is excluded as soon as any is selected.</summary>
    public IReadOnlyCollection<IssueEffort> Efforts { get; init; } = [];

    /// <summary>
    /// Workflow states to show, by their team-defined name, or empty for all. Names rather than a
    /// fixed set, because the states belong to the team's workflow.
    /// </summary>
    public IReadOnlyCollection<string> States { get; init; } = [];

    /// <summary>
    /// Which conditions of open work to show (#131): blocked, waiting, ready; empty, or all three,
    /// for everything. An issue both blocked and waiting is shown for either. Narrowed to some, the
    /// finished issues are left out, since they are not work to pick up. The caller says what an
    /// issue is, through <see cref="IncludesReadiness"/>.
    /// </summary>
    public IReadOnlyCollection<IssueReadiness> Readiness { get; init; } = [];

    /// <summary>Whether no dimension narrows anything.</summary>
    public bool IsEmpty => Bands.Count == 0 && Importances.Count == 0 && Efforts.Count == 0 && States.Count == 0 && Readiness.Count == 0;

    /// <summary>
    /// Whether a band is shown. Separate from <see cref="Matches(RoadmapBand?, IssueImportance?, IssueEffort?, string)"/> so a view can drop a whole band
    /// rather than draw it empty.
    /// </summary>
    public bool IncludesBand(RoadmapBand band) => Bands.Count == 0 || Bands.Contains(band);

    /// <summary>Whether a workflow state is shown.</summary>
    public bool IncludesState(string state) => States.Count == 0 || States.Contains(state, StringComparer.Ordinal);

    /// <summary>Whether an issue in this condition is shown.</summary>
    /// <param name="open">The issue is not in a terminal state.</param>
    /// <param name="blocked">An open issue blocks it.</param>
    /// <param name="waiting">It carries a waiting flag.</param>
    public bool IncludesReadiness(bool open, bool blocked, bool waiting)
    {
        if (Readiness.Count == 0 || Enum.GetValues<IssueReadiness>().All(Readiness.Contains)) return true;
        if (!open) return false;

        return (blocked && Readiness.Contains(IssueReadiness.Blocked))
               || (waiting && Readiness.Contains(IssueReadiness.Waiting))
               || (!blocked && !waiting && Readiness.Contains(IssueReadiness.Ready));
    }

    /// <summary>
    /// Whether an issue with these values is shown. A <c>null</c> band means the source could not
    /// say, which a band filter cannot judge, so it passes.
    /// </summary>
    public bool Matches(RoadmapBand? band, IssueImportance? importance, IssueEffort? effort, string state) =>
        (band == null || IncludesBand(band.Value))
        && (Importances.Count == 0 || (importance != null && Importances.Contains(importance.Value)))
        && (Efforts.Count == 0 || (effort != null && Efforts.Contains(effort.Value)))
        && IncludesState(state);

    /// <summary>
    /// <see cref="Matches(RoadmapBand?, IssueImportance?, IssueEffort?, string)"/>, and the issue's
    /// condition as <see cref="IncludesReadiness"/> takes it.
    /// </summary>
    public bool Matches(RoadmapBand? band, IssueImportance? importance, IssueEffort? effort, string state, bool open, bool blocked, bool waiting) =>
        Matches(band, importance, effort, state) && IncludesReadiness(open, blocked, waiting);

    /// <summary>
    /// The default view of a workflow: every state that is not terminal. Finished work is opt-in,
    /// because it outnumbers open work on any tracker that has been used for a while.
    /// </summary>
    public static IssueFilter OpenStates(IssueWorkflowResponse workflow) => new()
    {
        States = (workflow?.States ?? [])
            .Where(x => !x.IsTerminal)
            .OrderBy(x => x.Order)
            .Select(x => x.Name)
            .ToArray()
    };
}
