using GanttIt.Core.Timeline;

namespace GanttIt.Core.Layout;

/// <summary>
/// Maps days on the axis to x. Day width is <c>(Right − Left) / Days</c> (§6.2).
/// Both ends map exactly onto <see cref="Left"/> and <see cref="Right"/>, so
/// ticks, bars and the border share identical values, and the same inputs
/// always give the same doubles.
/// </summary>
internal readonly record struct TimelineMap(double Left, double Right, TimeAxis Axis)
{
    /// <summary>X of the left edge of the day <paramref name="dayOffset"/> days after the axis start.</summary>
    public double XOfDay(int dayOffset)
    {
        if (dayOffset <= 0) return Left;
        if (dayOffset >= Axis.Days) return Right;
        return Left + (Right - Left) * dayOffset / Axis.Days;
    }

    /// <summary>X of the left edge of <paramref name="date"/>.</summary>
    public double XOfDate(DateOnly date) => XOfDay(Axis.OffsetOf(date));
}
