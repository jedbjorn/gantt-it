#if DEBUG
using GanttIt.Core.Model;
using GanttIt.Core.Scheduling;

namespace GanttIt.Revit.SelfTest;

/// <summary>
/// The Self-Test's fixture charts (§8.1): a short days chart, a months chart, a
/// multi-year chart, wrapped long names (and a wrapping title), a chart with only
/// the Right-aligned Duration column, and one large chart with hundreds of
/// sections. Together they cover several colors, links with and without lag, an
/// overlap, a name with a line break and very short bars on a long axis.
/// </summary>
internal static class Fixtures
{
    public const string Days = "GI Days";
    public const string Months = "GI Months";
    public const string MultiYear = "GI Multi-year";
    public const string Wrapped = "GI Wrapped";
    public const string DurationOnly = "GI Duration only";
    public const string Large = "GI Large";

    /// <summary>Every fixture, freshly built (new Ids each call).</summary>
    public static List<Chart> All() => [BuildDays(), BuildMonths(), BuildMultiYear(), BuildWrapped(), BuildDurationOnly(), BuildLarge()];

    public static Chart BuildDays()
    {
        var chart = Chart.Create(Days);
        chart.Title = "Site mobilisation — first weeks";
        var b = new Builder(chart, new DateOnly(2026, 11, 2));
        b.Add("Kickoff", 3);
        b.Add("Survey", 5, linked: true);
        b.Add("Concept", 7, gap: 2);
        b.Add("Review", 2, linked: true);
        b.Add("Sign-off", 1, linked: true, lag: 3);
        return chart;
    }

    public static Chart BuildMonths()
    {
        var chart = Chart.Create(Months);
        chart.Title = "North wing — design and permitting";
        var b = new Builder(chart, new DateOnly(2026, 10, 1));
        b.Add("Design", 46);
        b.Add("Permitting & Approvals", 56, linked: true);
        b.Add("Tender", 21, gap: 5);
        b.Add("Contract award", 7, linked: true, lag: 2);
        b.Add("Mobilisation", 10, gap: -3);          // overlaps the previous section
        b.Add("Groundworks", 120, linked: true, lag: -2);
        b.Add("Utilities", 14, gap: -100);
        b.Add("Handover prep", 9, gap: 20, color: Rgb.Parse("#2A9D8F"));
        return chart;
    }

    public static Chart BuildMultiYear()
    {
        var chart = Chart.Create(MultiYear);
        chart.Title = "Campus programme 2026–2029";
        var b = new Builder(chart, new DateOnly(2026, 3, 2));
        b.Add("Feasibility", 90);
        b.Add("Masterplan", 150, linked: true);
        b.Add("Phase 1 design", 200, linked: true, lag: 10);
        b.Add("Phase 1 construction", 400, linked: true);
        b.Add("Phase 2 design", 180, gap: -300);
        b.Add("Phase 2 construction", 330, linked: true);
        b.Add("Landscaping", 120, gap: -60);
        b.Add("Commissioning", 45, linked: true);
        b.Add("Defects period", 60, linked: true);
        b.Add("Close-out", 14, linked: true, lag: 5);
        return chart;
    }

    public static Chart BuildWrapped()
    {
        var chart = Chart.Create(Wrapped);
        chart.Title = "Fit-out programme for the north wing extension, phase two, including the permitting, "
                      + "approvals, enabling works and the full sequence of trades from first fix to final handover";
        chart.NameMaxWidth = Units.FromMillimeters(40);
        var b = new Builder(chart, new DateOnly(2027, 1, 4));
        b.Add("Permitting and approvals for the north wing extension, phase two", 30);
        b.Add("Enabling works", 14, linked: true);
        b.Add("Structural steel frame erection and temporary propping of the existing slab edges", 40, linked: true);
        b.Add("Fit-out\nlevels 1–3", 35, gap: -10);
        b.Add("Mechanical, electrical and plumbing first fix, all floors, with coordination of penetrations", 45, linked: true);
        b.Add("Handover", 5, gap: 3);
        return chart;
    }

    public static Chart BuildDurationOnly()
    {
        var chart = Chart.Create(DurationOnly);
        chart.Title = "Durations only, weekly timescale";
        chart.Columns = ChartColumns.Duration;
        chart.Timescale = Timescale.Weeks;
        var b = new Builder(chart, new DateOnly(2026, 6, 1));
        b.Add("Strip-out", 12);
        b.Add("Structure", 33, linked: true);
        b.Add("Envelope", 26, gap: -5);
        b.Add("Services", 104, linked: true, lag: 1);
        return chart;
    }

    /// <summary>300 sections over 12 years: Years / Quarters, thinned labels, widened one-day bars.</summary>
    public static Chart BuildLarge()
    {
        var chart = Chart.Create(Large);
        chart.Title = "Portfolio — 300 work packages";
        var first = new DateOnly(2026, 1, 5);
        var span = new DateOnly(2037, 12, 31).DayNumber - first.DayNumber;
        for (var i = 0; i < 300; i++)
        {
            var start = first.AddDays((int)((long)span * i / 300));
            var days = i % 50 == 7 ? 1 : 20 + (i * 37 % 400);
            var end = start.AddDays(days - 1);
            if (end.DayNumber > first.DayNumber + span) end = first.AddDays(span);
            chart.Sections.Add(new Section
            {
                Id = Guid.NewGuid(),
                Name = $"WP-{i + 1:000} {Words[i % Words.Length]}",
                Start = start,
                End = end,
                Color = Palette.Default[i % Palette.Default.Count],
            });
        }
        return chart;
    }

    private static readonly string[] Words =
    [
        "Survey", "Design", "Approvals", "Procurement", "Enabling", "Substructure", "Frame", "Envelope",
        "Roofing", "MEP first fix", "Partitions", "MEP second fix", "Finishes", "Commissioning", "Handover",
    ];

    /// <summary>Appends sections in order; a link records the gap as its lag (§5.1).</summary>
    private sealed class Builder(Chart chart, DateOnly start)
    {
        private DateOnly _cursor = start;

        public void Add(string name, int days, bool linked = false, int lag = 0, int gap = 0, Rgb? color = null)
        {
            var offset = linked ? lag : gap;
            var s = chart.Sections.Count == 0 ? _cursor : _cursor.AddDays(offset);
            var section = new Section
            {
                Id = Guid.NewGuid(),
                Name = name,
                Start = s,
                End = s.AddDays(days - 1),
                Color = color ?? Palette.Default[chart.Sections.Count % Palette.Default.Count],
            };
            chart.Sections.Add(section);
            if (linked && chart.Sections.Count > 1) Scheduler.SetLink(chart, chart.Sections.Count - 2, true);
            _cursor = section.End.AddDays(1);
        }
    }
}
#endif
