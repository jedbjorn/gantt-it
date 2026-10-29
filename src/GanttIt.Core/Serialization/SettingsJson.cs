using GanttIt.Core.Dates;
using GanttIt.Core.Model;
using GanttIt.Core.Resources;

namespace GanttIt.Core.Serialization;

/// <summary>
/// JSON for <see cref="ProjectSettings"/>, with the same version rules as <see cref="ChartJson"/>.
/// </summary>
/// <remarks>
/// <c>chartLanguage</c> (§2.6) joined schema 1 as an optional field, without a version
/// bump: a document without it reads as English, the language it was drawn in, and an
/// older add-in that reads a document with it ignores the field. Each language is
/// stored by its enum name; a name this version doesn't know makes the document
/// invalid, so a later version that adds a language must also raise
/// <see cref="SchemaVersions.Settings"/>, which keeps its documents read-only here.
/// </remarks>
public static class SettingsJson
{
    /// <summary>Serializes settings; refuses a newer SchemaVersion like <see cref="ChartJson.Serialize"/>.</summary>
    public static string Serialize(ProjectSettings settings) =>
        JsonStore.Serialize(settings, settings.SchemaVersion, SchemaVersions.Settings, "settings document");

    /// <summary>Reads settings. Never throws.</summary>
    public static LoadResult<ProjectSettings> Deserialize(string? json) =>
        JsonStore.Deserialize<ProjectSettings>(json, SchemaVersions.Settings, Problem);

    private static string? Problem(ProjectSettings settings)
    {
        // Layout formats every date with this pattern, so a bad one must not load.
        if (!DateFormats.IsValidPattern(settings.DateFormat, out var dateError)) return dateError;
        if (settings.Palette is null) return CoreStrings.Settings_NoPalette;
        if (!double.IsFinite(settings.CellPadding)) return CoreStrings.Settings_NonFinitePadding;
        if (!Enum.IsDefined(settings.ChartLanguage)) return CoreStrings.Settings_UnknownLanguage;
        foreach (var style in new[] { settings.TitleTextStyle, settings.HeaderTextStyle, settings.BodyTextStyle, settings.ChartLineStyle, settings.HeaderLineStyle })
            if (style is not null && (style.UniqueId is null || style.Name is null))
                return CoreStrings.Settings_StyleIncomplete;
        return null;
    }
}
