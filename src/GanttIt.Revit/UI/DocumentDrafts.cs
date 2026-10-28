using Autodesk.Revit.DB;
using GanttIt.Core.Model;

namespace GanttIt.Revit.UI;

/// <summary>Where the editor parks an ungenerated chart while the window is closed (§7.1).</summary>
public interface IDraftStore
{
    /// <summary>The parked chart for this document, or null.</summary>
    Chart? Load();

    void Save(Chart chart);

    void Clear();
}

/// <summary>
/// The §7.1 no-legend path. Revit is blocked while the modal command runs (D7),
/// so the user must close Gantt-It to create a legend by hand. The chart being
/// edited is parked here, in memory for this Revit session only, keyed by the
/// open <see cref="Document"/>, and the editor reopens with it. A draft whose
/// document was closed in the meantime is dropped. Nothing goes to disk or the model.
/// </summary>
public sealed class DocumentDrafts(Document document) : IDraftStore
{
    private static readonly List<(Document Document, Chart Chart)> Drafts = [];

    public Chart? Load()
    {
        Purge();
        var index = Find();
        return index >= 0 ? Drafts[index].Chart.Clone() : null;
    }

    public void Save(Chart chart)
    {
        Purge();
        var index = Find();
        if (index >= 0) Drafts.RemoveAt(index);
        Drafts.Add((document, chart.Clone()));
    }

    public void Clear()
    {
        Purge();
        var index = Find();
        if (index >= 0) Drafts.RemoveAt(index);
    }

    /// <summary>
    /// Same document: Revit's own equality, or, should a new wrapper object come back
    /// for the same open document, the same path and title (two open documents never share both).
    /// </summary>
    private int Find() => Drafts.FindIndex(d =>
        d.Document.Equals(document)
        || (d.Document.PathName == document.PathName && d.Document.Title == document.Title));

    /// <summary>A closed document's object becomes invalid; its draft goes with it.</summary>
    private static void Purge() => Drafts.RemoveAll(d => !IsOpen(d.Document));

    private static bool IsOpen(Document doc)
    {
        try
        {
            return doc.IsValidObject;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
