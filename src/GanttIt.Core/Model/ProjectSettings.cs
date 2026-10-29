namespace GanttIt.Core.Model;

/// <summary>
/// A reference to a Revit element (text type or line style). The UniqueId is
/// authoritative; the name is the fallback when the UniqueId no longer
/// resolves, and the key used by per-user defaults (§2.3).
/// </summary>
public sealed record StyleRef(string UniqueId, string Name);

/// <summary>
/// Per-project settings (§2.3), stored on one <c>DataStorage</c> element (§7.2).
/// Style references are null until the user picks them in Settings.
/// </summary>
public sealed class ProjectSettings
{
    /// <summary>Default cell padding on paper: 1.5 mm, in feet.</summary>
    public static readonly double DefaultCellPadding = Units.FromMillimeters(1.5);

    public StyleRef? TitleTextStyle { get; set; }

    public StyleRef? HeaderTextStyle { get; set; }

    public StyleRef? BodyTextStyle { get; set; }

    /// <summary>Row separators and timescale grid lines.</summary>
    public StyleRef? ChartLineStyle { get; set; }

    /// <summary>Header rules and the outer border.</summary>
    public StyleRef? HeaderLineStyle { get; set; }

    /// <summary>A <see cref="Dates.DateFormats.Presets"/> pattern or a custom .NET date pattern.</summary>
    public string DateFormat { get; set; } = Dates.DateFormats.Iso;

    /// <summary>Space between text and cell lines, in feet. Must be positive.</summary>
    public double CellPadding { get; set; } = DefaultCellPadding;

    /// <summary>Palette used by the color picker and to auto-assign colors to new sections.</summary>
    public List<Rgb> Palette { get; set; } = [.. Model.Palette.Default];

    public int SchemaVersion { get; set; } = SchemaVersions.Settings;

    /// <summary>A copy that Settings can edit without touching the original until saved.</summary>
    public ProjectSettings Clone() => new()
    {
        TitleTextStyle = TitleTextStyle,
        HeaderTextStyle = HeaderTextStyle,
        BodyTextStyle = BodyTextStyle,
        ChartLineStyle = ChartLineStyle,
        HeaderLineStyle = HeaderLineStyle,
        DateFormat = DateFormat,
        CellPadding = CellPadding,
        Palette = [.. Palette],
        SchemaVersion = SchemaVersion,
    };
}
