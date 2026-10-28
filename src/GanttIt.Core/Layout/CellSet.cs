using GanttIt.Core.Model;
using GanttIt.Core.Timeline;

namespace GanttIt.Core.Layout;

/// <summary>Table columns, left to right. <see cref="Name"/> is always present.</summary>
public enum TableColumn
{
    Name,
    Start,
    End,
    Duration,
}

/// <summary>
/// One timescale tier before layout: its cells and, per cell, the label
/// candidates longest first (<see cref="TimescaleLabels"/>).
/// </summary>
public sealed record TierCells(
    TimeUnit Unit,
    IReadOnlyList<TimeCell> Cells,
    IReadOnlyList<IReadOnlyList<TextRequest>> Candidates);

/// <summary>
/// Output of <see cref="LayoutEngine.BuildCells"/>: every string the chart may
/// draw, with its style, before anything is measured or positioned.
/// </summary>
public sealed class CellSet
{
    public required Chart Chart { get; init; }

    /// <summary>The title, or null when the chart title is blank.</summary>
    public required TextRequest? Title { get; init; }

    public required IReadOnlyList<TableColumn> Columns { get; init; }

    /// <summary>Column header text, parallel to <see cref="Columns"/>.</summary>
    public required IReadOnlyList<TextRequest> Headers { get; init; }

    /// <summary>Body text per section (outer) and column (inner, parallel to <see cref="Columns"/>).</summary>
    public required IReadOnlyList<IReadOnlyList<TextRequest>> Rows { get; init; }

    /// <summary>The time axis, or null for a chart with no sections.</summary>
    public required TimeAxis? Axis { get; init; }

    /// <summary>Tier 1 then tier 2; empty when <see cref="Axis"/> is null.</summary>
    public required IReadOnlyList<TierCells> Tiers { get; init; }

    /// <summary>Every request in the set, in a stable order (may repeat).</summary>
    public IEnumerable<TextRequest> AllRequests()
    {
        if (Title is not null) yield return Title;
        foreach (var h in Headers) yield return h;
        foreach (var row in Rows)
            foreach (var cell in row) yield return cell;
        foreach (var tier in Tiers)
            foreach (var candidates in tier.Candidates)
                foreach (var c in candidates) yield return c;
    }
}

/// <summary>
/// Measured sizes keyed by request. Each distinct request is measured once,
/// across every batch sent to the measurer.
/// </summary>
public sealed class TextSizes
{
    private readonly Dictionary<TextRequest, TextSize> _sizes = [];

    public int Count => _sizes.Count;

    public TextSize this[TextRequest request] => _sizes[request];

    /// <summary>Measures the requests not yet known, in one batch. No call when all are known.</summary>
    public void MeasureMissing(IEnumerable<TextRequest> requests, ITextMeasurer measurer)
    {
        var missing = requests.Where(r => !_sizes.ContainsKey(r)).Distinct().ToList();
        if (missing.Count == 0) return;

        var sizes = measurer.Measure(missing);
        if (sizes.Count != missing.Count)
            throw new InvalidOperationException(
                $"The text measurer returned {sizes.Count} sizes for {missing.Count} requests.");
        for (var i = 0; i < missing.Count; i++) _sizes[missing[i]] = sizes[i];
    }
}
