using GanttIt.Core.Model;

namespace GanttIt.Revit.UI.Editor;

/// <summary>The date pickers' range: <see cref="SupportedDates"/> as <see cref="DateTime"/> for XAML.</summary>
public static class EditorDates
{
    public static readonly DateTime Min = SupportedDates.Min.ToDateTime(TimeOnly.MinValue);

    public static readonly DateTime Max = SupportedDates.Max.ToDateTime(TimeOnly.MinValue);
}
