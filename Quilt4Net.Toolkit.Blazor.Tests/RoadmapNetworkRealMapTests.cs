using FluentAssertions;
using Quilt4Net.Toolkit.Blazor.Features.Issue;
using Quilt4Net.Toolkit.Features.Issue;
using Xunit;

namespace Quilt4Net.Toolkit.Blazor.Tests;

/// <summary>
/// The network view on the shape of a real backlog: 174 cards and 180 links, numbers and link kinds
/// only. The synthetic swarms in <see cref="IssueRoadmapTests"/> are tidy trees; this one has the
/// hubs, the overlaps-only cards and the long chains a real backlog grows, which is what the first
/// layered layout drew 5,800px tall with one overlaps line running most of that height.
/// </summary>
public class RoadmapNetworkRealMapTests
{
    private const string Numbers =
        "2 30 31 32 33 167 169 3 7 13 14 40 43 168 4 5 15 20 172 25 10 16 21 37 41 8 11 42 173 174 175 180 181 27 177 178 35 26 182 28 36 140 143 47 73 51 68 141 144 146 147 171 49 53 54 69 152 153 155 156 157 158 148 164 183 55 56 59 60 159 161 176 179 58 64 65 67 162 154 61 70 74 142 149 106 109 112 145 150 104 151 120 114 116 117 121 110 113 105 160 111 127 115 118 119 125 126 91 98 85 86 87 88 89 100 90 92 93 95 97 99 170 94 96 101 128 133 134 132 75 82 78 79 80 81 19 166 136 135 137 138 6 9 12 17 18 22 23 24 29 34 38 39 46 48 50 52 57 62 63 66 71 72 83 102 107 108 122 123 124 129 130 131 165";

    private const string Links =
        "2,167,o 3,8,b 3,10,b 3,11,b 3,158,b 3,162,b 7,3,o 7,15,o 7,56,b 8,56,b 13,94,b 14,94,b 19,166,o 20,21,b 20,96,b 21,96,b 25,26,b 25,27,b 25,56,b 25,89,b 27,28,b 27,36,b 27,37,b 27,42,b 27,58,b 27,119,b 28,36,b 28,70,b 30,31,b 30,169,o 31,32,b 31,33,b 31,54,c 35,36,b 40,119,b 41,119,b 43,119,b 47,59,b 47,93,b 49,65,b " +
        "56,162,b 58,119,b 59,61,b 60,16,b 65,171,o 68,69,c 69,67,b 69,70,b 73,27,b 73,41,b 73,42,b 73,16,b 73,60,b 73,74,b 73,119,b " +
        "78,79,b 78,81,b 79,80,b 79,81,b 82,168,o 85,86,b 86,87,b 86,88,b 87,97,b 87,99,b 87,100,b 88,89,b 88,90,b 88,92,b 88,93,b 88,94,b 88,97,b 88,119,b 88,170,b 91,92,b 96,160,b 104,105,b 104,127,b 105,94,b 106,88,b 109,111,b 110,111,b 111,132,b 112,113,b 112,115,b 114,115,b 114,118,c 116,118,b 117,118,b 120,89,b 120,96,b 121,96,b 125,96,b 126,35,b 128,55,b 128,141,b 133,87,o 136,135,b 137,138,b 140,141,b 140,75,o 140,133,o 141,55,o 142,149,c 143,101,o 143,55,o 143,73,o 144,55,o 145,149,c 145,112,o 145,115,o 146,161,c 146,152,o 146,118,o 147,3,o 147,53,o 147,101,o 147,73,o 148,54,o 148,115,o " +
        "149,111,o 149,97,o 149,132,o 150,149,c 150,111,o 150,113,o 150,151,o 151,149,c 151,160,o 151,112,o 152,161,b 152,56,o 152,101,o 153,154,b 153,33,o 153,134,o 155,156,b 155,64,o 155,95,o 156,119,o 156,27,o 157,158,b 157,3,o 158,53,o 159,101,o 159,96,o 160,111,o 161,27,o 162,32,o 162,161,o 167,168,b 167,169,b 167,171,b 169,170,b 172,173,b 172,176,b 173,174,b 173,175,b 173,177,b 173,4,o 173,5,c 174,180,b 174,89,o 174,90,o 175,181,b 175,98,o 175,51,o 176,65,b 176,164,o 177,178,b 177,183,b 178,179,b 178,182,b 178,3,o 178,15,o 178,7,o 179,180,b 179,181,b 179,67,c 183,8,o";

    private static RoadmapFigure Figure()
    {
        var items = Numbers.Split(' ').Select(int.Parse).Select(n => new RoadmapItemResponse
        {
            Number = n,
            Title = $"issue {n}",
            State = "Todo",
            StateKind = RoadmapStateKind.NotStarted,
            AssignedUserKey = string.Empty,
            IsTerminal = false,
            IsQuickWin = false,
            Effort = null
        }).ToArray();
        var edges = Links.Split(' ').Select(x => x.Split(',')).Select(x => new RoadmapEdgeResponse
        {
            FromNumber = int.Parse(x[0]),
            ToNumber = int.Parse(x[1]),
            Kind = x[2] switch { "b" => IssueLinkKind.Blocks, "c" => IssueLinkKind.Cheapens, _ => IssueLinkKind.Overlaps },
            Reason = "r"
        }).ToArray();

        var roadmap = new RoadmapResponse
        {
            Routes = [new RoadmapRouteResponse { Name = "r", Now = items, Next = [], Later = [] }],
            Edges = edges,
            UnroutedCount = 0,
            HiddenCount = 0,
            GeneratedUtc = new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc)
        };

        return RoadmapLayout.Build(roadmap, view: RoadmapView.Network);
    }

    private static double Length(PlacedEdge e) =>
        Math.Sqrt(Math.Pow(e.To.CenterX - e.From.CenterX, 2) + Math.Pow(e.To.CenterY - e.From.CenterY, 2));

    [Fact]
    public void Every_card_and_link_is_drawn()
    {
        var figure = Figure();

        figure.Items.Should().HaveCount(174);
        figure.Edges.Should().HaveCount(180);
    }

    [Fact]
    public void The_map_is_not_mostly_empty()
    {
        var figure = Figure();

        // Card area against the figure's area. The first layered version came out at about 3%.
        var cards = figure.Items.Sum(x => x.Width * x.Height);
        var fill = cards / (figure.Width * figure.Height);
        fill.Should().BeGreaterThan(0.15, $"the figure is {figure.Width:0} x {figure.Height:0}");
    }

    [Fact]
    public void No_link_runs_across_most_of_the_map()
    {
        var figure = Figure();

        var longest = figure.Edges.MaxBy(Length);
        Length(longest).Should().BeLessThan(Math.Max(figure.Width, figure.Height) * 0.5,
            $"#{longest.From.Item.Number} -> #{longest.To.Item.Number} ({longest.Edge.Kind}) on a {figure.Width:0} x {figure.Height:0} figure");
    }

    [Fact]
    public void A_card_linked_only_by_overlaps_sits_beside_its_partner()
    {
        var figure = Figure();

        // #2 has one link, an overlaps to #167. In #167's own column the pair was drawn as one
        // vertical line through 3,300px of other cards.
        var pair = figure.Edges.Single(x => x.From.Item.Number == 2 && x.To.Item.Number == 167);
        Math.Abs(pair.To.CenterX - pair.From.CenterX).Should().Be(RoadmapNetworkLayout.ItemWidth + RoadmapNetworkLayout.ColumnGap, "the next column");
        Length(pair).Should().BeLessThan(1000, "it may sit a few rows off in a crowded column, but not across the map");
    }

    [Fact]
    public void Directed_links_still_run_left_to_right()
    {
        var figure = Figure();

        var backwards = figure.Edges
            .Where(x => x.Edge.Kind != IssueLinkKind.Overlaps)
            .Count(x => x.To.X <= x.From.Right);

        // This backlog has no loop of blocks/cheapens links, so none needs to point back.
        backwards.Should().Be(0);
    }
}

