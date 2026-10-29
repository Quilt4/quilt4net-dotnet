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
    public void Readiness_keeps_the_blocked_the_waiting_or_the_ready_cards()
    {
        var flagged = Item(1) with { Waiting = new IssueWaitingResponse { Reason = "the vendor", SetUtc = DateTime.UtcNow } };
        var roadmap = Roadmap(
            [Edge(2, 3, IssueLinkKind.Blocks, "3 builds on 2"), Edge(4, 5, IssueLinkKind.Blocks, "a finished blocker holds nothing up")],
            Route("r", now: [flagged, Item(2), Item(3), Item(5)], closed: [Item(4, terminal: true)]));

        int[] Shown(params IssueReadiness[] readiness) =>
            RoadmapLayout.Build(roadmap, new IssueFilter { Readiness = readiness }).Items.Select(x => x.Item.Number).ToArray();

        Shown(IssueReadiness.Blocked).Should().BeEquivalentTo([3]);
        Shown(IssueReadiness.Waiting).Should().BeEquivalentTo([1]);
        Shown(IssueReadiness.Ready).Should().BeEquivalentTo([2, 5], "4 is finished, and a finished blocker holds nothing up");
        Shown(IssueReadiness.Blocked, IssueReadiness.Waiting).Should().BeEquivalentTo([1, 3]);
        Shown().Should().BeEquivalentTo([1, 2, 3, 4, 5]);
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
                new[] { Edge(4, 1, IssueLinkKind.Cheapens, "cheaper after") },
                Route("r", now: [Item(1), Item(2), Item(3), Item(4)])));

            var path = figure.Edges[0].Path();

            // A link back up its own column bows out by a quarter of the drop (258 / 4 = 64.5), so
            // this path genuinely has non-integer coordinates —
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
        cut.Find("[data-assignee]").GetAttribute("data-tip").Should().Be("Assigned to Daniel");
    }

    [Fact]
    public void A_host_roster_overrides_the_projections_name()
    {
        _issueService.Roadmap = Roadmap(Route("r", now: [Item(1, assignedUserKey: "u1", assignedUserName: "stale")]));

        var cut = Render<IssueRoadmap>(p => p.Add(x => x.MemberNames, new Dictionary<string, string> { ["u1"] = "Fresh" }));

        cut.Find("[data-assignee]").GetAttribute("data-tip").Should().Be("Assigned to Fresh");
    }

    [Fact]
    public void An_assignee_the_server_could_not_name_still_shows_the_key()
    {
        _issueService.Roadmap = Roadmap(Route("r", now: [Item(1, assignedUserKey: "gone")]));

        var cut = Render<IssueRoadmap>();

        cut.Find("[data-assignee]").GetAttribute("data-tip").Should().Be("Assigned to gone",
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
    public void Picking_routes_draws_only_those_routes()
    {
        var roadmap = Roadmap(
            Route("first", now: [Item(1)]),
            Route("second", now: [Item(2)]),
            Route("third", now: [Item(3)]));

        var figure = RoadmapLayout.Build(roadmap, routeNames: ["second"]);

        figure.Lanes.Select(x => x.Name).Should().Equal("second");
        figure.Items.Select(x => x.Item.Number).Should().Equal(2);
    }

    [Fact]
    public void Picking_no_route_draws_every_route()
    {
        var roadmap = Roadmap(Route("first", now: [Item(1)]), Route("second", now: [Item(2)]));

        RoadmapLayout.Build(roadmap, routeNames: []).Lanes.Should().HaveCount(2);
    }

    [Fact]
    public void Picking_a_route_keeps_every_route_on_offer_and_its_colour()
    {
        var roadmap = Roadmap(
            Route("first", now: [Item(1)]),
            Route("second", now: [Item(2)]));

        var figure = RoadmapLayout.Build(roadmap, routeNames: ["second"]);

        // The routes are the choices a reader picks from, so they must not shrink to the ones chosen,
        // and a route keeps its colour when its neighbours are put away.
        figure.Routes.Select(x => x.Name).Should().Equal("first", "second");
        figure.Lanes.Single().Colour.Should().Be(RoadmapLayout.RoutePalette[1]);
    }

    [Fact]
    public void Picking_link_kinds_draws_only_those_links()
    {
        var roadmap = Roadmap(
            new[] { Edge(1, 2, IssueLinkKind.Blocks, "a"), Edge(2, 3, IssueLinkKind.Overlaps, "b") },
            Route("r", now: [Item(1), Item(2), Item(3)]));

        var figure = RoadmapLayout.Build(roadmap, linkKinds: [IssueLinkKind.Overlaps]);

        figure.Edges.Select(x => x.Edge.Kind).Should().Equal(IssueLinkKind.Overlaps);
    }

    [Fact]
    public void A_link_kind_left_out_does_not_count_as_a_link_for_hide_unlinked()
    {
        var roadmap = Roadmap(
            new[] { Edge(1, 2, IssueLinkKind.Blocks, "a"), Edge(2, 3, IssueLinkKind.Overlaps, "b") },
            Route("r", now: [Item(1), Item(2), Item(3)]));

        var figure = RoadmapLayout.Build(roadmap, hideUnlinked: true, linkKinds: [IssueLinkKind.Overlaps]);

        figure.Items.Select(x => x.Item.Number).Should().BeEquivalentTo([2, 3]);
    }

    [Fact]
    public void The_routes_and_link_kinds_are_offered_as_choices_not_legends()
    {
        _issueService.Roadmap = Roadmap(
            new[] { Edge(1, 2, IssueLinkKind.Blocks, "a") },
            Route("first", now: [Item(1)]),
            Route("second", now: [Item(2)]));

        var cut = Render<IssueRoadmap>();

        cut.Find("[data-route-legend]").QuerySelectorAll(".rz-button").Should().HaveCount(2);
        cut.Find("[data-kind-legend]").QuerySelectorAll(".rz-button").Should().HaveCount(3);
        cut.Markup.Should().NotContain("Not started — the workflow's entry state");
        cut.Markup.Should().NotContain("Lanes are routes");
    }

    [Fact]
    public void A_claim_names_the_user_and_the_machine()
    {
        _issueService.Roadmap = Roadmap(Route("r", now: [Item(1, claim: Claim("danie", "NEPTUNUS"))]));

        var cut = Render<IssueRoadmap>();

        // The machine is the half that matters: the collision a claim exists to show is one person's
        // two sessions, and "danie" alone cannot tell them apart.
        var node = cut.Find("[data-claim]");
        node.GetAttribute("data-tip").Should().StartWith("Claimed by danie on NEPTUNUS, last seen");
        node.GetAttribute("data-stale").Should().Be("false");
    }

    [Fact]
    public void An_assigned_and_claimed_card_shows_the_avatar_with_the_machine_in_its_tooltip()
    {
        _issueService.Roadmap = Roadmap(Route("r", now: [Item(1, assignedUserKey: "u1", assignedUserName: "Daniel", claim: Claim("danie", "NEPTUNUS"))]));

        var cut = Render<IssueRoadmap>(p => p.Add(x => x.OnItemSelected, _ => { }));

        var mark = cut.Find("[data-assignee]");
        mark.GetAttribute("data-tip").Should().StartWith("Assigned to Daniel\nClaimed by danie on NEPTUNUS, last seen");
        mark.QuerySelector("[data-claim]").Should().NotBeNull("a live claim is a dot on the avatar, so it shows without pointing at it");

        // The card's button covers the drawing, so the avatar's tooltip needs its own target on top.
        cut.Find("[data-person='1']").GetAttribute("title").Should().Be(mark.GetAttribute("data-tip"));
    }

    [Fact]
    public void The_avatar_draws_the_picture_over_the_initials()
    {
        _issueService.Roadmap = Roadmap(Route("r", now: [Item(1, assignedUserKey: "u1", assignedUserName: "Daniel Bohlin", assignedUserIconUrl: "https://example.test/u1.png")]));

        var cut = Render<IssueRoadmap>();

        // Both, because an image that fails to load draws nothing in SVG and the initials then show.
        cut.Find("[data-avatar]").GetAttribute("href").Should().Be("https://example.test/u1.png");
        cut.Find("[data-initials]").TextContent.Should().Be("DB");
    }

    [Fact]
    public void Without_a_picture_the_avatar_is_the_initials()
    {
        _issueService.Roadmap = Roadmap(Route("r", now: [Item(1, assignedUserKey: "u1", assignedUserName: "Daniel")]));

        var cut = Render<IssueRoadmap>();

        cut.FindAll("[data-avatar]").Should().BeEmpty();
        cut.Find("[data-initials]").TextContent.Should().Be("D");
    }

    [Fact]
    public void A_claim_without_an_assignee_draws_a_machine()
    {
        _issueService.Roadmap = Roadmap(Route("r", now: [Item(1, claim: Claim("danie", "NEPTUNUS"))]));

        var cut = Render<IssueRoadmap>();

        cut.FindAll("[data-assignee]").Should().BeEmpty();
        cut.Find("[data-claim]").QuerySelector("rect").Should().NotBeNull("the machine is drawn as a screen");
    }

    [Fact]
    public void The_person_mark_is_drawn_in_the_network_view_too()
    {
        _issueService.Roadmap = Roadmap(Route("r", now: [Item(1, assignedUserKey: "u1", assignedUserName: "Daniel")]));

        var cut = Render<IssueRoadmap>(p => p.Add(x => x.View, RoadmapView.Network));

        cut.FindAll("[data-assignee]").Should().HaveCount(1);
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
        stale.QuerySelector("rect").GetAttribute("stroke").Should().NotBe(live.QuerySelector("rect").GetAttribute("stroke"));
    }

    [Fact]
    public void A_claim_with_only_a_machine_does_not_draw_an_empty_separator()
    {
        _issueService.Roadmap = Roadmap(Route("r", now: [Item(1, claim: Claim("", "NEPTUNUS"))]));

        var cut = Render<IssueRoadmap>();

        cut.Find("[data-claim]").GetAttribute("data-tip").Should().StartWith("Claimed on NEPTUNUS,");
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

        figure.Columns.Should().Equal("Done", "Now", "Next", "Later");
        var byNumber = figure.Items.ToDictionary(x => x.Item.Number);
        byNumber[2].X.Should().BeLessThan(byNumber[1].X);
    }

    [Fact]
    public void A_terminal_item_an_older_server_left_in_its_band_moves_to_the_closed_column()
    {
        var figure = RoadmapLayout.Build(Roadmap(
            Route("r", now: [Item(1)], later: [Item(2, terminal: true, state: "Closed")])));

        figure.Columns.Should().Equal("Done", "Now", "Next", "Later");
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

        shown.FindAll("text[data-column]").Select(x => x.TextContent).Should().Equal("DONE", "NOW", "NEXT", "LATER");
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

    [Fact]
    public void A_route_keeps_its_colour_when_the_filter_removes_the_lane_before_it()
    {
        var roadmap = Roadmap(
            Route("first", now: [Item(1, importance: IssueImportance.Nice)]),
            Route("second", now: [Item(2)]));

        var whole = RoadmapLayout.Build(roadmap);
        var filtered = RoadmapLayout.Build(roadmap, new IssueFilter { Importances = [IssueImportance.Important] });

        filtered.Lanes.Should().ContainSingle();
        filtered.Lanes[0].Colour.Should().Be(whole.Lanes[1].Colour, "colour follows the route, never its position on the drawn map");
        filtered.Items[0].Colour.Should().Be(whole.Items.Single(x => x.Item.Number == 2).Colour);
    }

    [Fact]
    public void Every_route_gets_a_colour_and_the_palette_repeats_past_its_end()
    {
        var routes = Enumerable.Range(1, RoadmapLayout.RoutePalette.Length + 1)
            .Select(i => Route($"r{i}", now: [Item(i)]))
            .ToArray();

        var figure = RoadmapLayout.Build(Roadmap(routes));

        figure.Lanes.Take(RoadmapLayout.RoutePalette.Length).Select(x => x.Colour).Should().Equal(RoadmapLayout.RoutePalette);
        figure.Lanes.Last().Colour.Should().Be(RoadmapLayout.RoutePalette[0]);
        RoadmapLayout.RoutePalette.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Hiding_unlinked_cards_drops_them_and_counts_them_as_filtered()
    {
        var figure = RoadmapLayout.Build(Roadmap(
            new[] { Edge(1, 2, IssueLinkKind.Blocks, "first") },
            Route("r", now: [Item(1), Item(3)], next: [Item(2)])), hideUnlinked: true);

        figure.Items.Select(x => x.Item.Number).Should().BeEquivalentTo([1, 2]);
        figure.FilteredItemCount.Should().Be(1);
    }

    [Fact]
    public void A_link_to_a_filtered_out_card_does_not_keep_a_card_on_the_map()
    {
        // The reader cannot see that link either, so the card is as unlinked as one with none.
        var figure = RoadmapLayout.Build(Roadmap(
                new[] { Edge(2, 1, IssueLinkKind.Blocks, "done first") },
                Route("r", now: [Item(1)], closed: [Item(2, terminal: true, state: "Closed")])),
            new IssueFilter { States = ["Todo"] }, hideUnlinked: true);

        figure.Items.Should().BeEmpty();
    }

    [Fact]
    public void No_two_cards_overlap_in_the_network_view()
    {
        var figure = RoadmapLayout.Build(Swarm(40), view: RoadmapView.Network);

        figure.Items.Should().HaveCount(40);
        foreach (var a in figure.Items)
        {
            foreach (var b in figure.Items.Where(x => x != a))
            {
                var apart = a.Right <= b.X || b.Right <= a.X || a.Y + a.Height <= b.Y || b.Y + b.Height <= a.Y;
                apart.Should().BeTrue($"#{a.Item.Number} and #{b.Item.Number} should not overlap");
            }
        }
    }

    [Fact]
    public void The_network_view_draws_the_same_picture_every_time()
    {
        var first = RoadmapLayout.Build(Swarm(30), view: RoadmapView.Network);
        var second = RoadmapLayout.Build(Swarm(30), view: RoadmapView.Network);

        first.Items.Select(x => (x.Item.Number, x.X, x.Y)).Should().Equal(second.Items.Select(x => (x.Item.Number, x.X, x.Y)));
    }

    [Fact]
    public void Linked_cards_are_close_and_unlinked_ones_are_set_out_underneath()
    {
        var figure = RoadmapLayout.Build(Roadmap(
            new[] { Edge(1, 2, IssueLinkKind.Blocks, "a"), Edge(3, 4, IssueLinkKind.Blocks, "b") },
            Route("r", now: [Item(1), Item(2), Item(3), Item(4), Item(5), Item(6)])), view: RoadmapView.Network);

        var byNumber = figure.Items.ToDictionary(x => x.Item.Number);
        figure.UnlinkedTop.Should().NotBeNull();
        byNumber[5].Y.Should().BeGreaterThan(figure.UnlinkedTop!.Value);
        byNumber[6].Y.Should().BeGreaterThan(figure.UnlinkedTop.Value);
        new[] { 1, 2, 3, 4 }.Should().OnlyContain(n => byNumber[n].Y + byNumber[n].Height < figure.UnlinkedTop.Value);

    }

    [Fact]
    public void A_link_pulls_two_cards_closer_than_a_card_two_links_away()
    {
        var figure = RoadmapLayout.Build(Roadmap(
            new[] { Edge(1, 2, IssueLinkKind.Blocks, "a"), Edge(2, 3, IssueLinkKind.Blocks, "b"), Edge(3, 4, IssueLinkKind.Blocks, "c") },
            Route("r", now: [Item(1), Item(2), Item(3), Item(4)])), view: RoadmapView.Network);

        var byNumber = figure.Items.ToDictionary(x => x.Item.Number);
        double Distance(int a, int b) => Math.Abs(byNumber[a].CenterX - byNumber[b].CenterX) + Math.Abs(byNumber[a].CenterY - byNumber[b].CenterY);
        Distance(1, 2).Should().BeLessThan(Distance(1, 4));
    }

    [Fact]
    public void Separate_clusters_are_packed_side_by_side_rather_than_pushed_apart()
    {
        // Simulated together, two clusters with no link between them only repel each other.
        var figure = RoadmapLayout.Build(Roadmap(
            new[] { Edge(1, 2, IssueLinkKind.Blocks, "a"), Edge(3, 4, IssueLinkKind.Blocks, "b"), Edge(5, 6, IssueLinkKind.Blocks, "c") },
            Route("r", now: [Item(1), Item(2), Item(3), Item(4), Item(5), Item(6)])), view: RoadmapView.Network);

        var widest = 2 * (RoadmapNetworkLayout.ItemWidth + RoadmapNetworkLayout.ClusterGap) + 2 * RoadmapNetworkLayout.Padding;
        figure.Width.Should().BeLessThan(3 * widest, "three two-card clusters fit in about three cluster widths");
        figure.Height.Should().BeLessThan(6 * (RoadmapNetworkLayout.ItemHeight + RoadmapNetworkLayout.ClusterGap));
    }

    [Fact]
    public void Network_arrows_run_left_to_right()
    {
        var figure = RoadmapLayout.Build(Swarm(40), view: RoadmapView.Network);

        // A loop has no left-to-right reading; Swarm draws none, so every arrow must point right.
        foreach (var edge in figure.Edges.Where(x => x.Edge.Kind != IssueLinkKind.Overlaps))
        {
            edge.To.X.Should().BeGreaterThan(edge.From.Right, $"#{edge.From.Item.Number} leads to #{edge.To.Item.Number}");
        }
    }

    [Fact]
    public void A_loop_of_links_is_still_laid_out()
    {
        var figure = RoadmapLayout.Build(Roadmap(
            new[] { Edge(1, 2, IssueLinkKind.Blocks, "a"), Edge(2, 3, IssueLinkKind.Blocks, "b"), Edge(3, 1, IssueLinkKind.Blocks, "c"), Edge(3, 4, IssueLinkKind.Overlaps, "d") },
            Route("r", now: [Item(1), Item(2), Item(3), Item(4)])), view: RoadmapView.Network);

        figure.Items.Select(x => x.Item.Number).Should().BeEquivalentTo([1, 2, 3, 4]);
        figure.Edges.Should().HaveCount(4);
    }

    [Fact]
    public void A_lane_arrow_arrives_on_a_straight_run()
    {
        // Neighbouring columns, a lane apart: the case where the arrowhead sat on a steep bend.
        var figure = RoadmapLayout.Build(Roadmap(
            new[] { Edge(1, 2, IssueLinkKind.Blocks, "a") },
            Route("first", now: [Item(1)]),
            Route("second", next: [Item(2)])));

        var edge = figure.Edges.Single();
        var tail = System.Text.RegularExpressions.Regex.Match(edge.Path(), @"(-?[\d.]+) (-?[\d.]+) L (-?[\d.]+) (-?[\d.]+)$");
        tail.Success.Should().BeTrue("the curve should end in a straight segment");
        double At(int group) => double.Parse(tail.Groups[group].Value, CultureInfo.InvariantCulture);
        At(2).Should().Be(At(4), "the run is level");
        (At(3) - At(1)).Should().BeGreaterThanOrEqualTo(12, "at least as long as the arrowhead");
        At(3).Should().Be(edge.To.X);
    }

    [Fact]
    public void A_network_edge_runs_from_border_to_border()
    {
        var figure = RoadmapLayout.Build(Roadmap(
            new[] { Edge(1, 2, IssueLinkKind.Blocks, "a") },
            Route("r", now: [Item(1), Item(2)])), view: RoadmapView.Network);

        var edge = figure.Edges.Single();
        var numbers = System.Text.RegularExpressions.Regex.Matches(edge.Path(), @"-?\d+(\.\d+)?").Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture)).ToArray();
        OnBorder(edge.From, numbers[0], numbers[1]).Should().BeTrue();
        OnBorder(edge.To, numbers[2], numbers[3]).Should().BeTrue();

        static bool OnBorder(PlacedItem box, double x, double y) =>
            (Math.Abs(x - box.X) < 0.2 || Math.Abs(x - box.Right) < 0.2) && y >= box.Y - 0.2 && y <= box.Y + box.Height + 0.2
            || (Math.Abs(y - box.Y) < 0.2 || Math.Abs(y - box.Y - box.Height) < 0.2) && x >= box.X - 0.2 && x <= box.Right + 0.2;
    }

    [Fact]
    public void A_full_size_map_lays_out_quickly()
    {
        // The size that prompted the network view: about 150 cards and as many links.
        var watch = System.Diagnostics.Stopwatch.StartNew();
        RoadmapLayout.Build(Swarm(160), view: RoadmapView.Network);
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void An_edge_wears_its_source_routes_colour()
    {
        _issueService.Roadmap = Roadmap(
            new[] { Edge(1, 2, IssueLinkKind.Blocks, "a") },
            Route("first", now: [Item(1)]),
            Route("second", now: [Item(2)]));

        var cut = Render<IssueRoadmap>();

        cut.Find("path.q-edge").GetAttribute("stroke").Should().Be(RoadmapLayout.RoutePalette[0]);
        cut.FindAll("[data-route-legend] rect").Select(x => x.GetAttribute("fill")).Should().Equal(RoadmapLayout.RoutePalette[0], RoadmapLayout.RoutePalette[1]);
    }

    [Fact]
    public void Pointing_at_a_card_lights_its_edges_and_neighbours_but_a_loner_gets_no_rule()
    {
        _issueService.Roadmap = Roadmap(
            new[] { Edge(1, 2, IssueLinkKind.Blocks, "a") },
            Route("r", now: [Item(1), Item(3)], next: [Item(2)]));

        var cut = Render<IssueRoadmap>();

        var css = cut.Find("style").TextContent;
        css.Should().Contain("[data-hover=\"1\"]:hover");
        css.Should().Contain("[data-hover=\"2\"]:hover");
        css.Should().NotContain("[data-hover=\"3\"]:hover", "pointing at a card with no link must not blank the map");

        cut.Find("g[data-hover='2']").GetAttribute("class").Split(' ').Should().Contain(["q-n2", "q-n1", "q-linked"]);
        cut.Find("path.q-edge").GetAttribute("class").Split(' ').Should().Contain(["q-n1", "q-n2"]);
    }

    [Fact]
    public void The_view_switch_redraws_as_a_network()
    {
        _issueService.Roadmap = Roadmap(
            new[] { Edge(1, 2, IssueLinkKind.Blocks, "a") },
            Route("r", now: [Item(1), Item(3)], next: [Item(2)]));
        RoadmapView? reported = null;

        var cut = Render<IssueRoadmap>(p => p.Add(x => x.ViewChanged, v => reported = v));
        cut.FindAll("text[data-column]").Should().NotBeEmpty();

        cut.FindAll("[data-view-controls] .rz-selectbar .rz-button")[1].Click();

        cut.Find("[data-view]").GetAttribute("data-view").Should().Be("network");
        cut.FindAll("text[data-column]").Should().BeEmpty("the network view has no band columns");
        cut.Find("text[data-unlinked]").TextContent.Should().Be("NO LINKS");
        reported.Should().Be(RoadmapView.Network);
    }

    [Fact]
    public void An_unbound_view_survives_a_parent_re_render()
    {
        _issueService.Roadmap = Roadmap(Route("r", now: [Item(1)]));
        var cut = Render<IssueRoadmap>();

        cut.FindAll("[data-view-controls] .rz-selectbar .rz-button")[1].Click();
        cut.Render(p => p.Add(x => x.Filter, IssueFilter.None));

        cut.Find("[data-view]").GetAttribute("data-view").Should().Be("network");
    }

    /// <summary>A chain-and-branch graph of the given size, spread over three routes.</summary>
    private static RoadmapResponse Swarm(int count)
    {
        var items = Enumerable.Range(1, count).Select(n => Item(n)).ToArray();
        var edges = Enumerable.Range(2, count - 1)
            .Select(n => Edge(n % 3 == 0 ? n / 3 : n - 1, n, IssueLinkKind.Blocks, "x"))
            .ToArray();
        return Roadmap(edges,
            Route("a", now: items.Where(x => x.Number % 3 == 0).ToArray()),
            Route("b", next: items.Where(x => x.Number % 3 == 1).ToArray()),
            Route("c", later: items.Where(x => x.Number % 3 == 2).ToArray()));
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

    [Fact]
    public void A_card_shows_its_place_in_the_order_when_the_server_sends_one()
    {
        _issueService.Roadmap = Roadmap(Route("r", now: [Item(1) with { Position = 7 }, Item(2)]));

        var cut = Render<IssueRoadmap>();

        cut.FindAll("[data-position]").Should().ContainSingle().Which.TextContent.Trim().Should().Be("7");
    }

    [Fact]
    public void A_waiting_issue_carries_a_pennant_with_its_reason_and_turns_red_once_due()
    {
        _issueService.Roadmap = Roadmap(Route("r", now:
        [
            Item(1) with { Waiting = new IssueWaitingResponse { Reason = "the vendor's fix", WaitsOn = IssueWaitsOn.External, SetUtc = DateTime.UtcNow } },
            Item(2) with { Waiting = new IssueWaitingResponse { Reason = "record October", WaitsOn = IssueWaitsOn.Date, Until = new DateTime(2026, 11, 2), IsDue = true, SetUtc = DateTime.UtcNow } },
            Item(3)
        ]));

        var cut = Render<IssueRoadmap>();

        var flags = cut.FindAll("[data-waiting]");
        flags.Should().HaveCount(2, "issue 3 is not waiting");
        flags[0].GetAttribute("data-tip").Should().Be("waiting on external: the vendor's fix");
        flags[0].GetAttribute("data-due").Should().Be("false");
        flags[1].GetAttribute("data-tip").Should().Be("waiting on date: record October, due since 2026-11-02");
        flags[1].GetAttribute("data-due").Should().Be("true");
    }

    private static RoadmapItemResponse Item(int number, bool terminal = false, bool quickWin = false, string assignedUserKey = null, string assignedUserName = null,
        string state = "Todo", RoadmapStateKind kind = RoadmapStateKind.NotStarted, string resolution = null, bool? resolutionIsSuccess = null,
        IssueClaimResponse claim = null, RoadmapBand? band = null, IssueImportance? importance = IssueImportance.Important, IssueEffort? effort = IssueEffort.S,
        string assignedUserIconUrl = null) => new()
    {
        AssignedUserIconUrl = assignedUserIconUrl ?? string.Empty,
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
