using Quilt4Net.Toolkit.Features.Issue;

namespace Quilt4Net.Toolkit.Blazor.Features.Issue;

/// <summary>
/// How the roadmap is arranged. <see cref="Lanes"/> puts each route on its own row and the bands left
/// to right; <see cref="Network"/> lets the links decide where a card sits, so the dependencies are
/// the thing in the middle and the route is only a colour.
/// </summary>
public enum RoadmapView
{
    Lanes,
    Network
}

/// <summary>
/// Turns a <see cref="RoadmapResponse"/> into placed geometry for the roadmap figure.
/// </summary>
/// <remarks>
/// Layout is computed here rather than in the component's markup so it can be asserted directly:
/// "lane two sits below lane one" and "an edge joins the two boxes it names" are the properties worth
/// testing, and they are invisible from a rendered SVG string.
/// </remarks>
internal static class RoadmapLayout
{
    internal const double GutterWidth = 176;
    // 64px between the cards of neighbouring columns: room for an arrow to turn, and for the columns to read as columns.
    internal const double BandWidth = 320;
    internal const double ItemWidth = 256;

    // Three rows: number + effort, title, then status and assignee. The third row was added rather
    // than crammed into the second — the state used to share that line with the title, which left no
    // room for an assignee and truncated both.
    internal const double ItemHeight = 74;
    internal const double ItemGap = 12;
    internal const double LanePadding = 16;
    internal const double HeaderHeight = 36;

    /// <summary>
    /// The name of the virtual column terminal items are drawn in, left of Now. <c>Done</c>, the
    /// default workflow's terminal state, which reads as "finished with" whatever the outcome.
    /// </summary>
    internal const string ClosedColumn = "Done";

    /// <summary>
    /// One colour per route, in this fixed order. The first eight are the data-viz reference
    /// categorical palette; the second eight were picked to extend it, and the sixteen pass the
    /// palette validator's checks for neighbouring slots (lightness band, chroma, colour-blind and
    /// normal-vision separation) on a light surface. Not every pair of the sixteen is far apart -
    /// no sixteen colours are - which is why a lane also carries its name and a card its route in
    /// the tooltip. A route past the sixteenth starts the list again.
    /// </summary>
    internal static readonly string[] RoutePalette =
    [
        "#2a78d6", "#eb6834", "#1baf7a", "#eda100", "#e87ba4", "#008300", "#6250d6", "#e34948",
        "#00b5d1", "#9c5b2e", "#b0b800", "#a3006b", "#7a9cf0", "#5a6b00", "#3fbf95", "#c58af0"
    ];

    internal const string UnassignedRouteColour = "#898781";

    /// <summary>
    /// Lays out the roadmap as the filter shows it. A band the filter excludes is dropped rather than
    /// drawn empty, and the Closed column exists only while something is in it, so the map spends no
    /// width on columns that cannot hold a card.
    /// </summary>
    /// <param name="hideUnlinked">
    /// Drops a card with no edge left on the map once the filter has run. Judged after the filter,
    /// since a link to a hidden card is not one the reader can see either.
    /// </param>
    /// <param name="routeNames">The routes to draw. Null or empty draws every route.</param>
    /// <param name="linkKinds">The kinds of link to draw. Null or empty draws every kind.</param>
    public static RoadmapFigure Build(RoadmapResponse roadmap, IssueFilter filter = null, RoadmapView view = RoadmapView.Lanes, bool hideUnlinked = false,
        IReadOnlyCollection<string> routeNames = null, IReadOnlyCollection<IssueLinkKind> linkKinds = null)
    {
        filter ??= IssueFilter.None;

        // A kind left out is gone from the map entirely: not drawn, and not counted as a link by
        // "hide cards with no links" either.
        var edges = linkKinds is { Count: > 0 }
            ? roadmap.Edges.Where(e => linkKinds.Contains(e.Kind)).ToArray()
            : roadmap.Edges;

        // Colour follows the route, never its position on the drawn map: slots come from the whole
        // projection, so filtering a lane away does not repaint the ones left.
        var routeColours = roadmap.Routes
            .Select((route, index) => (route.Name, Colour: RoutePalette[index % RoutePalette.Length]))
            .GroupBy(x => x.Name, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First().Colour, StringComparer.Ordinal);

        var routes = roadmap.Routes.Select(route => Sort(route, filter)).ToArray();

        // Every route the filter leaves something in, whether or not it is picked: this is what a
        // host offers as the route choices, so it must not shrink to the routes already chosen.
        var legend = routes
            .Where(x => x.Closed.Length + x.Bands.Sum(b => b.Length) > 0)
            .Select(x => new RoadmapRouteColour(x.Name, routeColours[x.Name]))
            .ToArray();

        if (routeNames is { Count: > 0 })
            routes = routes.Where(x => routeNames.Contains(x.Name, StringComparer.Ordinal)).ToArray();

        if (hideUnlinked)
        {
            var shown = routes.SelectMany(x => x.Closed.Concat(x.Bands.SelectMany(b => b))).Select(x => x.Number).ToHashSet();
            var linked = edges
                .Where(e => shown.Contains(e.FromNumber) && shown.Contains(e.ToNumber))
                .SelectMany(e => new[] { e.FromNumber, e.ToNumber })
                .ToHashSet();
            routes = routes.Select(x => x.Keep(linked)).ToArray();
        }

        var shownCount = routes.Sum(x => x.Closed.Length + x.Bands.Sum(b => b.Length));
        var totalCount = roadmap.Routes.Sum(x => x.Now.Length + x.Next.Length + x.Later.Length + (x.Closed ?? []).Length);

        var figure = view == RoadmapView.Network
            ? RoadmapNetworkLayout.Place(routes.SelectMany(r => r.Closed.Concat(r.Bands.SelectMany(b => b)).Select(i => (i, r.Name, routeColours[r.Name]))).ToArray(), edges)
            : PlaceLanes(routes, edges, filter, routeColours);

        return figure with
        {
            Routes = legend,
            FilteredItemCount = totalCount - shownCount,
            FilteredEdgeCount = roadmap.Edges.Length - figure.Edges.Length
        };
    }

    private static RoadmapFigure PlaceLanes(SortedRoute[] routes, RoadmapEdgeResponse[] edges, IssueFilter filter, IReadOnlyDictionary<string, string> routeColours)
    {
        var columns = new List<(string Name, Func<SortedRoute, RoadmapItemResponse[]> Items)>();
        if (routes.Any(x => x.Closed.Length > 0)) columns.Add((ClosedColumn, x => x.Closed));
        foreach (var band in Bands.Where(filter.IncludesBand))
        {
            columns.Add((band.ToString(), x => x.Bands[(int)band]));
        }

        var lanes = new List<RoadmapLane>();
        var placed = new Dictionary<int, PlacedItem>();
        var y = HeaderHeight;

        foreach (var route in routes.Where(x => columns.Any(c => c.Items(x).Length > 0)))
        {
            var cells = columns.Select(c => c.Items(route)).ToArray();
            var deepest = cells.Max(b => b.Length);
            var laneHeight = Math.Max(ItemHeight + LanePadding * 2, deepest * (ItemHeight + ItemGap) - ItemGap + LanePadding * 2);

            for (var column = 0; column < cells.Length; column++)
            {
                for (var index = 0; index < cells[column].Length; index++)
                {
                    var item = cells[column][index];
                    var x = GutterWidth + column * BandWidth + (BandWidth - ItemWidth) / 2;
                    var itemY = y + LanePadding + index * (ItemHeight + ItemGap);
                    placed[item.Number] = new PlacedItem(item, x, itemY) { Route = route.Name, Colour = routeColours[route.Name] };
                }
            }

            lanes.Add(new RoadmapLane(route.Name, y, laneHeight) { Colour = routeColours[route.Name] });
            y += laneHeight;
        }

        return new RoadmapFigure(
            lanes.ToArray(),
            columns.Select(x => x.Name).ToArray(),
            placed.Values.ToArray(),
            JoinEdges(placed, edges, straight: false),
            GutterWidth + BandWidth * Math.Max(columns.Count, 1),
            Math.Max(y, HeaderHeight + ItemHeight),
            0,
            0);
    }

    /// <summary>The edges whose two ends are both on the map; a line to nowhere is worse than none.</summary>
    internal static PlacedEdge[] JoinEdges(IReadOnlyDictionary<int, PlacedItem> placed, RoadmapEdgeResponse[] edges, bool straight) =>
        edges
            .Where(e => placed.ContainsKey(e.FromNumber) && placed.ContainsKey(e.ToNumber))
            .Select(e => new PlacedEdge(e, placed[e.FromNumber], placed[e.ToNumber]) { Straight = straight })
            .ToArray();

    private static readonly RoadmapBand[] Bands = [RoadmapBand.Now, RoadmapBand.Next, RoadmapBand.Later];

    /// <summary>
    /// Sorts a route's items into the Closed column and the three bands, keeping only what the filter
    /// shows. A terminal item a server still listed under its band is moved to Closed here, so an older
    /// server draws the same map as a current one.
    /// </summary>
    private static SortedRoute Sort(RoadmapRouteResponse route, IssueFilter filter)
    {
        var byBand = new[] { route.Now, route.Next, route.Later }
            .Select((items, index) => items.Select(x => x.Band == null ? x with { Band = Bands[index] } : x).ToArray())
            .ToArray();

        var closed = (route.Closed ?? [])
            .Concat(byBand.SelectMany(x => x).Where(x => x.IsTerminal))
            .Where(x => Shows(filter, x))
            .ToArray();

        var bands = byBand
            .Select(items => items.Where(x => !x.IsTerminal && Shows(filter, x)).ToArray())
            .ToArray();

        return new SortedRoute(route.Name, closed, bands);
    }

    private static bool Shows(IssueFilter filter, RoadmapItemResponse item) =>
        filter.Matches(item.Band, item.Importance, item.Effort, item.State);

    private sealed record SortedRoute(string Name, RoadmapItemResponse[] Closed, RoadmapItemResponse[][] Bands)
    {
        public SortedRoute Keep(HashSet<int> numbers) =>
            this with
            {
                Closed = Closed.Where(x => numbers.Contains(x.Number)).ToArray(),
                Bands = Bands.Select(b => b.Where(x => numbers.Contains(x.Number)).ToArray()).ToArray()
            };
    }
}

/// <summary>
/// The whole figure, laid out. <paramref name="Columns"/> names the columns left to right;
/// <paramref name="FilteredItemCount"/> and <paramref name="FilteredEdgeCount"/> say what the filter
/// left off, so the caption can admit it.
/// </summary>
internal sealed record RoadmapFigure(RoadmapLane[] Lanes, string[] Columns, PlacedItem[] Items, PlacedEdge[] Edges, double Width, double Height, int FilteredItemCount, int FilteredEdgeCount)
{
    /// <summary>The routes drawn, with their colours, for the legend.</summary>
    public RoadmapRouteColour[] Routes { get; init; } = [];

    /// <summary>
    /// Network view only: where the cards with no link start, so the figure can label that block.
    /// Null when there is none.
    /// </summary>
    public double? UnlinkedTop { get; init; }
}

/// <summary>One route and the colour it is drawn in.</summary>
internal sealed record RoadmapRouteColour(string Name, string Colour);

/// <summary>One lane band, from <paramref name="Top"/> down.</summary>
internal sealed record RoadmapLane(string Name, double Top, double Height)
{
    public string Colour { get; init; } = RoadmapLayout.UnassignedRouteColour;
}

/// <summary>One issue box at a computed position.</summary>
internal sealed record PlacedItem(RoadmapItemResponse Item, double X, double Y)
{
    public double Width { get; init; } = RoadmapLayout.ItemWidth;
    public double Height { get; init; } = RoadmapLayout.ItemHeight;

    /// <summary>The route the card belongs to, for the tooltip.</summary>
    public string Route { get; init; }

    /// <summary>The route's colour.</summary>
    public string Colour { get; init; } = RoadmapLayout.UnassignedRouteColour;

    public double CenterX => X + Width / 2;
    public double CenterY => Y + Height / 2;
    public double Right => X + Width;
}

/// <summary>One edge joining two placed boxes.</summary>
internal sealed record PlacedEdge(RoadmapEdgeResponse Edge, PlacedItem From, PlacedItem To)
{
    /// <summary>A straight line between the two borders (network view) instead of the lanes' curve.</summary>
    public bool Straight { get; init; }

    /// <summary>
    /// Lanes: a cubic bezier leaving the source's right edge and arriving at the target's left edge.
    /// When the target sits left of the source the curve bows outward instead of doubling back through
    /// the boxes between them. Network: a straight line from border to border, aimed at the centres,
    /// since a card there can sit on any side of the one it links to.
    /// </summary>
    public string Path()
    {
        if (Straight)
        {
            var (sx, sy) = Border(From, To.CenterX - From.CenterX, To.CenterY - From.CenterY);
            var (tx, ty) = Border(To, From.CenterX - To.CenterX, From.CenterY - To.CenterY);
            return Format($"M {Round(sx)} {Round(sy)} L {Round(tx)} {Round(ty)}");
        }

        var y1 = From.CenterY;
        var y2 = To.CenterY;

        // A target in the same column, or further left, is reached round the left side: leaving from
        // the right edge would send the curve back across the source card to get there.
        // Every curve ends in a short straight run into the target. Between neighbouring columns the
        // gutter is narrow, so a curve that drops a whole lane is nearly vertical until its last few
        // pixels, and the arrowhead (drawn on the end tangent) then sat on a bend and looked broken off.
        var end = To.X - Lead;

        if (To.X < From.Right)
        {
            var left = From.X;
            var around = Math.Max(36, Math.Abs(y2 - y1) * 0.25) + Math.Max(0, left - To.X) * 0.5;
            return Format($"M {left} {y1} C {Round(left - around)} {y1}, {Round(end - around)} {y2}, {end} {y2} L {To.X} {y2}");
        }

        var x1 = From.Right;
        var bow = Math.Max(end - x1, 48) * 0.4;
        return Format($"M {x1} {y1} C {Round(x1 + bow)} {y1}, {Round(end - bow)} {y2}, {end} {y2} L {To.X} {y2}");
    }

    /// <summary>The straight run into a lane card: a little longer than the arrowhead it carries.</summary>
    private const double Lead = 14;

    /// <summary>Where a ray from the box's centre in direction (dx, dy) leaves the box.</summary>
    private static (double X, double Y) Border(PlacedItem box, double dx, double dy)
    {
        if (dx == 0 && dy == 0) return (box.CenterX, box.CenterY);
        var tx = dx == 0 ? double.MaxValue : box.Width / 2 / Math.Abs(dx);
        var ty = dy == 0 ? double.MaxValue : box.Height / 2 / Math.Abs(dy);
        var t = Math.Min(tx, ty);
        return (box.CenterX + dx * t, box.CenterY + dy * t);
    }

    private static double Round(double value) => Math.Round(value, 1);

    private static string Format(FormattableString value) =>
        FormattableString.Invariant(value);
}
