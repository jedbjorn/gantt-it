namespace GanttIt.Core.Model;

/// <summary>
/// The date range charts may use. Wide enough for any real schedule, and it
/// keeps every calendar computation (padding the axis to whole units) far
/// from <see cref="DateOnly.MinValue"/> and <see cref="DateOnly.MaxValue"/>.
/// </summary>
public static class SupportedDates
{
    public static readonly DateOnly Min = new(1900, 1, 1);

    public static readonly DateOnly Max = new(2199, 12, 31);

    public static bool Contains(DateOnly date) => date >= Min && date <= Max;
}
