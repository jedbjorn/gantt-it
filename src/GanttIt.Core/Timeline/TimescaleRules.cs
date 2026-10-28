using GanttIt.Core.Model;

namespace GanttIt.Core.Timeline;

public enum TimeUnit
{
    Day,
    Week,
    Month,
    Quarter,
    Year,
}

/// <summary>The two header tiers: <see cref="Tier1"/> is the coarse top row, <see cref="Tier2"/> the fine row.</summary>
public readonly record struct TierPair(TimeUnit Tier1, TimeUnit Tier2);

/// <summary>
/// Timescale selection (§5.3) and calendar-unit arithmetic. Weeks run Monday
/// to Sunday (ISO 8601), independent of the machine's culture.
/// </summary>
public static class TimescaleRules
{
    /// <summary>"≤ 6 weeks" is read as a span of at most 42 calendar days.</summary>
    public const int MaxDaysForDays = 42;

    /// <summary>"≤ 9 months" in calendar months (see <see cref="ResolveAuto"/>).</summary>
    public const int MaxMonthsForWeeks = 9;

    /// <summary>"≤ 4 years" in calendar years (see <see cref="ResolveAuto"/>).</summary>
    public const int MaxYearsForMonths = 4;

    /// <summary>Tiers for a chart setting; explicit values name the tier-2 unit.</summary>
    public static TierPair Resolve(Timescale timescale, DateOnly first, DateOnly last) => timescale switch
    {
        Timescale.Auto => ResolveAuto(first, last),
        Timescale.Days => new(TimeUnit.Week, TimeUnit.Day),
        Timescale.Weeks => new(TimeUnit.Month, TimeUnit.Week),
        Timescale.Months => new(TimeUnit.Year, TimeUnit.Month),
        Timescale.Quarters => new(TimeUnit.Year, TimeUnit.Quarter),
        _ => throw new ArgumentOutOfRangeException(nameof(timescale), timescale, null),
    };

    /// <summary>
    /// The Auto rule (§5.3) over the inclusive span <paramref name="first"/>..<paramref name="last"/>.
    /// <list type="bullet">
    /// <item>≤ 6 weeks: at most 42 days inclusive → Weeks / Days.</item>
    /// <item>≤ 9 months: <c>last &lt; first.AddMonths(9)</c> → Months / Weeks. So 1 Jan..30 Sep is
    /// "9 months" and 1 Jan..1 Oct is not. Month-end starts clamp (31 Jan + 9 months = 31 Oct).</item>
    /// <item>≤ 4 years: <c>last &lt; first.AddYears(4)</c> → Years / Months. So 1 Jan 2026..31 Dec 2029
    /// is "4 years" and ..1 Jan 2030 is not.</item>
    /// <item>Otherwise → Years / Quarters.</item>
    /// </list>
    /// Calendar-based boundaries were chosen over fixed day counts so a span
    /// that reads as "9 months" or "4 years" on a calendar lands in that bucket.
    /// </summary>
    public static TierPair ResolveAuto(DateOnly first, DateOnly last)
    {
        if (last < first) (first, last) = (last, first);
        var days = last.DayNumber - first.DayNumber + 1;
        if (days <= MaxDaysForDays) return new(TimeUnit.Week, TimeUnit.Day);
        if (last < first.AddMonths(MaxMonthsForWeeks)) return new(TimeUnit.Month, TimeUnit.Week);
        if (last < first.AddYears(MaxYearsForMonths)) return new(TimeUnit.Year, TimeUnit.Month);
        return new(TimeUnit.Year, TimeUnit.Quarter);
    }

    /// <summary>First day of the unit containing <paramref name="date"/>.</summary>
    public static DateOnly UnitStart(TimeUnit unit, DateOnly date) => unit switch
    {
        TimeUnit.Day => date,
        TimeUnit.Week => date.AddDays(-(((int)date.DayOfWeek + 6) % 7)), // Monday
        TimeUnit.Month => new DateOnly(date.Year, date.Month, 1),
        TimeUnit.Quarter => new DateOnly(date.Year, (date.Month - 1) / 3 * 3 + 1, 1),
        TimeUnit.Year => new DateOnly(date.Year, 1, 1),
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, null),
    };

    /// <summary>First day of the unit after the one containing <paramref name="date"/>.</summary>
    public static DateOnly NextUnitStart(TimeUnit unit, DateOnly date)
    {
        var start = UnitStart(unit, date);
        return unit switch
        {
            TimeUnit.Day => start.AddDays(1),
            TimeUnit.Week => start.AddDays(7),
            TimeUnit.Month => start.AddMonths(1),
            TimeUnit.Quarter => start.AddMonths(3),
            TimeUnit.Year => start.AddYears(1),
            _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, null),
        };
    }

    /// <summary>Last day (inclusive) of the unit containing <paramref name="date"/>.</summary>
    public static DateOnly UnitEnd(TimeUnit unit, DateOnly date) => NextUnitStart(unit, date).AddDays(-1);
}
