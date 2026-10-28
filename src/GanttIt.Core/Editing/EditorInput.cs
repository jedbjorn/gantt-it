using System.Globalization;
using GanttIt.Core.Model;

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
            error = "The duration must be a whole number of days.";
            return false;
        }
        if (value < 1)
        {
            error = "The duration must be at least 1 day.";
            return false;
        }
        if (value > MaxDurationDays)
        {
            error = $"The duration can be at most {MaxDurationDays.ToString(CultureInfo.InvariantCulture)} days.";
            return false;
        }
        days = value;
        return true;
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
            : $"Dates must be between {SupportedDates.Min.Year} and {SupportedDates.Max.Year}.";
        return error is null;
    }
}
