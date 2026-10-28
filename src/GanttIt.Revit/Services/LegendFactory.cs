using Autodesk.Revit.DB;

namespace GanttIt.Revit.Services;

/// <summary>
/// Creates chart legends (§7.1, D9). The Revit API cannot create a legend from
/// scratch, so an existing legend is duplicated with
/// <see cref="ViewDuplicateOption.Duplicate"/> (which copies no annotation, V3) and
/// then renamed and set to 1:1. Legends can't be duplicated AsDependent.
/// </summary>
internal static class LegendFactory
{
    /// <summary>
    /// A legend to duplicate, or null when the model has none (the UI then shows the
    /// explanatory modal, §7.1). Plain legends (detached copies included) are
    /// preferred; live chart legends also serve.
    /// </summary>
    public static View? FindSource(Document doc)
    {
        var legends = RevitUtil.Legends(doc).Where(v => v.CanViewBeDuplicated(ViewDuplicateOption.Duplicate)).ToList();
        return legends.FirstOrDefault(v => ChartStore.ReadLive(v) is null) ?? legends.FirstOrDefault();
    }

    /// <summary>
    /// Duplicates <paramref name="source"/> into a new empty legend named
    /// <paramref name="name"/> at scale 1:1. A chart entity inherited from a chart
    /// legend source is removed at once; the caller writes the new chart's entity
    /// in the same transaction. Must run inside a transaction.
    /// </summary>
    public static View Create(Document doc, View source, string name)
    {
        var id = source.Duplicate(ViewDuplicateOption.Duplicate);
        var view = (View)doc.GetElement(id);
        ChartStore.Clear(view);
        if (view.ViewTemplateId != ElementId.InvalidElementId) view.ViewTemplateId = ElementId.InvalidElementId;
        view.Name = name.Trim();
        EnsureScaleOne(view);
        return view;
    }

    /// <summary>§6.4: the chart legend is drawn at 1:1, so model feet equal paper feet.</summary>
    public static void EnsureScaleOne(View view)
    {
        if (view.Scale != 1) view.Scale = 1;
    }
}
