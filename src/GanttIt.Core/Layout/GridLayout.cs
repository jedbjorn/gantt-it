using GanttIt.Core.Timeline;

namespace GanttIt.Core.Layout;

/// <summary>A measured text assigned to its cell, ready for anchoring.</summary>
public sealed record PlacedText(TextRequest Request, TextSize Size, Box Cell, TextAlign Align);

/// <summary>
/// One solved timescale tier. Neighbouring tick cells may be merged into
/// groups of <see cref="GroupSize"/> when their labels don't fit (§6.2);
/// <see cref="Dividers"/> are the x values between groups, where a line is drawn.
/// </summary>
public sealed record TierLayout(
    TimeUnit Unit,
    double Top,
    double Bottom,
    int GroupSize,
    IReadOnlyList<double> Dividers);

/// <summary>
/// Output of <see cref="LayoutEngine.SolveGrid"/>: every x and y of the chart's
/// grid, in feet, legend coordinates (origin top-left of the border, Y up).
/// Emit reads positions only from here, so every line endpoint is exactly a
/// grid value.
/// </summary>
public sealed class GridLayout
{
    public required double Padding { get; init; }

    /// <summary>Full chart width: table plus timeline. The right border is at X = Width.</summary>
    public required double Width { get; init; }

    public required IReadOnlyList<TableColumn> Columns { get; init; }

    /// <summary><c>Columns.Count + 1</c> x values from 0 to <see cref="TimelineLeft"/>.</summary>
    public required IReadOnlyList<double> ColumnEdges { get; init; }

    public double TimelineLeft => ColumnEdges[^1];

    /// <summary>Bottom of the title row; 0 when there is no title.</summary>
    public required double TitleBottom { get; init; }

    /// <summary>Bottom of the tier-1 row (equals <see cref="TitleBottom"/> without an axis).</summary>
    public required double Tier1Bottom { get; init; }

    /// <summary>Bottom of the header (tier 2 and the column headers).</summary>
    public required double HeaderBottom { get; init; }

    /// <summary>
    /// <c>Sections.Count + 1</c> y values: <c>RowEdges[i]</c> is the top of body
    /// row i and <c>RowEdges[i + 1]</c> its bottom. <c>RowEdges[0] = HeaderBottom</c>.
    /// </summary>
    public required IReadOnlyList<double> RowEdges { get; init; }

    /// <summary>The bottom border (≤ 0).</summary>
    public double Bottom => RowEdges[^1];

    public required TimeAxis? Axis { get; init; }

    /// <summary>Tier 1 then tier 2; empty without an axis.</summary>
    public required IReadOnlyList<TierLayout> Tiers { get; init; }

    /// <summary>
    /// X values of the vertical grid lines drawn through the body: every tier-2
    /// tick, or, when ticks would be closer than <see cref="LayoutEngine.MinTickSpacing"/>
    /// (§6.3), only the tier-2 label-group dividers.
    /// </summary>
    public required IReadOnlyList<double> BodyTicks { get; init; }

    public required IReadOnlyList<PlacedText> Texts { get; init; }

    /// <summary>Every x a line may end on, ascending.</summary>
    public required IReadOnlyList<double> GridXs { get; init; }

    /// <summary>Every y a line may end on, ascending.</summary>
    public required IReadOnlyList<double> GridYs { get; init; }

    /// <summary>
    /// X of the left edge of <paramref name="date"/> on the timeline; the day
    /// after the axis end maps to the right edge. Day width is
    /// <c>TimelineWidth / Days</c> (§6.2), and the axis ends map exactly onto
    /// the timeline edges, so ticks, bars and the border share identical values.
    /// </summary>
    public double XOfDate(DateOnly date) =>
        Axis is null
            ? throw new InvalidOperationException("The chart has no time axis.")
            : new TimelineMap(TimelineLeft, Width, Axis).XOfDate(date);
}
