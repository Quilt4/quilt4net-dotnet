using Quilt4Net.Toolkit.Features.Issue;

namespace Quilt4Net.Toolkit.Blazor.Features.Issue;

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
    internal const double BandWidth = 288;
    internal const double ItemWidth = 256;

    // Three rows: number + effort, title, then status and assignee. The third row was added rather
    // than crammed into the second — the state used to share that line with the title, which left no
    // room for an assignee and truncated both.
    internal const double ItemHeight = 74;
    internal const double ItemGap = 12;
    internal const double LanePadding = 16;
    internal const double HeaderHeight = 36;

    /// <summary>The name of the virtual column terminal items are drawn in, left of Now.</summary>
    internal const string ClosedColumn = "Closed";

    /// <summary>
    /// Lays out the roadmap as the filter shows it. A band the filter excludes is dropped rather than
    /// drawn empty, and the Closed column exists only while something is in it, so the map spends no
    /// width on columns that cannot hold a card.
    /// </summary>
    public static RoadmapFigure Build(RoadmapResponse roadmap, IssueFilter filter = null)
    {
        filter ??= IssueFilter.None;

        var routes = roadmap.Routes.Select(route => Sort(route, filter)).ToArray();
        var shownCount = routes.Sum(x => x.Closed.Length + x.Bands.Sum(b => b.Length));

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
                    placed[item.Number] = new PlacedItem(item, x, itemY);
                }
            }

            lanes.Add(new RoadmapLane(route.Name, y, laneHeight));
            y += laneHeight;
        }

        var edges = roadmap.Edges
            .Where(e => placed.ContainsKey(e.FromNumber) && placed.ContainsKey(e.ToNumber))
            .Select(e => new PlacedEdge(e, placed[e.FromNumber], placed[e.ToNumber]))
            .ToArray();

        var totalCount = roadmap.Routes.Sum(x => x.Now.Length + x.Next.Length + x.Later.Length + (x.Closed ?? []).Length);

        return new RoadmapFigure(
            lanes.ToArray(),
            columns.Select(x => x.Name).ToArray(),
            placed.Values.ToArray(),
            edges,
            GutterWidth + BandWidth * Math.Max(columns.Count, 1),
            Math.Max(y, HeaderHeight + ItemHeight),
            totalCount - shownCount,
            roadmap.Edges.Length - edges.Length);
    }

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

    private sealed record SortedRoute(string Name, RoadmapItemResponse[] Closed, RoadmapItemResponse[][] Bands);
}

/// <summary>
/// The whole figure, laid out. <paramref name="Columns"/> names the columns left to right;
/// <paramref name="FilteredItemCount"/> and <paramref name="FilteredEdgeCount"/> say what the filter
/// left off, so the caption can admit it.
/// </summary>
internal sealed record RoadmapFigure(RoadmapLane[] Lanes, string[] Columns, PlacedItem[] Items, PlacedEdge[] Edges, double Width, double Height, int FilteredItemCount, int FilteredEdgeCount);

/// <summary>One lane band, from <paramref name="Top"/> down.</summary>
internal sealed record RoadmapLane(string Name, double Top, double Height);

/// <summary>One issue box at a computed position.</summary>
internal sealed record PlacedItem(RoadmapItemResponse Item, double X, double Y)
{
    public double CenterY => Y + RoadmapLayout.ItemHeight / 2;
    public double Right => X + RoadmapLayout.ItemWidth;
}

/// <summary>One edge joining two placed boxes.</summary>
internal sealed record PlacedEdge(RoadmapEdgeResponse Edge, PlacedItem From, PlacedItem To)
{
    /// <summary>
    /// A cubic bezier leaving the source's right edge and arriving at the target's left edge. When
    /// the target sits left of the source the curve bows outward instead of doubling back through
    /// the boxes between them.
    /// </summary>
    public string Path()
    {
        var x1 = From.Right;
        var y1 = From.CenterY;
        var x2 = To.X;
        var y2 = To.CenterY;
        var span = Math.Max(Math.Abs(x2 - x1), 48);
        var bow = x2 >= x1 ? span * 0.4 : span * 0.6;
        return Format($"M {x1} {y1} C {x1 + bow} {y1}, {x2 - bow} {y2}, {x2} {y2}");
    }

    private static string Format(FormattableString value) =>
        FormattableString.Invariant(value);
}
