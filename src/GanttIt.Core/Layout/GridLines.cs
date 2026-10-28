namespace GanttIt.Core.Layout;

/// <summary>
/// Builds the chart's lines from a solved grid (§6.3). Every wanted run of
/// line is first collected per grid line (orientation + coordinate) and style;
/// then header-style runs win where they overlap chart-style runs, and touching
/// runs of one style merge. The result is one continuous primitive per grid
/// line (more only where a gap is required, e.g. a tick interrupted by a merged
/// label cell), with every endpoint taken verbatim from the grid's values.
/// </summary>
internal static class GridLines
{
    public static IEnumerable<LinePrimitive> Build(GridLayout grid)
    {
        var runs = new SortedDictionary<(bool Vertical, double At), Runs>();

        void Horizontal(double y, double x0, double x1, LineStyleKey style) => Add(false, y, x0, x1, style);
        void Vertical(double x, double y0, double y1, LineStyleKey style) => Add(true, x, y0, y1, style);
        void Add(bool vertical, double at, double a, double b, LineStyleKey style)
        {
            if (!runs.TryGetValue((vertical, at), out var r)) runs[(vertical, at)] = r = new Runs();
            (style == LineStyleKey.Header ? r.Header : r.Chart).Add((Math.Min(a, b), Math.Max(a, b)));
        }

        var left = 0.0;
        var right = grid.Width;
        var top = 0.0;
        var bottom = grid.Bottom;
        var timelineLeft = grid.TimelineLeft;

        // Outer border and header rules: header style.
        Horizontal(top, left, right, LineStyleKey.Header);
        Horizontal(bottom, left, right, LineStyleKey.Header);
        Vertical(left, bottom, top, LineStyleKey.Header);
        Vertical(right, bottom, top, LineStyleKey.Header);
        if (grid.TitleBottom < top) Horizontal(grid.TitleBottom, left, right, LineStyleKey.Header);
        Horizontal(grid.HeaderBottom, left, right, LineStyleKey.Header);

        // Row separators: chart style, full width. The last row's bottom is the border.
        for (var i = 1; i < grid.RowEdges.Count - 1; i++)
            Horizontal(grid.RowEdges[i], left, right, LineStyleKey.Chart);

        // Column dividers (table columns and the table/timeline edge), below the title.
        for (var c = 1; c < grid.ColumnEdges.Count; c++)
            Vertical(grid.ColumnEdges[c], bottom, grid.TitleBottom, LineStyleKey.Chart);

        if (grid.Tiers.Count == 2)
        {
            // Rule between the two timescale tiers, across the timeline only.
            Horizontal(grid.Tier1Bottom, timelineLeft, right, LineStyleKey.Chart);

            // Dividers between label groups, each within its own tier row.
            foreach (var tier in grid.Tiers)
                foreach (var x in tier.Dividers)
                    Vertical(x, tier.Bottom, tier.Top, LineStyleKey.Chart);

            // Tier-2 ticks through the body (dropped when too dense).
            foreach (var x in grid.BodyTicks)
                Vertical(x, bottom, grid.HeaderBottom, LineStyleKey.Chart);
        }

        foreach (var ((vertical, at), r) in runs)
        {
            var header = Merge(r.Header);
            foreach (var (a, b) in header)
                yield return Line(vertical, at, a, b, LineStyleKey.Header);
            foreach (var (a, b) in Merge(Subtract(r.Chart, header)))
                yield return Line(vertical, at, a, b, LineStyleKey.Chart);
        }
    }

    private static LinePrimitive Line(bool vertical, double at, double a, double b, LineStyleKey style) =>
        vertical
            ? new LinePrimitive(new Point2(at, a), new Point2(at, b), style)
            : new LinePrimitive(new Point2(a, at), new Point2(b, at), style);

    /// <summary>Sorts and joins overlapping or touching intervals.</summary>
    private static List<(double A, double B)> Merge(IEnumerable<(double A, double B)> intervals)
    {
        var merged = new List<(double A, double B)>();
        foreach (var (a, b) in intervals.OrderBy(i => i.A).ThenBy(i => i.B))
        {
            if (merged.Count > 0 && a <= merged[^1].B)
                merged[^1] = (merged[^1].A, Math.Max(merged[^1].B, b));
            else
                merged.Add((a, b));
        }
        return merged;
    }

    /// <summary>The parts of <paramref name="intervals"/> not covered by <paramref name="cover"/> (sorted, disjoint).</summary>
    private static IEnumerable<(double A, double B)> Subtract(
        IEnumerable<(double A, double B)> intervals, List<(double A, double B)> cover)
    {
        foreach (var (a0, b) in intervals)
        {
            var a = a0;
            foreach (var (ca, cb) in cover)
            {
                if (cb <= a || ca >= b) continue;
                if (ca > a) yield return (a, ca);
                a = Math.Max(a, cb);
                if (a >= b) break;
            }
            if (a < b) yield return (a, b);
        }
    }

    private sealed class Runs
    {
        public List<(double A, double B)> Header { get; } = [];

        public List<(double A, double B)> Chart { get; } = [];
    }
}
