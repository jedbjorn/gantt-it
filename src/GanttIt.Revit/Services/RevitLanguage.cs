using Autodesk.Revit.ApplicationServices;
using GanttIt.Core.Localization;

namespace GanttIt.Revit.Services;

/// <summary>
/// Sets Gantt-It's UI language from Revit's (spec §2.6, decision #22): French, German
/// and Spanish Revit show Gantt-It in that language, every other Revit language in
/// English. Only <see cref="UiLanguage"/> changes; the thread cultures Revit and other
/// add-ins rely on are never touched.
/// </summary>
internal static class RevitLanguage
{
#if DEBUG
    /// <summary>
    /// Debug builds only: <c>GANTTIT_UI_LANG=en|fr|es|de</c> overrides Revit's language,
    /// so each language can be captured on a machine without the Revit language pack.
    /// </summary>
    public const string OverrideVariable = "GANTTIT_UI_LANG";
#endif

    /// <summary>Applies the UI language for <paramref name="revitLanguage"/> and returns it.</summary>
    public static Language Apply(LanguageType revitLanguage)
    {
        var language = Languages.FromRevitLanguage(revitLanguage.ToString());
#if DEBUG
        if (Languages.TryParseCode(Environment.GetEnvironmentVariable(OverrideVariable), out var forced)) language = forced;
#endif
        UiLanguage.Current = language;
        return language;
    }

    /// <summary>As <see cref="Apply(LanguageType)"/>, never throwing: a failure leaves the current language.</summary>
    public static void TryApply(Func<LanguageType> read)
    {
        try
        {
            Apply(read());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Gantt-It: could not read Revit's language: {ex.Message}");
        }
    }
}
