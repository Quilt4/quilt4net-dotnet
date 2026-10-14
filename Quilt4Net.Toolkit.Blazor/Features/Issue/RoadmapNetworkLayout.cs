using Quilt4Net.Toolkit.Features.Issue;

namespace Quilt4Net.Toolkit.Blazor.Features.Issue;

/// <summary>
/// Places the roadmap as a network: linked cards are set out in columns so every arrow runs left to
/// right, from what comes first to what it leads to, and the links end up in the middle of the figure
/// instead of squeezed into the gutters between band columns. Cards with no link at all are set out
/// in a grid underneath.
/// </summary>
/// <remarks>
/// A layered layout (longest path for the columns, barycentre sweeps for the order within a column)
/// rather than a force simulation: a force layout points arrows every which way, and the question
/// this view answers - what leads to what - reads as a direction. Deterministic on purpose: the same
/// projection draws the same picture on every load and every server.
/// </remarks>
internal static class RoadmapNetworkLayout
{
    internal const double ItemWidth = 208;

    // Two rows: number + effort, then the title. The network view is about the links, and a card the
    // size of a lane card makes the swarm several screens wide; state and claim stay in the tooltip.
    internal const double ItemHeight = 50;

    internal const double Padding = 24;
    internal const double MinGap = 12;
    internal const double UnlinkedHeaderHeight = 36;

    /// <summary>Space between two columns, wide enough to see an arrow leave one card and reach the next.</summary>
    internal const double ColumnGap = 72;

    /// <summary>Space between two cards in one column.</summary>
    internal const double RowGap = 22;

    private const int OrderSweeps = 8;
    private const int SpacingPasses = 12;
    private const int OverlapPasses = 400;

    public static RoadmapFigure Place((RoadmapItemResponse Item, string Route, string Colour)[] items, RoadmapEdgeResponse[] edges)
    {
        var numbers = items.Select(x => x.Item.Number).ToHashSet();
        var drawn = edges
            .Where(e => numbers.Contains(e.FromNumber) && numbers.Contains(e.ToNumber) && e.FromNumber != e.ToNumber)
            .ToArray();
        var linkedNumbers = drawn.SelectMany(e => new[] { e.FromNumber, e.ToNumber }).ToHashSet();

        var linked = items.Where(x => linkedNumbers.Contains(x.Item.Number)).ToArray();
        var unlinked = items.Where(x => !linkedNumbers.Contains(x.Item.Number)).OrderBy(x => x.Item.Number).ToArray();

        // Each cluster of linked cards is laid out on its own and the clusters are then packed in rows.
        // Laid out together, clusters that share no link would share columns and stretch every
        // column to the height of all of them.
        var order = linked.Select((x, i) => (x.Item.Number, i)).ToDictionary(x => x.Number, x => x.i);
        var clusters = Clusters(linked.Select(x => x.Item.Number).ToArray(), drawn)
            .Select(numbers =>
            {
                var set = numbers.ToHashSet();
                var centres = Layered(numbers.OrderBy(n => order[n]).ToArray(), drawn.Where(e => set.Contains(e.FromNumber)).ToArray());
                RemoveOverlaps(centres);
                var minX = centres.Values.Min(c => c.X);
                var minY = centres.Values.Min(c => c.Y);
                var local = centres.ToDictionary(x => x.Key, x => new Point(x.Value.X - minX, x.Value.Y - minY));
                return (Centres: local,
                    Width: local.Values.Max(c => c.X) + ItemWidth,
                    Height: local.Values.Max(c => c.Y) + ItemHeight);
            })
            .OrderByDescending(x => x.Centres.Count)
            .ThenBy(x => x.Centres.Keys.Min())
            .ToArray();

        var placed = new Dictionary<int, PlacedItem>();
        double width = 0, height = 0;

        if (clusters.Length > 0)
        {
            // Rows about as wide as the clusters would be if laid out square, and never narrower than
            // the widest one, so a figure reads like a page rather than a strip.
            var area = clusters.Sum(x => (x.Width + ClusterGap) * (x.Height + ClusterGap));
            var rowWidth = Math.Max(clusters.Max(x => x.Width), Math.Sqrt(area) * 1.5);
            var byNumber = linked.ToDictionary(x => x.Item.Number);

            double x = 0, y = 0, rowHeight = 0;
            foreach (var cluster in clusters)
            {
                if (x > 0 && x + cluster.Width > rowWidth)
                {
                    x = 0;
                    y += rowHeight + ClusterGap;
                    rowHeight = 0;
                }

                foreach (var (number, c) in cluster.Centres)
                {
                    var (item, route, colour) = byNumber[number];
                    placed[number] = Card(item, route, colour, Padding + x + c.X, Padding + y + c.Y);
                }

                x += cluster.Width + ClusterGap;
                rowHeight = Math.Max(rowHeight, cluster.Height);
            }

            width = placed.Values.Max(p => p.Right) + Padding;
            height = placed.Values.Max(p => p.Y + p.Height) + Padding;
        }

        double? unlinkedTop = null;
        if (unlinked.Length > 0)
        {
            // As wide as the swarm, but never so narrow that a short swarm stacks them in one column.
            var perRow = Math.Max(4, (int)((width - Padding) / (ItemWidth + MinGap)));
            unlinkedTop = height + (height > 0 ? MinGap : 0);
            var top = unlinkedTop.Value + UnlinkedHeaderHeight;
            for (var i = 0; i < unlinked.Length; i++)
            {
                var (item, route, colour) = unlinked[i];
                placed[item.Number] = Card(item, route, colour, Padding + i % perRow * (ItemWidth + MinGap), top + i / perRow * (ItemHeight + MinGap));
            }

            width = Math.Max(width, Padding * 2 + Math.Min(perRow, unlinked.Length) * (ItemWidth + MinGap) - MinGap);
            height = placed.Values.Max(x => x.Y + x.Height) + Padding;
        }

        return new RoadmapFigure([], [], placed.Values.ToArray(), RoadmapLayout.JoinEdges(placed, drawn, straight: true), width, height, 0, 0)
        {
            UnlinkedTop = unlinkedTop
        };
    }

    /// <summary>Space between two packed clusters: wider than between cards, so a cluster reads as one.</summary>
    internal const double ClusterGap = 64;

    /// <summary>The connected groups of cards, ignoring edge direction, each in first-seen order.</summary>
    private static List<int[]> Clusters(int[] numbers, RoadmapEdgeResponse[] edges)
    {
        var parent = numbers.ToDictionary(x => x, x => x);
        int Find(int n) => parent[n] == n ? n : parent[n] = Find(parent[n]);
        foreach (var e in edges) parent[Find(e.FromNumber)] = Find(e.ToNumber);

        return numbers.GroupBy(Find).Select(g => g.ToArray()).ToList();
    }

    private static PlacedItem Card(RoadmapItemResponse item, string route, string colour, double x, double y) =>
        new(item, x, y) { Width = ItemWidth, Height = ItemHeight, Route = route, Colour = colour };

    /// <summary>
    /// Top-left corners for one connected cluster. Each card's column is the longest chain of
    /// directed links leading to it, so every <c>blocks</c> and <c>cheapens</c> arrow points right.
    /// <c>overlaps</c> has no direction and places nothing, except that a card linked only that way
    /// stands in its partner's column. Within a column, cards are ordered towards the average row of
    /// their neighbours, which keeps arrows short and uncrossed, and then spaced out.
    /// </summary>
    private static Dictionary<int, Point> Layered(int[] numbers, RoadmapEdgeResponse[] edges)
    {
        var index = numbers.Select((n, i) => (n, i)).ToDictionary(x => x.n, x => x.i);
        var count = numbers.Length;
        var directed = edges.Where(e => e.Kind != IssueLinkKind.Overlaps)
            .Select(e => (From: index[e.FromNumber], To: index[e.ToNumber])).Distinct().ToArray();
        var neighbours = Enumerable.Range(0, count).Select(_ => new List<int>()).ToArray();
        foreach (var e in edges)
        {
            neighbours[index[e.FromNumber]].Add(index[e.ToNumber]);
            neighbours[index[e.ToNumber]].Add(index[e.FromNumber]);
        }

        var kept = WithoutCycles(count, directed);
        var overlaps = edges.Where(e => e.Kind == IssueLinkKind.Overlaps).Select(e => (index[e.FromNumber], index[e.ToNumber])).ToArray();
        var layer = Columns(count, kept, overlaps);

        // Order within each column: first-seen order, then sweeps right and left, each card moving
        // towards the average position of its neighbours in other columns.
        var columns = Enumerable.Range(0, layer.Max() + 1)
            .Select(c => Enumerable.Range(0, count).Where(i => layer[i] == c).ToList())
            .ToArray();
        var rank = new double[count];
        foreach (var column in columns) for (var i = 0; i < column.Count; i++) rank[column[i]] = i;

        double Across(int i, double[] value, double fallback)
        {
            var others = neighbours[i].Where(n => layer[n] != layer[i]).ToArray();
            return others.Length == 0 ? fallback : others.Average(n => value[n]);
        }

        for (var sweep = 0; sweep < OrderSweeps; sweep++)
        {
            foreach (var column in sweep % 2 == 0 ? columns : columns.Reverse())
            {
                var key = column.ToDictionary(i => i, i => Across(i, rank, rank[i]));
                column.Sort((a, b) => key[a] != key[b] ? key[a].CompareTo(key[b]) : rank[a].CompareTo(rank[b]));
                for (var i = 0; i < column.Count; i++) rank[column[i]] = i;
            }
        }

        // Rows: stacked, then nudged towards the neighbours' rows so a chain runs level, but never
        // closer than a card and a gap.
        const double pitch = ItemHeight + RowGap;
        var y = new double[count];
        foreach (var column in columns) for (var i = 0; i < column.Count; i++) y[column[i]] = i * pitch;
        for (var pass = 0; pass < SpacingPasses; pass++)
        {
            foreach (var column in pass % 2 == 0 ? columns : columns.Reverse())
            {
                var wanted = column.Select(i => Across(i, y, y[i])).ToArray();
                for (var i = 0; i < column.Count; i++) y[column[i]] = i == 0 ? wanted[i] : Math.Max(wanted[i], y[column[i - 1]] + pitch);
                for (var i = column.Count - 2; i >= 0; i--) y[column[i]] = Math.Min(y[column[i]], y[column[i + 1]] - pitch);
            }
        }

        const double step = ItemWidth + ColumnGap;
        return numbers.Select((n, i) => (n, new Point(layer[i] * step, y[i]))).ToDictionary(x => x.n, x => x.Item2);
    }

    /// <summary>
    /// The directed links minus those that would close a loop, found depth first in first-seen order.
    /// A loop has no left-to-right reading, so one of its arrows is left to point back.
    /// </summary>
    private static (int From, int To)[] WithoutCycles(int count, (int From, int To)[] links)
    {
        var outgoing = Enumerable.Range(0, count).Select(i => links.Where(l => l.From == i).Select(l => l.To).ToArray()).ToArray();
        var state = new int[count]; // 0 unseen, 1 on the current path, 2 done
        var back = new HashSet<(int, int)>();

        for (var root = 0; root < count; root++)
        {
            if (state[root] != 0) continue;
            var stack = new Stack<(int Node, int Next)>();
            stack.Push((root, 0));
            state[root] = 1;
            while (stack.Count > 0)
            {
                var (node, next) = stack.Pop();
                if (next == outgoing[node].Length)
                {
                    state[node] = 2;
                    continue;
                }

                stack.Push((node, next + 1));
                var to = outgoing[node][next];
                if (state[to] == 1) back.Add((node, to));
                else if (state[to] == 0)
                {
                    state[to] = 1;
                    stack.Push((to, 0));
                }
            }
        }

        return links.Where(l => !back.Contains(l)).ToArray();
    }

    /// <summary>
    /// Each card's column: the longest chain of links leading to it. A card that only starts chains
    /// is then moved right to sit just before the nearest card it leads to, so its arrows do not
    /// stretch across empty columns. A card linked only by <c>overlaps</c> takes its partner's column.
    /// </summary>
    private static int[] Columns(int count, (int From, int To)[] links, (int A, int B)[] overlaps)
    {
        var layer = new int[count];
        var incoming = new int[count];
        foreach (var l in links) incoming[l.To]++;
        var ready = new Queue<int>(Enumerable.Range(0, count).Where(i => incoming[i] == 0));
        while (ready.Count > 0)
        {
            var node = ready.Dequeue();
            foreach (var l in links.Where(l => l.From == node))
            {
                layer[l.To] = Math.Max(layer[l.To], layer[node] + 1);
                if (--incoming[l.To] == 0) ready.Enqueue(l.To);
            }
        }

        for (var i = 0; i < count; i++)
        {
            var targets = links.Where(l => l.From == i).Select(l => layer[l.To]).ToArray();
            if (targets.Length > 0 && links.All(l => l.To != i)) layer[i] = targets.Min() - 1;
        }

        var placed = links.SelectMany(l => new[] { l.From, l.To }).ToHashSet();
        for (var pass = 0; pass < count; pass++)
        {
            var moved = false;
            foreach (var (a, b) in overlaps)
            {
                if (!placed.Contains(a) && placed.Contains(b)) { layer[a] = layer[b]; placed.Add(a); moved = true; }
                else if (!placed.Contains(b) && placed.Contains(a)) { layer[b] = layer[a]; placed.Add(b); moved = true; }
            }

            if (!moved) break;
        }

        // Cards linked only by overlaps among themselves: side by side, in first-seen order.
        foreach (var (a, b) in overlaps)
        {
            if (placed.Contains(a) || placed.Contains(b)) continue;
            layer[b] = layer[a] + 1;
            placed.Add(a);
            placed.Add(b);
        }

        var lowest = layer.Min();
        return layer.Select(x => x - lowest).ToArray();
    }

    /// <summary>
    /// A safety net: the layered placement keeps a column's cards apart and the columns themselves
    /// apart, so this should find nothing. Each pass pushes every overlapping pair apart along the
    /// axis where they overlap least, half each way, until no two cards (plus a gap) intersect.
    /// </summary>
    private static void RemoveOverlaps(Dictionary<int, Point> centres)
    {
        var keys = centres.Keys.OrderBy(x => x).ToArray();
        var pos = keys.Select(x => centres[x]).ToArray();
        const double w = ItemWidth + MinGap;
        const double h = ItemHeight + MinGap;

        for (var pass = 0; pass < OverlapPasses; pass++)
        {
            var moved = false;
            for (var a = 0; a < pos.Length; a++)
            {
                for (var b = a + 1; b < pos.Length; b++)
                {
                    var ox = w - Math.Abs(pos[a].X - pos[b].X);
                    var oy = h - Math.Abs(pos[a].Y - pos[b].Y);
                    if (ox <= 0 || oy <= 0) continue;

                    moved = true;
                    if (ox / w < oy / h)
                    {
                        var shift = (ox / 2 + 0.5) * (pos[a].X < pos[b].X || (pos[a].X == pos[b].X && a < b) ? -1 : 1);
                        pos[a] = pos[a] with { X = pos[a].X + shift };
                        pos[b] = pos[b] with { X = pos[b].X - shift };
                    }
                    else
                    {
                        var shift = (oy / 2 + 0.5) * (pos[a].Y < pos[b].Y || (pos[a].Y == pos[b].Y && a < b) ? -1 : 1);
                        pos[a] = pos[a] with { Y = pos[a].Y + shift };
                        pos[b] = pos[b] with { Y = pos[b].Y - shift };
                    }
                }
            }

            if (!moved) break;
        }

        for (var i = 0; i < keys.Length; i++) centres[keys[i]] = pos[i];
    }

    private readonly record struct Point(double X, double Y);
}
