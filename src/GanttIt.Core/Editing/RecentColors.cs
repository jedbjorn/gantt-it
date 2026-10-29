using GanttIt.Core.Model;

namespace GanttIt.Core.Editing;

/// <summary>
/// The color picker's recent-colors row (§2.4): most recent first, no
/// duplicates, at most <see cref="Capacity"/> entries.
/// </summary>
public sealed class RecentColors
{
    private readonly List<Rgb> _colors = [];

    public RecentColors(int capacity = 12)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        Capacity = capacity;
    }

    public int Capacity { get; }

    /// <summary>Most recent first.</summary>
    public IReadOnlyList<Rgb> Items => _colors;

    /// <summary>Moves <paramref name="color"/> to the front, dropping the oldest entry when full.</summary>
    public void Add(Rgb color)
    {
        _colors.Remove(color);
        _colors.Insert(0, color);
        if (_colors.Count > Capacity) _colors.RemoveAt(_colors.Count - 1);
    }
}
