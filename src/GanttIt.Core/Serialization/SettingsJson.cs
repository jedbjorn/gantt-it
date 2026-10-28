using GanttIt.Core.Model;

namespace GanttIt.Core.Serialization;

/// <summary>JSON for <see cref="ProjectSettings"/>, with the same version rules as <see cref="ChartJson"/>.</summary>
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
        if (settings.DateFormat is null) return "The settings have no date format.";
        if (settings.Palette is null) return "The settings have no palette.";
        if (!double.IsFinite(settings.CellPadding)) return "The settings have a non-finite cell padding.";
        foreach (var style in new[] { settings.TitleTextStyle, settings.HeaderTextStyle, settings.BodyTextStyle, settings.ChartLineStyle, settings.HeaderLineStyle })
            if (style is not null && (style.UniqueId is null || style.Name is null))
                return "A style reference is incomplete.";
        return null;
    }
}
