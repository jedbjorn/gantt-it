using GanttIt.Core.Model;

namespace GanttIt.Core.Tests.Support;

/// <summary>Deterministic chart builders.</summary>
public static class TestCharts
{
    public static Guid Id(int n) => new(n, 0, 0, new byte[8]);

    public static DateOnly D(int year, int month, int day) => new(year, month, day);

    public static Section Section(int n, DateOnly start, DateOnly end, string? name = null) => new()
    {
        Id = Id(n),
        Name = name ?? $"Section {n}",
        Start = start,
        End = end,
        Color = Palette.Default[(n - 1) % Palette.Default.Count],
    };

    /// <summary>
    /// <paramref name="count"/> sections of <paramref name="days"/> days each,
    /// separated by <paramref name="gap"/> empty days, starting on <paramref name="start"/>.
    /// </summary>
    public static Chart Sequential(int count, DateOnly start, int days = 10, int gap = 0)
    {
        var chart = Chart.Create("Test chart", Id(1000));
        var cursor = start;
        for (var i = 1; i <= count; i++)
        {
            chart.Sections.Add(Section(i, cursor, cursor.AddDays(days - 1)));
            cursor = cursor.AddDays(days + gap);
        }
        return chart;
    }

    /// <summary>Sections spread evenly from <paramref name="first"/> to <paramref name="last"/>.</summary>
    public static Chart Spanning(int count, DateOnly first, DateOnly last, Timescale timescale = Timescale.Auto)
    {
        var chart = Chart.Create("Span chart", Id(2000));
        chart.Timescale = timescale;
        var span = last.DayNumber - first.DayNumber + 1;
        for (var i = 0; i < count; i++)
        {
            var s = first.AddDays((int)((long)span * i / count));
            var e = first.AddDays((int)((long)span * (i + 1) / count) - 1);
            if (e < s) e = s;
            chart.Sections.Add(Section(i + 1, s, e));
        }
        return chart;
    }

    public static ProjectSettings Settings() => new();
}
