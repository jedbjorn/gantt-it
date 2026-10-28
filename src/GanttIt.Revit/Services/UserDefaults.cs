using System.IO;
using System.Text.Json;
using GanttIt.Core.Model;

namespace GanttIt.Revit.Services;

/// <summary>
/// Per-user defaults (§2.3, Q1): the chosen style <i>names</i> only, in
/// <c>%AppData%\GanttIt\defaults.json</c>. They seed the settings of a model that
/// has none yet. No model data is ever written here (Data Tenancy).
/// </summary>
internal static class UserDefaults
{
    public sealed record Names(
        string? TitleTextStyle,
        string? HeaderTextStyle,
        string? BodyTextStyle,
        string? ChartLineStyle,
        string? HeaderLineStyle);

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GanttIt", "defaults.json");

    public static void Save(ProjectSettings settings)
    {
        var names = new Names(
            settings.TitleTextStyle?.Name,
            settings.HeaderTextStyle?.Name,
            settings.BodyTextStyle?.Name,
            settings.ChartLineStyle?.Name,
            settings.HeaderLineStyle?.Name);
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(names, Options));
    }

    /// <summary>The saved names, or null when there is no readable file. Never throws.</summary>
    public static Names? Load()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<Names>(File.ReadAllText(FilePath), Options) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return null;
        }
    }
}
