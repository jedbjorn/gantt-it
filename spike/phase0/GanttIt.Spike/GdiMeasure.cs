using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;

namespace GanttIt.Spike;

/// <summary>Round-2 V9 experiment: Revit's TextNote widths are whole multiples of 1/384 inch, i.e. hinted
/// pixel advances on a 384-dpi grid. This measures each string at candidate integer em sizes (in 384-dpi
/// pixels) with (a) WPF FormattedText in TextFormattingMode.Display (GDI-compatible, whole-pixel advances)
/// and (b) GDI GetTextExtentPoint32 as a reference, so the em-size rule can be fitted offline.</summary>
internal static class GdiMeasure
{
    public const double Dpi = 384.0;
    public static double PxToMm(double px) => px * 25.4 / Dpi;
    public static double MmToPx(double mm) => mm / 25.4 * Dpi;

    public static JsonObject Candidates(string text, string font, bool bold, bool italic, double sizeMm, double capsHeight)
    {
        var emIdeal = MmToPx(sizeMm) / capsHeight;
        var o = new JsonObject { ["emIdealPx"] = Util.N(emIdeal, 4) };
        var arr = new JsonArray();
        var lines = text.Replace("\r", "\n");
        for (var em = (int)Math.Floor(emIdeal) - 1; em <= (int)Math.Floor(emIdeal) + 2; em++)
        {
            var c = new JsonObject { ["emPx"] = em };
            try
            {
                var ft = new FormattedText(lines, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, WpfMeasure.Face(font, bold, italic),
                    em, Brushes.Black, null, TextFormattingMode.Display, 1.0);
                c["wpfDispW"] = Util.N(ft.WidthIncludingTrailingWhitespace, 3); c["wpfDispH"] = Util.N(ft.Height, 3);
            }
            catch (Exception ex) { c["wpfDispErr"] = ex.Message; }
            try
            {
                var (cx, cy, tmH, tmAsc, tmDesc, tmExt) = Gdi(lines.Split('\n')[0], font, bold, italic, em);
                c["gdiW"] = cx; c["gdiH"] = cy; c["tmHeight"] = tmH; c["tmAscent"] = tmAsc; c["tmDescent"] = tmDesc; c["tmExtLeading"] = tmExt;
            }
            catch (Exception ex) { c["gdiErr"] = ex.Message; }
            arr.Add(c);
        }
        o["c"] = arr;
        return o;
    }

    private static (int, int, int, int, int, int) Gdi(string text, string font, bool bold, bool italic, int emPx)
    {
        var hdc = CreateCompatibleDC(IntPtr.Zero);
        try
        {
            var hf = CreateFontW(-emPx, 0, 0, 0, bold ? 700 : 400, italic ? 1u : 0u, 0, 0, 1 /*DEFAULT_CHARSET*/, 0, 0, 5 /*CLEARTYPE*/, 0, font);
            var old = SelectObject(hdc, hf);
            GetTextExtentPoint32W(hdc, text, text.Length, out var sz);
            GetTextMetricsW(hdc, out var tm);
            SelectObject(hdc, old); DeleteObject(hf);
            return (sz.cx, sz.cy, tm.tmHeight, tm.tmAscent, tm.tmDescent, tm.tmExternalLeading);
        }
        finally { DeleteDC(hdc); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TEXTMETRICW
    {
        public int tmHeight, tmAscent, tmDescent, tmInternalLeading, tmExternalLeading, tmAveCharWidth, tmMaxCharWidth, tmWeight, tmOverhang, tmDigitizedAspectX, tmDigitizedAspectY;
        public char tmFirstChar, tmLastChar, tmDefaultChar, tmBreakChar;
        public byte tmItalic, tmUnderlined, tmStruckOut, tmPitchAndFamily, tmCharSet;
    }
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFontW(int h, int w, int esc, int orient, int weight, uint italic, uint underline, uint strike, uint charset, uint outPrec, uint clipPrec, uint quality, uint pitch, string face);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern bool GetTextExtentPoint32W(IntPtr hdc, string s, int c, out SIZE sz);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern bool GetTextMetricsW(IntPtr hdc, out TEXTMETRICW tm);
}
