using System.Globalization;

namespace GanttIt.Core.Model;

/// <summary>An opaque 8-bit RGB color. Serialized as <c>#RRGGBB</c>.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    /// <summary>Formats as upper-case <c>#RRGGBB</c>, the form used in fill type names (§7.3).</summary>
    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    public override string ToString() => ToHex();

    /// <summary>Parses <c>#RRGGBB</c> (the leading <c>#</c> is optional, case-insensitive).</summary>
    public static bool TryParse(string? text, out Rgb color)
    {
        color = default;
        if (text is null) return false;
        var s = text.Trim();
        if (s.StartsWith('#')) s = s[1..];
        if (s.Length != 6 || !uint.TryParse(s, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var v))
            return false;
        color = new Rgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    /// <summary>Parses <c>#RRGGBB</c>; throws <see cref="FormatException"/> on bad input.</summary>
    public static Rgb Parse(string text) =>
        TryParse(text, out var c) ? c : throw new FormatException($"'{text}' is not a #RRGGBB color.");
}
