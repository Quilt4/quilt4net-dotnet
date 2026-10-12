using System.Globalization;
using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Quilt4Net.Toolkit.Blazor.Features.Issue;
using Quilt4Net.Toolkit.Features.Issue;
using Quilt4Net.Toolkit.Framework;
using Xunit;

namespace Quilt4Net.Toolkit.Blazor.Tests;

/// <summary>
/// The roadmap has to be a drawing, not a list with arrows described underneath it — so these tests
/// assert the figure: that lanes stack, that items land in the band they belong to, and that each
/// edge kind is drawn differently and carries its reason.
/// </summary>
public class IssueRoadmapTests : BunitContext
{
    private readonly StubIssueService _issueService = new();

    public IssueRoadmapTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IConnectionService>(new AlwaysConnectedService());
        Services.AddSingleton<IIssueService>(_issueService);
    }

    [Fact]
    public void Lanes_stack_downwards_one_per_route()
    {
        var figure = RoadmapLayout.Build(Roadmap(
            Route("content", now: [Item(1)]),
            Route("auth", now: [Item(2)])));

        figure.Lanes.Should().HaveCount(2);
        figure.Lanes[1].Top.Should().BeGreaterThan(figure.Lanes[0].Top);
    }

    [Fact]
    public void A_lane_grows_with_its_deepest_band()
    {
        var shallow = RoadmapLayout.Build(Roadmap(Route("r", now: [Item(1)])));
        var deep = RoadmapLayout.Build(Roadmap(Route("r", now: [Item(1), Item(2), Item(3)])));

        deep.Lanes[0].Height.Should().BeGreaterThan(shallow.Lanes[0].Height);
    }

    [Fact]
    public void Bands_place_items_left_to_right()
    {
        var figure = RoadmapLayout.Build(Roadmap(
            Route("r", now: [Item(1)], next: [Item(2)], later: [Item(3)])));

        var byNumber = figure.Items.ToDictionary(x => x.Item.Number);
        byNumber[1].X.Should().BeLessThan(byNumber[2].X);
        byNumber[2].X.Should().BeLessThan(byNumber[3].X);
    }

    [Fact]
    public void An_edge_joins_the_two_boxes_it_names()
    {
        var figure = RoadmapLayout.Build(Roadmap(
            new[] { Edge(1, 2, IssueLinkKind.Blocks, "schema first") },
            Route("r", now: [Item(1)], next: [Item(2)])));

        figure.Edges.Should().HaveCount(1);
        figure.Edges[0].From.Item.Number.Should().Be(1);
        figure.Edges[0].To.Item.Number.Should().Be(2);
    }

    [Fact]
    public void An_edge_pointing_at_an_unplaced_issue_is_dropped()
    {
        // The endpoint carries no route, so it is in no lane. Drawing a line to nowhere is worse
        // than not drawing it.
        var figure = RoadmapLayout.Build(Roadmap(
            new[] { Edge(1, 99, IssueLinkKind.Blocks, "points off the map") },
            Route("r", now: [Item(1)])));

        figure.Edges.Should().BeEmpty();
    }

    [Fact]
    public void Edge_paths_are_written_in_invariant_culture()
    {
        // On a machine whose culture uses a comma decimal separator, "M 12,5 C …" is not a valid
        // SVG path and the whole figure silently fails to draw.
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
            var figure = RoadmapLayout.Build(Roadmap(
                new[] { Edge(1, 2, IssueLinkKind.Cheapens, "cheaper after") },
                Route("r", now: [Item(1)], next: [Item(2)])));

            var path = figure.Edges[0].Path();

            // The bow is a fraction of the span, so this path genuinely has non-integer coordinates —
            // without which the assertion below would hold no matter how the string was built.
            path.Should().MatchRegex(@"\d\.\d", "the sample must contain a decimal, or this test proves nothing");

            // Coordinate pairs are separated by ", ", so a comma followed by a digit can only be a
            // decimal comma. Checking the prefix alone passes even when the later values are wrong.
            path.Should().NotMatchRegex(@",\d");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void The_figure_renders_one_svg_with_a_box_per_item()
    {
        _issueService.Roadmap = Roadmap(Route("content", now: [Item(1)], later: [Item(2)]));

        var cut = Render<IssueRoadmap>();

        cut.FindAll("svg").Should().NotBeEmpty();
        cut.Markup.Should().Contain("#1").And.Contain("#2");
    }

    [Fact]
    public void Each_edge_kind_is_drawn_differently()
    {
        _issueService.Roadmap = Roadmap(
            new[]
            {
                Edge(1, 2, IssueLinkKind.Blocks, "hard"),
                Edge(1, 3, IssueLinkKind.Cheapens, "soft"),
                Edge(2, 3, IssueLinkKind.Overlaps, "same surface")
            },
            Route("r", now: [Item(1)], next: [Item(2)], later: [Item(3)]));

        var cut = Render<IssueRoadmap>();

        var dashes = cut.FindAll("path[stroke-dasharray]")
            .Select(x => x.GetAttribute("stroke-dasharray"))
            .Distinct()
            .ToArray();

        dashes.Should().HaveCountGreaterThanOrEqualTo(3, "each of the three edge kinds must be visually distinct");
    }

    [Fact]
    public void An_edge_states_its_reason_on_the_map()
    {
        _issueService.Roadmap = Roadmap(
            new[] { Edge(1, 2, IssueLinkKind.Blocks, "the schema has to settle first") },
            Route("r", now: [Item(1)], next: [Item(2)]));

        var cut = Render<IssueRoadmap>();

        cut.Markup.Should().Contain("the schema has to settle first");
    }

    [Fact]
    public void An_empty_roadmap_says_so_rather_than_drawing_nothing()
    {
        _issueService.Roadmap = Roadmap();

        var cut = Render<IssueRoadmap>();

        cut.Markup.Should().Contain("Nothing to map yet");
    }

    [Fact]
    public void Unrouted_issues_are_named_rather_than_silently_omitted()
    {
        _issueService.Roadmap = Roadmap() with { UnroutedCount = 4 };

        var cut = Render<IssueRoadmap>();

        cut.Markup.Should().Contain("4 issue(s) carry no route");
    }

    [Fact]
    public void A_failed_load_reports_instead_of_spinning_forever()
    {
        _issueService.Failure = new IssueServiceException("Missing required scope 'issue:read'.", 403);

        var cut = Render<IssueRoadmap>();

        cut.Markup.Should().Contain("could not be loaded");
        cut.Markup.Should().Contain("issue:read");
    }

    [Fact]
    public void Clicking_a_card_reports_its_number()
    {
        _issueService.Roadmap = Roadmap(Route("r", now: [Item(7)]));
        var clicked = 0;

        var cut = Render<IssueRoadmap>(p => p.Add(x => x.OnItemSelected, n => clicked = n));
        cut.Find("button[data-issue='7']").Click();

        clicked.Should().Be(7);
    }

    [Fact]
    public void The_hit_target_covers_the_whole_card()
    {
        _issueService.Roadmap = Roadmap(Route("r", now: [Item(7)]));

        var cut = Render<IssueRoadmap>(p => p.Add(x => x.OnItemSelected, n => { }));

        // A real button rather than an SVG shape: Blazor does not attach handlers to
        // SVG-namespaced elements, so an @onclick on a <rect> draws the shape and wires nothing.
        var style = cut.Find("button[data-issue='7']").GetAttribute("style")!;
        style.Should().Contain($"width: {RoadmapLayout.ItemWidth.ToString(CultureInfo.InvariantCulture)}px");
        style.Should().Contain($"height: {RoadmapLayout.ItemHeight.ToString(CultureInfo.InvariantCulture)}px");
    }

    [Fact]
    public void Cards_are_not_offered_as_controls_when_the_host_handles_no_click()
    {
        _issueService.Roadmap = Roadmap(Route("r", now: [Item(7)]));

        var cut = Render<IssueRoadmap>();

        cut.FindAll("button[data-issue]").Should().BeEmpty(
            "a control over something that does nothing is a worse answer than a plain figure");
    }

    [Fact]
    public void The_assignee_is_drawn_by_name()
    {
        _issueService.Roadmap = Roadmap(Route("r", now: [Item(1, assignedUserKey: "u1", assignedUserName: "Daniel")]));

        var cut = Render<IssueRoadmap>();

        // Read the assignee node rather than the whole document. `NotContain("u1")` over cut.Markup
        // passed locally and failed on CI, because Radzen stamps a random 10-character id on every
        // RadzenText it renders and one of them came out as "5u1OjIFAi0" — a two-character needle
        // hits one of those roughly once in forty renders. Asserting the node's own text is both
        // stable and stricter: it says the key is not shown *here*, which is the actual claim.
        cut.Find("[data-assignee]").TextContent.Should().Be("Daniel");
    }

    [Fact]
    public void A_host_roster_overrides_the_projections_name()
    {
        _issueService.Roadmap = Roadmap(Route("r", now: [Item(1, assignedUserKey: "u1", assignedUserName: "stale")]));

        var cut = Render<IssueRoadmap>(p => p.Add(x => x.MemberNames, new Dictionary<string, string> { ["u1"] = "Fresh" }));

        cut.Find("[data-assignee]").TextContent.Should().Be("Fresh");
    }

    [Fact]
    public void An_assignee_the_server_could_not_name_still_shows_the_key()
    {
        _issueService.Roadmap = Roadmap(Route("r", now: [Item(1, assignedUserKey: "gone")]));

        var cut = Render<IssueRoadmap>();

        cut.Find("[data-assignee]").TextContent.Should().Be("gone",
            "an issue parked on somebody who will never see it should look wrong, not unassigned");
    }

    [Fact]
    public void An_ending_that_did_not_deliver_is_not_drawn_as_a_delivery()
    {
        _issueService.Roadmap = Roadmap(Route("r", now:
        [
            Item(1, terminal: true, state: "Closed", kind: RoadmapStateKind.Done, resolution: "Done", resolutionIsSuccess: true),
            Item(2, terminal: true, state: "Closed", kind: RoadmapStateKind.Done, resolution: "Won't do", resolutionIsSuccess: false)
        ]));

        var cut = Render<IssueRoadmap>();

        // Both are RoadmapStateKind.Done — the kind cannot separate them, which is exactly why the
        // resolution is carried alongside it rather than as a fourth kind.
        Bar(cut, 1).GetAttribute("fill").Should().Be("var(--rz-success)");
        Bar(cut, 2).GetAttribute("fill").Should().Be("var(--rz-text-disabled-color)");
    }

    [Fact]
    public void A_terminal_item_from_a_server_that_predates_resolutions_still_reads_as_delivered()
    {
        // The upgrade window this has to survive: the component ships before the server populates
        // the field, so every closed issue arrives with ResolutionIsSuccess null. Treating null as
        // "did not deliver" would repaint the entire finished backlog grey on that server.
        _issueService.Roadmap = Roadmap(Route("r", now:
            [Item(1, terminal: true, state: "Done", kind: RoadmapStateKind.Done)]));

        var cut = Render<IssueRoadmap>();

        Bar(cut, 1).GetAttribute("fill").Should().Be("var(--rz-success)");
        cut.Find("[data-state]").TextContent.Should().Be("Done", "there is no resolution to append");
    }

    [Fact]
    public void The_card_names_the_reason_it_closed()
    {
        _issueService.Roadmap = Roadmap(Route("r", now:
            [Item(1, terminal: true, state: "Closed", kind: RoadmapStateKind.Done, resolution: "Won't do", resolutionIsSuccess: false)]));

        var cut = Render<IssueRoadmap>();

        // A colour alone says "not delivered" but never why, and "Closed" alone leaves the reader
        // guessing which kind of ending it was.
        cut.Find("[data-state]").TextContent.Should().Be("Closed · Won't do");
    }

    [Fact]
    public void The_legend_explains_a_dropped_ending_only_when_one_is_drawn()
    {
        _issueService.Roadmap = Roadmap(Route("r", now: [Item(1)]));
        Render<IssueRoadmap>().Markup.Should().NotContain("Closed without delivering",
            "explaining a colour that cannot appear on this map is clutter");

        _issueService.Roadmap = Roadmap(Route("r", now:
            [Item(2, terminal: true, state: "Closed", kind: RoadmapStateKind.Done, resolution: "Won't do", resolutionIsSuccess: false)]));
        Render<IssueRoadmap>().Markup.Should().Contain("Closed without delivering");
    }

    [Fact]
    public void A_claim_names_the_user_and_the_machine()
    {
        _issueService.Roadmap = Roadmap(Route("r", now: [Item(1, claim: Claim("danie", "NEPTUNUS"))]));

        var cut = Render<IssueRoadmap>();

        // The machine is the half that matters: the collision a claim exists to show is one person's
        // two sessions, and "danie" alone cannot tell them apart.
        var node = cut.Find("[data-claim]");
        node.TextContent.Should().Be("danie · NEPTUNUS");
        node.GetAttribute("data-stale").Should().Be("false");
    }

    [Fact]
    public void A_claim_takes_the_assignees_corner()
    {
        _issueService.Roadmap = Roadmap(Route("r", now: [Item(1, assignedUserKey: "u1", assignedUserName: "Daniel", claim: Claim("danie", "NEPTUNUS"))]));

        var cut = Render<IssueRoadmap>(p => p.Add(x => x.OnItemSelected, _ => { }));

        cut.FindAll("[data-assignee]").Should().BeEmpty("the card has one corner for a name, and the live claim wins it");
        cut.Find("[data-claim]").TextContent.Should().Be("danie · NEPTUNUS");
        cut.Find("button[data-issue='1']").GetAttribute("title").Should().Contain("Daniel", "the assignee is not lost, only moved to the tooltip");
    }

    [Fact]
    public void A_stale_claim_is_drawn_differently_from_a_live_one()
    {
        _issueService.Roadmap = Roadmap(Route("r", now:
        [
            Item(1, claim: Claim("danie", "NEPTUNUS")),
            Item(2, claim: Claim("danie", "SATURNUS", stale: true))
        ]));

        var cut = Render<IssueRoadmap>();

        var claims = cut.FindAll("[data-claim]");
        var live = claims.Single(x => x.GetAttribute("data-stale") == "false");
        var stale = claims.Single(x => x.GetAttribute("data-stale") == "true");

        // Asserted on the drawing rather than only the flag: a stale marker that renders identically
        // is the bug this guards against, since "somebody is on it" and "somebody was" must not look alike.
        stale.GetAttribute("opacity").Should().NotBe(live.GetAttribute("opacity"));
        stale.GetAttribute("fill").Should().NotBe(live.GetAttribute("fill"));
    }

    [Fact]
    public void A_claim_with_only_a_machine_does_not_draw_an_empty_separator()
    {
        _issueService.Roadmap = Roadmap(Route("r", now: [Item(1, claim: Claim("", "NEPTUNUS"))]));

        var cut = Render<IssueRoadmap>();

        cut.Find("[data-claim]").TextContent.Should().Be("NEPTUNUS");
    }

    [Fact]
    public void The_tooltip_says_when_the_claimant_was_last_seen()
    {
        // The roadmap is generated 2026-09-03 00:00; the claimant last wrote two hours before that.
        _issueService.Roadmap = Roadmap(Route("r", now: [Item(1, claim: Claim("danie", "NEPTUNUS", lastSeen: new DateTime(2026, 9, 2, 22, 0, 0, DateTimeKind.Utc)))]));

        var cut = Render<IssueRoadmap>(p => p.Add(x => x.OnItemSelected, _ => { }));

        cut.Find("button[data-issue='1']").GetAttribute("title").Should().Contain("claimed by danie · NEPTUNUS, last seen 2h ago");
    }

    [Fact]
    public void An_unclaimed_item_draws_no_claim()
    {
        // Also the shape of every item from a server that predates claims: null must read as
        // unclaimed, not fail to render.
        _issueService.Roadmap = Roadmap(Route("r", now: [Item(1)]));

        var cut = Render<IssueRoadmap>();

        cut.FindAll("[data-claim]").Should().BeEmpty();
    }

    [Fact]
    public void Closed_items_are_drawn_in_a_column_left_of_now()
    {
        var figure = RoadmapLayout.Build(Roadmap(
            Route("r", now: [Item(1)], closed: [Item(2, terminal: true, state: "Closed")])));

        figure.Columns.Should().Equal("Closed", "Now", "Next", "Later");
        var byNumber = figure.Items.ToDictionary(x => x.Item.Number);
        byNumber[2].X.Should().BeLessThan(byNumber[1].X);
    }

    [Fact]
    public void A_terminal_item_an_older_server_left_in_its_band_moves_to_the_closed_column()
    {
        var figure = RoadmapLayout.Build(Roadmap(
            Route("r", now: [Item(1)], later: [Item(2, terminal: true, state: "Closed")])));

        figure.Columns.Should().Equal("Closed", "Now", "Next", "Later");
        var byNumber = figure.Items.ToDictionary(x => x.Item.Number);
        byNumber[2].X.Should().BeLessThan(byNumber[1].X);
    }

    [Fact]
    public void There_is_no_closed_column_when_nothing_is_closed()
    {
        var figure = RoadmapLayout.Build(Roadmap(Route("r", now: [Item(1)])));

        figure.Columns.Should().Equal("Now", "Next", "Later");
    }

    [Fact]
    public void There_is_no_closed_column_when_the_filter_leaves_out_the_terminal_states()
    {
        var figure = RoadmapLayout.Build(
            Roadmap(Route("r", now: [Item(1)], closed: [Item(2, terminal: true, state: "Closed")])),
            new IssueFilter { States = ["Todo", "Doing"] });

        figure.Columns.Should().Equal("Now", "Next", "Later");
        figure.Items.Select(x => x.Item.Number).Should().Equal(1);
        figure.FilteredItemCount.Should().Be(1);
    }

    [Fact]
    public void A_band_the_filter_excludes_is_dropped_rather_than_drawn_empty()
    {
        var figure = RoadmapLayout.Build(
            Roadmap(Route("r", now: [Item(1)], later: [Item(2)])),
            new IssueFilter { Bands = [RoadmapBand.Now, RoadmapBand.Next] });

        figure.Columns.Should().Equal("Now", "Next");
        figure.Width.Should().Be(RoadmapLayout.GutterWidth + 2 * RoadmapLayout.BandWidth);
        figure.Items.Select(x => x.Item.Number).Should().Equal(1);
    }

    [Fact]
    public void The_band_filter_applies_to_a_closed_item_by_the_band_it_left()
    {
        var figure = RoadmapLayout.Build(
            Roadmap(Route("r", now: [Item(1)], closed:
            [
                Item(2, terminal: true, state: "Closed", band: RoadmapBand.Now),
                Item(3, terminal: true, state: "Closed", band: RoadmapBand.Later)
            ])),
            new IssueFilter { Bands = [RoadmapBand.Now] });

        figure.Items.Select(x => x.Item.Number).Should().BeEquivalentTo([1, 2]);
    }

    [Fact]
    public void Importance_and_effort_filters_hide_items_and_count_them()
    {
        var figure = RoadmapLayout.Build(
            Roadmap(Route("r", now:
            [
                Item(1, importance: IssueImportance.Critical, effort: IssueEffort.S),
                Item(2, importance: IssueImportance.Nice, effort: IssueEffort.S),
                Item(3, importance: IssueImportance.Critical, effort: IssueEffort.L),
                Item(4, importance: null)
            ])),
            new IssueFilter { Importances = [IssueImportance.Critical], Efforts = [IssueEffort.S] });

        figure.Items.Select(x => x.Item.Number).Should().Equal(1);
        figure.FilteredItemCount.Should().Be(3);
    }

    [Fact]
    public void A_lane_the_filter_empties_is_not_drawn()
    {
        var figure = RoadmapLayout.Build(
            Roadmap(
                Route("kept", now: [Item(1, importance: IssueImportance.Critical)]),
                Route("emptied", now: [Item(2, importance: IssueImportance.Nice)])),
            new IssueFilter { Importances = [IssueImportance.Critical] });

        figure.Lanes.Select(x => x.Name).Should().Equal("kept");
    }

    [Fact]
    public void An_edge_to_a_filtered_out_item_is_dropped_and_counted()
    {
        var figure = RoadmapLayout.Build(
            Roadmap(
                new[] { Edge(2, 1, IssueLinkKind.Blocks, "done first") },
                Route("r", now: [Item(1)], closed: [Item(2, terminal: true, state: "Closed")])),
            new IssueFilter { States = ["Todo"] });

        figure.Edges.Should().BeEmpty();
        figure.FilteredEdgeCount.Should().Be(1);
    }

    [Fact]
    public void The_component_draws_the_closed_column_heading_only_when_it_holds_something()
    {
        _issueService.Roadmap = Roadmap(Route("r", now: [Item(1)], closed: [Item(2, terminal: true, state: "Closed")]));

        var shown = Render<IssueRoadmap>();
        var hidden = Render<IssueRoadmap>(p => p.Add(x => x.Filter, new IssueFilter { States = ["Todo"] }));

        shown.FindAll("text[data-column]").Select(x => x.TextContent).Should().Equal("CLOSED", "NOW", "NEXT", "LATER");
        hidden.FindAll("text[data-column]").Select(x => x.TextContent).Should().Equal("NOW", "NEXT", "LATER");
    }

    [Fact]
    public void Changing_the_filter_redraws_without_reloading()
    {
        _issueService.Roadmap = Roadmap(Route("r", now: [Item(1)], closed: [Item(2, terminal: true, state: "Closed")]));
        var cut = Render<IssueRoadmap>();
        _issueService.Roadmap = Roadmap(Route("r", now: [Item(9)]));

        cut.Render(p => p.Add(x => x.Filter, new IssueFilter { States = ["Todo"] }));

        var texts = cut.FindAll("text").Select(x => x.TextContent).ToArray();
        texts.Should().Contain("issue 1");
        texts.Should().NotContain("issue 2");
        texts.Should().NotContain("issue 9", "a filter change re-lays out the loaded projection rather than fetching a new one");
    }

    [Fact]
    public void The_caption_says_what_the_filter_hides()
    {
        _issueService.Roadmap = Roadmap(
            new[] { Edge(2, 1, IssueLinkKind.Blocks, "done first") },
            Route("r", now: [Item(1)], closed: [Item(2, terminal: true, state: "Closed")]));

        var cut = Render<IssueRoadmap>(p => p.Add(x => x.Filter, new IssueFilter { States = ["Todo"] }));

        cut.Markup.Should().Contain("Hidden by the filter: 1 item(s), 1 edge(s).");
    }

    [Fact]
    public void A_filter_that_matches_nothing_says_so()
    {
        _issueService.Roadmap = Roadmap(Route("r", now: [Item(1)]));

        var cut = Render<IssueRoadmap>(p => p.Add(x => x.Filter, new IssueFilter { States = ["Doing"] }));

        cut.Markup.Should().Contain("Nothing matches the filter.");
    }

    private static IssueClaimResponse Claim(string user, string machine, bool stale = false, DateTime? lastSeen = null) => new()
    {
        User = user,
        Machine = machine,
        ClaimedUtc = new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc),
        LastSeenUtc = lastSeen ?? new DateTime(2026, 9, 2, 23, 0, 0, DateTimeKind.Utc),
        IsStale = stale
    };

    /// <summary>The status bar down an item's leading edge, found by the item's own hit-target row.</summary>
    private static IElement Bar(IRenderedComponent<IssueRoadmap> cut, int number) =>
        cut.FindAll("rect[rx='1.5']")[Index(cut, number)];

    /// <summary>
    /// Items are drawn in the order the projection lists them, so the nth status bar belongs to the
    /// nth item. Derived from the rendered state labels rather than assumed, so this does not
    /// silently pick the wrong bar if the draw order ever changes.
    /// </summary>
    private static int Index(IRenderedComponent<IssueRoadmap> cut, int number)
    {
        var titles = cut.FindAll("text").Select(x => x.TextContent).ToList();
        var at = titles.IndexOf($"issue {number}");
        at.Should().BeGreaterThanOrEqualTo(0, $"issue {number} should be drawn");
        return titles.Take(at).Count(x => x.StartsWith("issue ", StringComparison.Ordinal));
    }

    private static RoadmapResponse Roadmap(params RoadmapRouteResponse[] routes) => Roadmap([], routes);

    private static RoadmapResponse Roadmap(RoadmapEdgeResponse[] edges, params RoadmapRouteResponse[] routes) => new()
    {
        Routes = routes,
        Edges = edges,
        UnroutedCount = 0,
        HiddenCount = 0,
        GeneratedUtc = new DateTime(2026, 9, 3, 0, 0, 0, DateTimeKind.Utc)
    };

    private static RoadmapRouteResponse Route(string name, RoadmapItemResponse[] now = null, RoadmapItemResponse[] next = null, RoadmapItemResponse[] later = null, RoadmapItemResponse[] closed = null) => new()
    {
        Name = name,
        Now = now ?? [],
        Next = next ?? [],
        Later = later ?? [],
        Closed = closed ?? []
    };

    private static RoadmapItemResponse Item(int number, bool terminal = false, bool quickWin = false, string assignedUserKey = null, string assignedUserName = null,
        string state = "Todo", RoadmapStateKind kind = RoadmapStateKind.NotStarted, string resolution = null, bool? resolutionIsSuccess = null,
        IssueClaimResponse claim = null, RoadmapBand? band = null, IssueImportance? importance = IssueImportance.Important, IssueEffort? effort = IssueEffort.S) => new()
    {
        Band = band,
        Importance = importance,
        Claim = claim,
        Number = number,
        Title = $"issue {number}",
        State = state,
        StateKind = kind,
        Resolution = resolution ?? string.Empty,
        ResolutionIsSuccess = resolutionIsSuccess,
        AssignedUserKey = assignedUserKey ?? string.Empty,
        AssignedUserName = assignedUserName ?? string.Empty,
        Effort = effort,
        IsTerminal = terminal,
        IsQuickWin = quickWin
    };

    private static RoadmapEdgeResponse Edge(int from, int to, IssueLinkKind kind, string reason) => new()
    {
        FromNumber = from,
        ToNumber = to,
        Kind = kind,
        Reason = reason
    };

    private sealed class AlwaysConnectedService : IConnectionService
    {
        public Task<ConnectionResult> CanConnectAsync(Service service) =>
            Task.FromResult(new ConnectionResult { Success = true, Address = new Uri("https://example.com/") });
    }

    private sealed class StubIssueService : IIssueService
    {
        public RoadmapResponse Roadmap { get; set; }
        public IssueServiceException Failure { get; set; }

        public Task<RoadmapResponse> GetRoadmapAsync(CancellationToken cancellationToken = default) =>
            Failure != null ? throw Failure : Task.FromResult(Roadmap);

        public Task<IssueResponse[]> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(Array.Empty<IssueResponse>());
        public Task<IssueResponse> GetAsync(int number, CancellationToken cancellationToken = default) => Task.FromResult<IssueResponse>(null);
        public Task<IssueWorkflowResponse> GetWorkflowAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IssueResponse> CreateAsync(CreateIssueRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IssueResponse> UpdateAsync(int number, UpdateIssueRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IssueResponse> SetStateAsync(int number, SetIssueStateRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IssueResponse> AddLinkAsync(int number, AddIssueLinkRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IssueResponse> RemoveLinkAsync(int number, int targetNumber, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(int number, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IssueWorkflowResponse> SetWorkflowAsync(SetIssueWorkflowRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
