using FluentAssertions;
using Quilt4Net.Toolkit.Features.Issue;
using Xunit;

namespace Quilt4Net.Toolkit.Tests;

/// <summary>
/// The board, the list and the roadmap all answer "is this issue shown?" through this one model, so
/// these tests pin the rules they must agree on: empty means everything, and an unset value only
/// passes a dimension nobody narrowed.
/// </summary>
public class IssueFilterTests
{
    [Fact]
    public void An_empty_filter_shows_everything()
    {
        IssueFilter.None.IsEmpty.Should().BeTrue();
        IssueFilter.None.Matches(RoadmapBand.Later, null, null, "Closed").Should().BeTrue();
    }

    [Fact]
    public void An_untriaged_issue_is_hidden_once_an_importance_is_selected()
    {
        var filter = new IssueFilter { Importances = [IssueImportance.Critical] };

        filter.Matches(RoadmapBand.Now, IssueImportance.Critical, null, "Todo").Should().BeTrue();
        filter.Matches(RoadmapBand.Now, IssueImportance.Nice, null, "Todo").Should().BeFalse();
        filter.Matches(RoadmapBand.Now, null, null, "Todo").Should().BeFalse();
    }

    [Fact]
    public void An_unsized_issue_is_hidden_once_an_effort_is_selected()
    {
        var filter = new IssueFilter { Efforts = [IssueEffort.S] };

        filter.Matches(RoadmapBand.Now, null, IssueEffort.S, "Todo").Should().BeTrue();
        filter.Matches(RoadmapBand.Now, null, null, "Todo").Should().BeFalse();
    }

    [Fact]
    public void States_match_by_exact_name()
    {
        var filter = new IssueFilter { States = ["Doing"] };

        filter.Matches(RoadmapBand.Now, null, null, "Doing").Should().BeTrue();
        filter.Matches(RoadmapBand.Now, null, null, "doing").Should().BeFalse();
        filter.Matches(RoadmapBand.Now, null, null, "Todo").Should().BeFalse();
    }

    [Fact]
    public void An_unknown_band_passes_a_band_filter_it_cannot_judge()
    {
        var filter = new IssueFilter { Bands = [RoadmapBand.Now] };

        filter.Matches(null, null, null, "Todo").Should().BeTrue();
        filter.Matches(RoadmapBand.Later, null, null, "Todo").Should().BeFalse();
        filter.IncludesBand(RoadmapBand.Later).Should().BeFalse();
    }

    [Fact]
    public void The_open_states_are_every_non_terminal_state_in_workflow_order()
    {
        var workflow = new IssueWorkflowResponse
        {
            States =
            [
                State("Closed", 3, terminal: true),
                State("Doing", 2),
                State("Todo", 1),
                State("Rejected", 4, terminal: true)
            ],
            Transitions = []
        };

        IssueFilter.OpenStates(workflow).States.Should().Equal("Todo", "Doing");
    }

    [Fact]
    public void Without_a_workflow_the_open_states_filter_shows_everything()
    {
        IssueFilter.OpenStates(null).IsEmpty.Should().BeTrue();
    }

    private static IssueWorkflowStateResponse State(string name, int order, bool terminal = false) => new()
    {
        Name = name,
        Order = order,
        IsInitial = order == 1,
        IsTerminal = terminal
    };
}
