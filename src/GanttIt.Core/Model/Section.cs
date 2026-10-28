namespace GanttIt.Core.Model;

/// <summary>One bar of the chart (§3). Dates are calendar days; <see cref="End"/> is inclusive.</summary>
public sealed class Section
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public DateOnly Start { get; set; }

    public DateOnly End { get; set; }

    public Rgb Color { get; set; }

    /// <summary>
    /// <c>End − Start + 1</c>. A valid section has a duration of at least 1;
    /// a smaller value means End is before Start, which validation reports.
    /// </summary>
    public int Duration => End.DayNumber - Start.DayNumber + 1;
}
