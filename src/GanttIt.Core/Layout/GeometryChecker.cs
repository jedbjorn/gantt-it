using System.Globalization;

namespace GanttIt.Core.Layout;

public enum ViolationKind
{
    /// <summary>A text box intersects or touches a line.</summary>
    TextCrossesLine,

    /// <summary>A line endpoint is not on a grid x/y value.</summary>
    EndpointOffGrid,

    /// <summary>Two collinear segments overlap by more than the tolerance.</summary>
    CollinearOverlap,
}

public sealed record GeometryViolation(ViolationKind Kind, string Detail);

public sealed record GeometryReport(IReadOnlyList<GeometryViolation> Violations)
{
    public bool IsClean => Violations.Count == 0;

    public override string ToString() =>
        IsClean ? "clean" : string.Join(Environment.NewLine, Violations.Select(v => $"{v.Kind}: {v.Detail}"));
}

/// <summary>
/// The A5 clean-lines rule set (§6.3), shared by the Core tests and the Revit
/// Self-Test. Pure geometry: it takes plain boxes, segments and grid values, so
/// it can check Emit output or the bounding boxes and curve endpoints read back
/// from a generated legend.
/// <list type="number">
/// <item>No text box intersects or touches any line.</item>
/// <item>Every line endpoint lies on a grid x and a grid y value (within the tolerance).</item>
/// <item>No two collinear segments overlap (sharing an endpoint is fine).</item>
/// </list>
/// </summary>
public static class GeometryChecker
{
    /// <summary>The A5 tolerance: 1e-6 ft.</summary>
    public const double DefaultTolerance = 1e-6;

    /// <summary>Checks Emit output against the grid it was emitted from.</summary>
    public static GeometryReport Check(IEnumerable<Primitive> primitives, GridLayout grid, double tolerance = DefaultTolerance)
    {
        var list = primitives as IReadOnlyCollection<Primitive> ?? primitives.ToList();
        return Check(
            list.OfType<TextPrimitive>().Select(t => t.MeasuredBox),
            list.OfType<LinePrimitive>().Select(l => (l.Start, l.End)),
            grid.GridXs,
            grid.GridYs,
            tolerance);
    }

    /// <summary>Checks raw geometry, e.g. read back from Revit.</summary>
    public static GeometryReport Check(
        IEnumerable<Box> textBoxes,
        IEnumerable<(Point2 Start, Point2 End)> lines,
        IEnumerable<double> gridXs,
        IEnumerable<double> gridYs,
        double tolerance = DefaultTolerance)
    {
        var boxes = textBoxes.ToList();
        var segments = lines.ToList();
        var xs = gridXs.Order().ToArray();
        var ys = gridYs.Order().ToArray();
        var violations = new List<GeometryViolation>();

        foreach (var (start, end) in segments)
        {
            foreach (var box in boxes)
                if (SegmentTouchesBox(start, end, box))
                    violations.Add(new(ViolationKind.TextCrossesLine, $"line {Fmt(start)}–{Fmt(end)} touches text box {Fmt(box)}"));

            foreach (var p in new[] { start, end })
                if (!OnGrid(xs, p.X, tolerance) || !OnGrid(ys, p.Y, tolerance))
                    violations.Add(new(ViolationKind.EndpointOffGrid, $"endpoint {Fmt(p)} is not on the grid"));
        }

        violations.AddRange(CollinearOverlaps(segments, tolerance));
        return new GeometryReport(violations);
    }

    private static bool OnGrid(double[] sorted, double v, double tolerance)
    {
        if (sorted.Length == 0) return false;
        var i = Array.BinarySearch(sorted, v);
        if (i >= 0) return true;
        i = ~i;
        return (i < sorted.Length && sorted[i] - v <= tolerance)
               || (i > 0 && v - sorted[i - 1] <= tolerance);
    }

    /// <summary>
    /// Liang–Barsky clip of the segment against the closed box: true when any
    /// point of the segment lies inside or on the box's edge.
    /// </summary>
    private static bool SegmentTouchesBox(Point2 a, Point2 b, Box box)
    {
        double t0 = 0, t1 = 1;
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        return Clip(-dx, a.X - box.Left) && Clip(dx, box.Right - a.X)
            && Clip(-dy, a.Y - box.Bottom) && Clip(dy, box.Top - a.Y);

        bool Clip(double p, double q)
        {
            if (p == 0) return q >= 0;
            var r = q / p;
            if (p < 0)
            {
                if (r > t1) return false;
                if (r > t0) t0 = r;
            }
            else
            {
                if (r < t0) return false;
                if (r < t1) t1 = r;
            }
            return true;
        }
    }

    /// <summary>
    /// Axis-aligned segments are swept per line (coordinates within the
    /// tolerance count as one line); any others are compared pairwise.
    /// </summary>
    private static IEnumerable<GeometryViolation> CollinearOverlaps(List<(Point2 Start, Point2 End)> segments, double tolerance)
    {
        var horizontal = new List<(double At, double A, double B)>();
        var vertical = new List<(double At, double A, double B)>();
        var other = new List<(Point2 Start, Point2 End)>();
        foreach (var (s, e) in segments)
        {
            if (Math.Abs(s.Y - e.Y) <= tolerance) horizontal.Add((s.Y, Math.Min(s.X, e.X), Math.Max(s.X, e.X)));
            else if (Math.Abs(s.X - e.X) <= tolerance) vertical.Add((s.X, Math.Min(s.Y, e.Y), Math.Max(s.Y, e.Y)));
            else other.Add((s, e));
        }

        foreach (var v in Sweep(horizontal, "horizontal", tolerance)) yield return v;
        foreach (var v in Sweep(vertical, "vertical", tolerance)) yield return v;

        for (var i = 0; i < other.Count; i++)
            for (var j = i + 1; j < other.Count; j++)
                if (GeneralOverlap(other[i], other[j], tolerance))
                    yield return new(ViolationKind.CollinearOverlap,
                        $"segments {Fmt(other[i].Start)}–{Fmt(other[i].End)} and {Fmt(other[j].Start)}–{Fmt(other[j].End)} overlap");
    }

    private static IEnumerable<GeometryViolation> Sweep(List<(double At, double A, double B)> runs, string name, double tolerance)
    {
        var sorted = runs.OrderBy(r => r.At).ThenBy(r => r.A).ToList();
        for (var i = 0; i < sorted.Count;)
        {
            // One line: every run whose coordinate is within tolerance of the first.
            var j = i;
            while (j < sorted.Count && sorted[j].At - sorted[i].At <= tolerance) j++;
            var line = sorted.GetRange(i, j - i).OrderBy(r => r.A).ToList();
            var reach = double.NegativeInfinity;
            foreach (var (at, a, b) in line)
            {
                if (a < reach - tolerance)
                    yield return new(ViolationKind.CollinearOverlap,
                        string.Create(CultureInfo.InvariantCulture, $"{name} segments overlap on {at:R} near {a:R}"));
                reach = Math.Max(reach, b);
            }
            i = j;
        }
    }

    private static bool GeneralOverlap((Point2 Start, Point2 End) s, (Point2 Start, Point2 End) t, double tolerance)
    {
        var dx = s.End.X - s.Start.X;
        var dy = s.End.Y - s.Start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length <= tolerance) return false;
        var ux = dx / length;
        var uy = dy / length;

        // Perpendicular distance of t's endpoints from s's line.
        double Distance(Point2 p) => Math.Abs((p.X - s.Start.X) * uy - (p.Y - s.Start.Y) * ux);
        if (Distance(t.Start) > tolerance || Distance(t.End) > tolerance) return false;

        // Overlap of the projections onto s's direction.
        double Project(Point2 p) => (p.X - s.Start.X) * ux + (p.Y - s.Start.Y) * uy;
        var t0 = Project(t.Start);
        var t1 = Project(t.End);
        return Math.Min(length, Math.Max(t0, t1)) - Math.Max(0, Math.Min(t0, t1)) > tolerance;
    }

    private static string Fmt(Point2 p) => string.Create(CultureInfo.InvariantCulture, $"({p.X:R}, {p.Y:R})");

    private static string Fmt(Box b) =>
        string.Create(CultureInfo.InvariantCulture, $"[{b.Left:R}, {b.Bottom:R} – {b.Right:R}, {b.Top:R}]");
}
