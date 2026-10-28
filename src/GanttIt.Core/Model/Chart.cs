namespace GanttIt.Core.Model;

/// <summary>
/// One Gantt chart = one Revit legend view (§3). Mutable so the editor's
/// view-models can bind to it; the scheduling rules in
/// <see cref="Scheduling.Scheduler"/> keep <see cref="Links"/> consistent.
/// </summary>
public sealed class Chart
{
    /// <summary>Default timeline width on paper: 180 mm, in feet.</summary>
    public static readonly double DefaultTimelineWidth = Units.FromMillimeters(180);

    /// <summary>Default maximum name-column width on paper: 60 mm, in feet.</summary>
    public static readonly double DefaultNameMaxWidth = Units.FromMillimeters(60);

    /// <summary>Stable identity, also stored on the legend view.</summary>
    public Guid Id { get; set; }

    /// <summary>The legend view name.</summary>
    public string Name { get; set; } = "";

    /// <summary>Title text drawn above the table. Blank means no title row.</summary>
    public string Title { get; set; } = "";

    public Timescale Timescale { get; set; } = Timescale.Auto;

    public ChartColumns Columns { get; set; } = ChartColumns.All;

    /// <summary>Paper width of the timeline area, in feet.</summary>
    public double TimelineWidth { get; set; } = DefaultTimelineWidth;

    /// <summary>Paper width cap of the name column, in feet; longer names wrap.</summary>
    public double NameMaxWidth { get; set; } = DefaultNameMaxWidth;

    /// <summary>Sections in display order.</summary>
    public List<Section> Sections { get; set; } = [];

    /// <summary>Links between neighbouring sections, in no particular order.</summary>
    public List<Link> Links { get; set; } = [];

    public int SchemaVersion { get; set; } = SchemaVersions.Chart;

    /// <summary>Creates an empty chart whose title defaults to its name (§2.2).</summary>
    public static Chart Create(string name, Guid? id = null) =>
        new() { Id = id ?? Guid.NewGuid(), Name = name, Title = name };

    /// <summary>Index of the section with <paramref name="sectionId"/>, or −1.</summary>
    public int IndexOf(Guid sectionId) => Sections.FindIndex(s => s.Id == sectionId);

    /// <summary>The section with <paramref name="sectionId"/>; throws if absent.</summary>
    public Section GetSection(Guid sectionId) =>
        Sections.Find(s => s.Id == sectionId)
        ?? throw new ArgumentException($"No section with id {sectionId}.", nameof(sectionId));

    /// <summary>The link from the section at <paramref name="index"/> to the next one, if any.</summary>
    public Link? LinkAfter(int index) =>
        index >= 0 && index < Sections.Count - 1
            ? FindLink(Sections[index].Id, Sections[index + 1].Id)
            : null;

    /// <summary>The link from the previous section to the one at <paramref name="index"/>, if any.</summary>
    public Link? LinkBefore(int index) => LinkAfter(index - 1);

    private Link? FindLink(Guid from, Guid to) =>
        Links.Find(l => l.FromSectionId == from && l.ToSectionId == to);
}
