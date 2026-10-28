using GanttIt.Core.Model;

namespace GanttIt.Core.Serialization;

/// <summary>
/// The copy rule (§7.2): a chart legend duplicated in Revit carries the same
/// <see cref="Chart.Id"/>. On load the later one is a copy and gets a new Id;
/// its stored GeneratedIds no longer describe its own elements, so the Revit
/// side regenerates it on its first edit.
/// </summary>
public static class ChartCopies
{
    /// <summary>Gives <paramref name="chart"/> a fresh Id and returns it.</summary>
    public static Guid AssignNewId(Chart chart, Func<Guid>? newId = null)
    {
        chart.Id = (newId ?? Guid.NewGuid)();
        return chart.Id;
    }

    /// <summary>
    /// Walks <paramref name="chartsInOrder"/> (the caller decides "later",
    /// e.g. by ascending ElementId) and gives every chart whose Id was already
    /// seen a new Id. Returns the charts that were reassigned, in order.
    /// </summary>
    public static IReadOnlyList<Chart> ReassignDuplicateIds(IEnumerable<Chart> chartsInOrder, Func<Guid>? newId = null)
    {
        var seen = new HashSet<Guid>();
        var reassigned = new List<Chart>();
        foreach (var chart in chartsInOrder)
        {
            if (seen.Add(chart.Id)) continue;
            seen.Add(AssignNewId(chart, newId));
            reassigned.Add(chart);
        }
        return reassigned;
    }
}
