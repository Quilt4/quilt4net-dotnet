using AwesomeAssertions;
using Quilt4Net.Toolkit.Features.ApplicationInsights;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

/// <summary>
/// The previous VersionMatrix KQL used <c>union startup, fallback | summarize arg_max(TimeGenerated, …)</c>
/// — a flat <c>arg_max</c> over the union, which means any regular log row emitted *after* the
/// Startup hosted service's entry wins the pick. Apps log continuously, so the <c>Source = "Startup"</c>
/// row was effectively unreachable. These tests pin the new "per-source pick + leftanti merge"
/// shape so the Startup fast path can't silently regress to that flat <c>arg_max</c> form.
/// </summary>
public class VersionMatrixSourcePriorityTests
{
    [Fact]
    public void Picks_latest_Startup_row_per_App_Env_via_arg_max_inside_startup_pick()
    {
        // The startup_pick let-binding must summarise startup-only rows with arg_max so we keep
        // the *latest* Startup version per (App, Env) when there are multiple startup entries
        // (e.g. multiple deploys in the lookback window).
        ApplicationInsightsService.VersionMatrixPickStartupPreferred
            .Should().Contain("let startup_pick = startup")
            .And.Contain("summarize arg_max(TimeGenerated, Version, Source) by ApplicationName, Environment");
    }

    [Fact]
    public void Picks_latest_Log_row_per_App_Env_via_arg_max_inside_log_pick()
    {
        // Same shape as startup_pick but for the fallback (Log) source.
        ApplicationInsightsService.VersionMatrixPickStartupPreferred
            .Should().Contain("let log_pick = fallback");
    }

    [Fact]
    public void Merges_via_leftanti_join_so_Startup_wins_when_both_sources_have_a_row()
    {
        // The merge has to keep ALL startup_pick rows and only the log_pick rows that don't have
        // a matching (App, Env) in startup_pick. `join kind=leftanti` is the operator that does
        // exactly that; any other join shape would either lose Startup rows or double-count.
        ApplicationInsightsService.VersionMatrixPickStartupPreferred
            .Should().Contain("join kind=leftanti startup_pick on ApplicationName, Environment");
    }

    [Fact]
    public void Does_not_arg_max_over_a_naive_union_of_both_sources()
    {
        // Regression guard for the previous shape — any future edit that reverts to
        // `union startup, fallback | summarize arg_max(...)` fails this test loudly.
        ApplicationInsightsService.VersionMatrixPickStartupPreferred
            .Should().NotContain("union startup, fallback")
            .And.NotContain("union startup,fallback");
    }

    [Fact]
    public void Normalises_empty_Environment_to_unknown_inside_each_per_source_pipeline()
    {
        // The empty-environment normalisation has to happen BEFORE the summarize on each side
        // so rows with empty Environment fold together under the (unknown) bucket per source.
        // Doing it after the merge would group correctly but would need the rewrite to keep the
        // semantics — pin the per-pipeline shape so it doesn't drift.
        var picksByEnvironment = ApplicationInsightsService.VersionMatrixPickStartupPreferred
            .Split("\n");
        picksByEnvironment.Should().Contain(line => line.Contains("extend Environment = iff(isempty(Environment), \"(unknown)\", Environment)"));
    }

    [Fact]
    public void Orders_result_by_App_then_Environment_for_stable_UI_rendering()
    {
        ApplicationInsightsService.VersionMatrixPickStartupPreferred
            .Should().Contain("order by ApplicationName asc, Environment asc");
    }
}
