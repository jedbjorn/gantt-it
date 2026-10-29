using System.Globalization;
using GanttIt.Core.Localization;
using GanttIt.Core.Model;
using GanttIt.Core.Resources;

namespace GanttIt.Core.Editing;

/// <summary>Small input rules of the chart editor (§2.2), kept out of the WPF layer so they are tested.</summary>
public static class EditorInput
{
    /// <summary>The longest duration that fits inside <see cref="SupportedDates"/>.</summary>
    public static readonly int MaxDurationDays = SupportedDates.Max.DayNumber - SupportedDates.Min.DayNumber + 1;

    /// <summary>Parses a typed duration: a whole number of days, at least 1.</summary>
    public static bool TryParseDuration(string? text, out int days, out string? error)
    {
        days = 0;
        error = null;
        if (!int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            error = CoreStrings.Duration_NotWhole;
            return false;
        }
        if (value < 1)
        {
            error = CoreStrings.Duration_TooShort;
            return false;
        }
        if (value > MaxDurationDays)
        {
            error = CoreStrings.Duration_TooLong(MaxDurationDays.ToString(CultureInfo.InvariantCulture));
            return false;
        }
        days = value;
        return true;
    }

    /// <summary>
    /// The default name of a new chart in the chart <paramref name="language"/>:
    /// "Gantt Chart 1", "Gantt Chart 2", … ("Diagramme de Gantt 1", "Gantt-Diagramm 1"…)
    /// — the first not used by any view, compared the way <c>ChartValidator</c> does
    /// (trimmed, case-insensitive), so a new chart never starts with a name error.
    /// </summary>
    public static string UniqueChartName(IEnumerable<string> viewNames, Language language = Language.English)
    {
        var text = ChartText.For(language);
        var taken = new HashSet<string>(viewNames.Select(n => n.Trim()), StringComparer.OrdinalIgnoreCase);
        for (var i = 1; ; i++)
        {
            var name = text.DefaultChartName(i);
            if (!taken.Contains(name)) return name;
        }
    }

    /// <summary>
    /// The title defaults to the name (§2.2): while the title still equals the
    /// old name, it follows a rename; once the user has typed their own title it stays.
    /// </summary>
    public static string TitleAfterRename(string oldName, string newName, string currentTitle) =>
        string.Equals(currentTitle, oldName, StringComparison.Ordinal) ? newName : currentTitle;

    /// <summary>
    /// Where a dragged row lands. <paramref name="overIndex"/> is the row under
    /// the pointer and <paramref name="dropAfter"/> whether the pointer is in its
    /// lower half. Returns the index to pass to <c>Scheduler.Move</c>, which
    /// removes the row first and then inserts it.
    /// </summary>
    public static int DropIndex(int fromIndex, int overIndex, bool dropAfter, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fromIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(fromIndex, count);
        var insertBefore = Math.Clamp(overIndex + (dropAfter ? 1 : 0), 0, count);
        var target = insertBefore > fromIndex ? insertBefore - 1 : insertBefore;
        return Math.Clamp(target, 0, count - 1);
    }

    /// <summary>
    /// True when a picked or typed date is inside <see cref="SupportedDates"/>;
    /// otherwise <paramref name="error"/> is the inline notice to show.
    /// </summary>
    public static bool IsSupportedDate(DateOnly date, out string? error)
    {
        error = SupportedDates.Contains(date)
            ? null
            : CoreStrings.Date_OutOfRange(SupportedDates.Min.Year, SupportedDates.Max.Year);
        return error is null;
    }
}
