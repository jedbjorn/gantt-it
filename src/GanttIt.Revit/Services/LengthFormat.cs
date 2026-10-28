using Autodesk.Revit.DB;

namespace GanttIt.Revit.Services;

/// <summary>
/// Paper lengths in the project's length units (D11, §6.4), through
/// <see cref="UnitFormatUtils"/>. The project's unit and symbol are kept, but the
/// rounding is made fine enough for small paper lengths: a 1.5 mm padding in a
/// project that rounds to whole millimetres would otherwise show as "2".
/// </summary>
internal static class LengthFormat
{
    /// <summary>Target precision: 0.01 mm, in feet.</summary>
    private const double TargetFt = 0.01 / 304.8;

    public static string Format(Document doc, double feet)
    {
        var units = doc.GetUnits();
        try
        {
            var options = new FormatValueOptions();
            options.SetFormatOptions(FinerOptions(units));
            return UnitFormatUtils.Format(units, SpecTypeId.Length, feet, false, options);
        }
        catch (Exception)
        {
            return UnitFormatUtils.Format(units, SpecTypeId.Length, feet, false);
        }
    }

    public static bool TryParse(Document doc, string text, out double feet)
    {
        feet = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        return UnitFormatUtils.TryParse(doc.GetUnits(), SpecTypeId.Length, text.Trim(), out feet) && double.IsFinite(feet);
    }

    private static FormatOptions FinerOptions(Units units)
    {
        var project = units.GetFormatOptions(SpecTypeId.Length);
        var unit = project.GetUnitTypeId();
        var options = new FormatOptions(unit);
        try
        {
            var symbol = project.GetSymbolTypeId();
            if (options.CanHaveSymbol() && options.IsValidSymbol(symbol)) options.SetSymbolTypeId(symbol);
        }
        catch (Exception)
        {
            // Keep the unit's default symbol.
        }
        options.SuppressTrailingZeros = true;

        // The coarsest valid accuracy that still resolves about 0.1 mm; the project's
        // own accuracy when it is already that fine.
        var wanted = UnitUtils.ConvertFromInternalUnits(TargetFt, unit) * 10;
        var accuracy = project.Accuracy;
        if (accuracy > wanted)
        {
            var candidates = new List<double>();
            for (var p = 0; p <= 8; p++) candidates.Add(Math.Pow(10, -p));
            for (var n = 1; n <= 8; n++) candidates.Add(1.0 / (1 << n));         // fractional inches, in inches
            for (var n = 1; n <= 8; n++) candidates.Add(1.0 / (1 << n) / 12.0);  // fractional inches, in feet
            var finer = candidates
                .Where(a => a <= wanted && FormatOptions.IsValidAccuracy(unit, a))
                .OrderByDescending(a => a)
                .FirstOrDefault();
            if (finer > 0) accuracy = finer;
        }
        if (FormatOptions.IsValidAccuracy(unit, accuracy)) options.Accuracy = accuracy;
        return options;
    }
}
