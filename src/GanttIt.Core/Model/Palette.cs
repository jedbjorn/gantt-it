namespace GanttIt.Core.Model;

/// <summary>The default 12-swatch palette and next-color assignment (§2.2, §2.4).</summary>
public static class Palette
{
    /// <summary>Twelve distinct, print-friendly colors. The first is the Designs/OS blue.</summary>
    public static readonly IReadOnlyList<Rgb> Default =
    [
        Rgb.Parse("#4F8EF7"), // blue
        Rgb.Parse("#F28E2B"), // orange
        Rgb.Parse("#59A14F"), // green
        Rgb.Parse("#E15759"), // red
        Rgb.Parse("#B07AA1"), // purple
        Rgb.Parse("#76B7B2"), // teal
        Rgb.Parse("#EDC948"), // yellow
        Rgb.Parse("#FF9DA7"), // pink
        Rgb.Parse("#9C755F"), // brown
        Rgb.Parse("#BAB0AC"), // warm grey
        Rgb.Parse("#8CD17D"), // light green
        Rgb.Parse("#499894"), // dark teal
    ];

    /// <summary>
    /// The color for a section appended after <paramref name="sections"/>: the
    /// palette entry following the last section's color (wrapping). If the last
    /// color is not in the palette (picked by hand), falls back to
    /// <c>palette[count % palette.Count]</c>. Deterministic.
    /// </summary>
    public static Rgb NextColor(IReadOnlyList<Rgb> palette, IReadOnlyList<Section> sections)
    {
        ArgumentNullException.ThrowIfNull(palette);
        if (palette.Count == 0) throw new ArgumentException("The palette is empty.", nameof(palette));
        if (sections.Count == 0) return palette[0];

        var lastIndex = IndexOf(palette, sections[^1].Color);
        return lastIndex >= 0
            ? palette[(lastIndex + 1) % palette.Count]
            : palette[sections.Count % palette.Count];
    }

    private static int IndexOf(IReadOnlyList<Rgb> palette, Rgb color)
    {
        for (var i = 0; i < palette.Count; i++)
            if (palette[i] == color) return i;
        return -1;
    }
}
