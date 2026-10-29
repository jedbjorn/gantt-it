using System.Globalization;
using GanttIt.Core.Dates;
using GanttIt.Core.Model;
using GanttIt.Core.Timeline;

namespace GanttIt.Core.Layout;

/// <summary>The result of a full layout run.</summary>
public sealed record ChartLayout(GridLayout Grid, IReadOnlyList<Primitive> Primitives);

/// <summary>
/// The layout engine (§6), shared by the Revit generator and the WPF preview:
/// <code>
/// cells = BuildCells(chart, settings)
/// sizes = Measure(cells, measurer)
/// grid  = SolveGrid(cells, sizes, settings, measurer)   // re-measures wrapped text
/// prims = Emit(grid, chart)
/// </code>
/// <see cref="Layout"/> runs all four steps.
/// </summary>
public static class LayoutEngine
{
    /// <summary>
    /// When tier-2 ticks would be closer than this, they are not drawn through
    /// the body (§6.3: day ticks under 1 mm); the label-group dividers are drawn instead.
    /// </summary>
    public static readonly double MinTickSpacing = Units.FromMillimeters(1);

    public const string NameHeader = "Section";
    public const string StartHeader = "Start";
    public const string EndHeader = "End";
    public const string DurationHeader = "Dur";

    /// <summary>Runs BuildCells → Measure → SolveGrid → Emit.</summary>
    public static ChartLayout Layout(Chart chart, ProjectSettings settings, ITextMeasurer measurer)
    {
        var cells = BuildCells(chart, settings);
        var sizes = Measure(cells, measurer);
        var grid = SolveGrid(cells, sizes, settings, measurer);
        return new ChartLayout(grid, Emit(grid, chart));
    }

    /// <summary>Every string the chart may draw, with its style (§6.1).</summary>
    public static CellSet BuildCells(Chart chart, ProjectSettings settings)
    {
        var columns = new List<TableColumn> { TableColumn.Name };
        if (chart.Columns.HasFlag(ChartColumns.Start)) columns.Add(TableColumn.Start);
        if (chart.Columns.HasFlag(ChartColumns.End)) columns.Add(TableColumn.End);
        if (chart.Columns.HasFlag(ChartColumns.Duration)) columns.Add(TableColumn.Duration);

        var headers = columns.Select(c => Header(HeaderText(c))).ToList();
        var rows = chart.Sections
            .Select(s => (IReadOnlyList<TextRequest>)columns.Select(c => Body(BodyText(s, c, settings.DateFormat))).ToList())
            .ToList();

        var axis = TimeAxis.ForChart(chart);
        var tiers = axis is null
            ? []
            : new[] { BuildTier(axis, axis.Tiers.Tier1, true, settings), BuildTier(axis, axis.Tiers.Tier2, false, settings) };

        return new CellSet
        {
            Chart = chart,
            Title = string.IsNullOrWhiteSpace(chart.Title) ? null : new TextRequest(chart.Title, TextStyleKey.Title),
            Columns = columns,
            Headers = headers,
            Rows = rows,
            Axis = axis,
            Tiers = tiers,
        };
    }

    /// <summary>Measures every distinct request in one batch.</summary>
    public static TextSizes Measure(CellSet cells, ITextMeasurer measurer)
    {
        var sizes = new TextSizes();
        sizes.MeasureMissing(cells.AllRequests(), measurer);
        return sizes;
    }

    /// <summary>
    /// Solves column x-positions and row y-positions (§6.2). Wrapped names and a
    /// wrapped title are re-measured through <paramref name="measurer"/>.
    /// </summary>
    public static GridLayout SolveGrid(CellSet cells, TextSizes sizes, ProjectSettings settings, ITextMeasurer measurer)
    {
        var chart = cells.Chart;
        var pad = settings.CellPadding;
        var cols = cells.Columns;
        var rowCount = cells.Rows.Count;

        // Working copies of the column texts; name cells may be swapped for wrapped requests.
        var headers = cells.Headers.ToArray();
        var rows = cells.Rows.Select(r => r.ToArray()).ToArray();

        // Columns: widest measured text, header included, + 2 × padding.
        var widths = new double[cols.Count];
        for (var c = 0; c < cols.Count; c++)
            widths[c] = ColumnTexts(headers, rows, c).Max(r => sizes[r].Width) + 2 * pad;

        // Name column (always column 0): capped at NameMaxWidth; longer names wrap
        // at NameMaxWidth − 2 × padding and are measured again.
        var wrapWidth = chart.NameMaxWidth - 2 * pad;
        if (widths[0] > chart.NameMaxWidth && wrapWidth > 0)
        {
            WrapColumn(headers, rows, 0, r => sizes[r].Width + 2 * pad > chart.NameMaxWidth, wrapWidth);
            sizes.MeasureMissing(ColumnTexts(headers, rows, 0), measurer);
            // Normally exactly NameMaxWidth. If a wrapped note is still wider (one
            // unbreakable word), the column grows so its text never crosses a line.
            widths[0] = Math.Max(chart.NameMaxWidth, ColumnTexts(headers, rows, 0).Max(r => sizes[r].Width) + 2 * pad);
        }

        var columnEdges = CumulativeEdges(widths);
        var width = columnEdges[^1] + chart.TimelineWidth;

        // Title: spans the full width and wraps when longer.
        var title = cells.Title;
        if (title is not null && sizes[title].Width + 2 * pad > width && width - 2 * pad > 0)
        {
            title = title with { WrapWidth = width - 2 * pad };
            sizes.MeasureMissing([title], measurer);
            var overflow = sizes[title].Width + 2 * pad - width;
            if (overflow > 0)
            {
                // Same safeguard as the name column: widen the table, never let text cross the border.
                widths[0] += overflow;
                columnEdges = CumulativeEdges(widths);
                width = columnEdges[^1] + chart.TimelineWidth;
            }
        }
        var tableWidth = columnEdges[^1];

        // Rows, top down (Y-up, so y decreases).
        var titleBottom = title is null ? 0.0 : -(sizes[title].Height + 2 * pad);

        // Header tiers: each tier's height = its tallest label (any candidate) + 2 × padding.
        // The column headers span both tiers; if they need more, tier 2 grows.
        double tier1Height = 0, tier2Height = 0;
        if (cells.Tiers.Count == 2)
        {
            tier1Height = TallestCandidate(cells.Tiers[0], sizes) + 2 * pad;
            tier2Height = TallestCandidate(cells.Tiers[1], sizes) + 2 * pad;
        }
        var columnHeaderHeight = headers.Max(h => sizes[h].Height) + 2 * pad;
        if (tier1Height + tier2Height < columnHeaderHeight) tier2Height = columnHeaderHeight - tier1Height;

        var tier1Bottom = titleBottom - tier1Height;
        var headerBottom = tier1Bottom - tier2Height;

        // Body rows: tallest cell + 2 × padding. Wrapped names make a row grow
        // and push everything below it down.
        var rowEdges = new double[rowCount + 1];
        rowEdges[0] = headerBottom;
        for (var i = 0; i < rowCount; i++)
            rowEdges[i + 1] = rowEdges[i] - (rows[i].Max(r => sizes[r].Height) + 2 * pad);

        // Assemble placed texts.
        var texts = new List<PlacedText>();
        if (title is not null)
            texts.Add(new PlacedText(title, sizes[title], new Box(0, titleBottom, width, 0), TextAlign.Left));
        for (var c = 0; c < cols.Count; c++)
        {
            var align = Align(cols[c]);
            texts.Add(new PlacedText(headers[c], sizes[headers[c]],
                new Box(columnEdges[c], headerBottom, columnEdges[c + 1], titleBottom), align));
            for (var i = 0; i < rowCount; i++)
                texts.Add(new PlacedText(rows[i][c], sizes[rows[i][c]],
                    new Box(columnEdges[c], rowEdges[i + 1], columnEdges[c + 1], rowEdges[i]), align));
        }

        // Timescale tiers and body ticks, mapped onto the timeline.
        var tiers = new List<TierLayout>();
        var bodyTicks = new List<double>();
        if (cells.Axis is { } axis && cells.Tiers.Count == 2)
        {
            var timeline = new TimelineMap(tableWidth, width, axis);
            tiers.Add(SolveTier(timeline, cells.Tiers[0], titleBottom, tier1Bottom, pad, sizes, texts));
            tiers.Add(SolveTier(timeline, cells.Tiers[1], tier1Bottom, headerBottom, pad, sizes, texts));
            bodyTicks.AddRange(BodyTickXs(timeline, cells.Tiers[1].Cells, tiers[1]));
        }

        var xs = new SortedSet<double>(columnEdges) { width };
        foreach (var t in tiers) xs.UnionWith(t.Dividers);
        xs.UnionWith(bodyTicks);
        var ys = new SortedSet<double>(rowEdges) { 0, titleBottom, tier1Bottom, headerBottom };

        return new GridLayout
        {
            Padding = pad,
            Width = width,
            Columns = cols,
            ColumnEdges = columnEdges,
            TitleBottom = titleBottom,
            Tier1Bottom = tier1Bottom,
            HeaderBottom = headerBottom,
            RowEdges = rowEdges,
            Axis = cells.Axis,
            Tiers = tiers,
            BodyTicks = bodyTicks,
            Texts = texts,
            GridXs = [.. xs],
            GridYs = [.. ys],
        };
    }

    /// <summary>Turns a solved grid into drawing primitives: bars, then lines, then text.</summary>
    public static IReadOnlyList<Primitive> Emit(GridLayout grid, Chart chart)
    {
        var prims = new List<Primitive>();
        var pad = grid.Padding;

        // Bars: from the left edge of the start day to the right edge of the end
        // day, inset vertically by padding inside the row (§6.2).
        if (grid.Axis is not null)
        {
            for (var i = 0; i < chart.Sections.Count; i++)
            {
                var s = chart.Sections[i];
                if (s.End < s.Start) continue; // invalid; validation blocks Generate
                var rect = new Box(
                    grid.XOfDate(s.Start),
                    grid.RowEdges[i + 1] + pad,
                    grid.XOfDate(s.End.AddDays(1)),
                    grid.RowEdges[i] - pad);
                prims.Add(new FilledRectPrimitive(rect, s.Color, s.Id));
            }
        }

        prims.AddRange(GridLines.Build(grid));

        foreach (var t in grid.Texts)
        {
            var top = t.Cell.Top - pad;
            var (anchorX, left) = t.Align == TextAlign.Left
                ? (t.Cell.Left + pad, t.Cell.Left + pad)
                : (t.Cell.Right - pad, t.Cell.Right - pad - t.Size.Width);
            prims.Add(new TextPrimitive(
                t.Request.Text, t.Request.Style, t.Align, new Point2(anchorX, top), t.Request.WrapWidth,
                new Box(left, top - t.Size.Height, left + t.Size.Width, top)));
        }

        return prims;
    }

    private static TierCells BuildTier(TimeAxis axis, TimeUnit unit, bool isTier1, ProjectSettings settings)
    {
        var cells = axis.Cells(unit);
        var candidates = cells
            .Select(c => (IReadOnlyList<TextRequest>)TimescaleLabels
                .Candidates(unit, isTier1, c.Start, settings.DateFormat)
                .Select(Header)
                .ToList())
            .ToList();
        return new TierCells(unit, cells, candidates);
    }

    /// <summary>
    /// Fits labels to one tier (§6.2).
    /// <list type="number">
    /// <item>Tick cells are merged into groups of k for each thinning step k
    /// in turn (<see cref="TimescaleLabels.ThinningSteps"/>; calendar-aligned
    /// for months, quarters and years), so every k-th tick is labelled. A group
    /// is full when it holds k whole units; partial groups (a cell clipped at an
    /// axis end, or a group cut short by an axis end or by the alignment) only
    /// occur at the ends.</item>
    /// <item>A partial group narrower than its own shortest label plus padding
    /// is a sliver. Every other group is labelled.</item>
    /// <item>Accept the first k at which some format fits every labelled group
    /// with padding on both sides (and at least one group is labelled); the
    /// whole tier then uses the longest such format. A partial group that is
    /// not a sliver always fits the shortest format, so an edge can shorten
    /// the format but never forces more thinning.</item>
    /// <item>A sliver joins its neighbour (the divider between them is
    /// dropped), so a cell overstates its period by less than one label. The
    /// label keeps its own left edge, so it still starts exactly at its date.</item>
    /// </list>
    /// A group is named by its first cell, unless that cell is a sliver clipped
    /// at the axis start and the rest of the group can hold the next cell's
    /// label: then the next cell names it, so three days of September don't
    /// name a group that is otherwise October. If a single group spanning the
    /// whole axis can't hold a label, the tier stays unlabelled. A label is only
    /// ever placed inside a cell it fits, so labels never overlap and never
    /// touch a line.
    /// </summary>
    private static TierLayout SolveTier(
        TimelineMap timeline, TierCells tier, double top, double bottom, double pad, TextSizes sizes, List<PlacedText> texts)
    {
        var n = tier.Cells.Count;
        var formats = tier.Candidates.Max(c => c.Count);
        var slots = tier.Cells.Select(c => TimescaleLabels.ThinningSlot(tier.Unit, c.Start, timeline.Axis.Start)).ToList();

        foreach (var k in TimescaleLabels.ThinningSteps(tier.Unit))
        {
            var groups = Groups(k);
            var slivers = groups.Select(g => g.Partial && g.Right - g.Left < Shortest(g.Name)).ToList();
            for (var format = 0; format < formats && slivers.Contains(false); format++)
            {
                if (groups.Where((g, i) => !slivers[i] && !Fits(g, Candidate(tier, g.Name, format))).Any())
                    continue;

                // Slivers join their neighbour; interior groups are full, so never slivers.
                var spans = groups
                    .Select((g, i) => new Span(g.Left, g.Right, g.LabelLeft, slivers[i] ? null : Candidate(tier, g.Name, format)))
                    .ToList();
                while (spans.Count > 1 && spans[0].Label is null)
                {
                    spans[1] = spans[1] with { Left = spans[0].Left };
                    spans.RemoveAt(0);
                }
                while (spans.Count > 1 && spans[^1].Label is null)
                {
                    spans[^2] = spans[^2] with { Right = spans[^1].Right };
                    spans.RemoveAt(spans.Count - 1);
                }

                foreach (var s in spans)
                    if (s.Label is { } label)
                        texts.Add(new PlacedText(label, sizes[label], new Box(s.LabelLeft, bottom, s.Right, top), TextAlign.Left));
                return new TierLayout(tier.Unit, top, bottom, k, spans.Skip(1).Select(s => s.Left).ToList());
            }

            if (groups.Count == 1)
                return new TierLayout(tier.Unit, top, bottom, k, []);
        }
        return new TierLayout(tier.Unit, top, bottom, n, []);

        bool Fits(Group g, TextRequest label) => sizes[label].Width + 2 * pad <= g.Right - g.LabelLeft;

        // Width the cell's shortest format needs, padding included.
        double Shortest(int cell) => sizes[tier.Candidates[cell][^1]].Width + 2 * pad;

        double Left(int cell) => timeline.XOfDate(tier.Cells[cell].Start);

        List<Group> Groups(int k)
        {
            var result = new List<Group>();
            for (var first = 0; first < n;)
            {
                var last = first;
                while (last + 1 < n && slots[last + 1] % k != 0) last++;
                var start = tier.Cells[first].Start;
                var end = tier.Cells[last].End;
                var right = timeline.XOfDate(end.AddDays(1));
                var partial = last - first + 1 < k
                              || TimescaleRules.UnitStart(tier.Unit, start) != start
                              || TimescaleRules.UnitEnd(tier.Unit, end) != end;
                var name = first == 0
                           && last > first
                           && TimescaleRules.UnitStart(tier.Unit, start) != start
                           && Left(1) - Left(0) < Shortest(0)
                           && right - Left(1) >= Shortest(1)
                    ? 1
                    : first;
                result.Add(new Group(name, Left(first), Left(name), right, partial));
                first = last + 1;
            }
            return result;
        }
    }

    private static TextRequest Candidate(TierCells tier, int cell, int level)
    {
        var candidates = tier.Candidates[cell];
        return candidates[Math.Min(level, candidates.Count - 1)];
    }

    /// <summary>
    /// Body grid x values: every internal tier-2 tick. When the narrowest tick
    /// cell is under <see cref="MinTickSpacing"/>, those ticks are dropped (§6.3)
    /// and the body continues the tier-2 header dividers instead, which are at
    /// least one label apart. That keeps a readable grid on huge spans.
    /// </summary>
    private static IEnumerable<double> BodyTickXs(TimelineMap timeline, IReadOnlyList<TimeCell> cells, TierLayout tier2)
    {
        var edges = cells.Select(c => timeline.XOfDate(c.Start)).Append(timeline.Right).ToList();
        for (var i = 0; i < cells.Count; i++)
            if (edges[i + 1] - edges[i] < MinTickSpacing) return tier2.Dividers;
        return edges.Skip(1).SkipLast(1);
    }

    /// <summary>
    /// Left-to-right running sums. A plain loop on purpose: <c>Enumerable.Sum</c>
    /// may vectorize and reorder additions, and layout must be bit-identical everywhere.
    /// </summary>
    private static double[] CumulativeEdges(double[] widths)
    {
        var edges = new double[widths.Length + 1];
        for (var i = 0; i < widths.Length; i++) edges[i + 1] = edges[i] + widths[i];
        return edges;
    }

    private static double TallestCandidate(TierCells tier, TextSizes sizes) =>
        tier.Candidates.SelectMany(c => c).Max(r => sizes[r].Height);

    private static IEnumerable<TextRequest> ColumnTexts(TextRequest[] headers, TextRequest[][] rows, int column) =>
        rows.Select(r => r[column]).Prepend(headers[column]);

    private static void WrapColumn(
        TextRequest[] headers, TextRequest[][] rows, int column, Func<TextRequest, bool> tooWide, double wrapWidth)
    {
        if (tooWide(headers[column])) headers[column] = headers[column] with { WrapWidth = wrapWidth };
        foreach (var row in rows)
            if (tooWide(row[column])) row[column] = row[column] with { WrapWidth = wrapWidth };
    }

    private static TextAlign Align(TableColumn column) =>
        column == TableColumn.Duration ? TextAlign.Right : TextAlign.Left;

    private static string HeaderText(TableColumn column) => column switch
    {
        TableColumn.Name => NameHeader,
        TableColumn.Start => StartHeader,
        TableColumn.End => EndHeader,
        TableColumn.Duration => DurationHeader,
        _ => throw new ArgumentOutOfRangeException(nameof(column), column, null),
    };

    private static string BodyText(Section s, TableColumn column, string dateFormat) => column switch
    {
        TableColumn.Name => s.Name,
        TableColumn.Start => DateFormats.Format(s.Start, dateFormat),
        TableColumn.End => DateFormats.Format(s.End, dateFormat),
        TableColumn.Duration => s.Duration.ToString(CultureInfo.InvariantCulture),
        _ => throw new ArgumentOutOfRangeException(nameof(column), column, null),
    };

    private static TextRequest Header(string text) => new(text, TextStyleKey.Header);

    private static TextRequest Body(string text) => new(text, TextStyleKey.Body);

    /// <summary>A group of tick cells; <see cref="Name"/> is the cell whose label it takes, starting at <see cref="LabelLeft"/>.</summary>
    private readonly record struct Group(int Name, double Left, double LabelLeft, double Right, bool Partial);

    /// <summary>A solved tier cell: <see cref="LabelLeft"/> is where its label's cell starts (≥ <see cref="Left"/>).</summary>
    private readonly record struct Span(double Left, double Right, double LabelLeft, TextRequest? Label);
}
