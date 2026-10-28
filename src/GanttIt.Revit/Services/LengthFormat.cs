using Autodesk.Revit.DB;

namespace GanttIt.Revit.Services;

/// <summary>
/// Paper lengths in the project's length units (D11, §6.4), through
/// <see cref="UnitFormatUtils"/>. The project's unit, symbol and display flags are
/// kept, but the rounding is made fine enough for small paper lengths (about
/// 0.1 mm, or 1/256" in fractional-inch units): a 1.5 mm padding in a project that
/// rounds to whole millimetres would otherwise show as "2", and 180 mm in a project
/// that rounds to 1/16" would not round-trip.
/// </summary>
internal static class LengthFormat
{
    /// <summary>Target resolution: 0.1 mm, in feet.</summary>
    private const double WantedFt = 0.1 / 304.8;

    public static string Format(Document doc, double feet)
    {
        var units = doc.GetUnits();
        var finer = FinerOptions(units);
        if (finer is not null)
        {
            try
            {
                var options = new FormatValueOptions();
                options.SetFormatOptions(finer);
                return UnitFormatUtils.Format(units, SpecTypeId.Length, feet, false, options);
            }
            catch (Exception)
            {
                // Fall back to the project's own format below.
            }
        }
        return UnitFormatUtils.Format(units, SpecTypeId.Length, feet, false);
    }

    public static bool TryParse(Document doc, string text, out double feet)
    {
        feet = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        return UnitFormatUtils.TryParse(doc.GetUnits(), SpecTypeId.Length, text.Trim(), out feet) && double.IsFinite(feet);
    }

    /// <summary>
    /// The project's length format with a finer accuracy, or null when no finer valid
    /// accuracy exists. Each optional setting is applied on its own, guarded, so a
    /// setting a unit doesn't support can't undo the accuracy change.
    /// </summary>
    private static FormatOptions? FinerOptions(Units units)
    {
        FormatOptions project;
        ForgeTypeId unit;
        try
        {
            project = units.GetFormatOptions(SpecTypeId.Length);
            unit = project.GetUnitTypeId();
        }
        catch (Exception)
        {
            return null;
        }

        var accuracy = FinerAccuracy(unit, project.Accuracy);
        if (accuracy is null) return null;

        var options = new FormatOptions(unit) { Accuracy = accuracy.Value };
        Try(() =>
        {
            var symbol = project.GetSymbolTypeId();
            if (options.CanHaveSymbol() && options.IsValidSymbol(symbol)) options.SetSymbolTypeId(symbol);
        });
        Try(() => { if (options.CanSuppressTrailingZeros()) options.SuppressTrailingZeros = true; });
        Try(() => { if (options.CanSuppressLeadingZeros()) options.SuppressLeadingZeros = project.SuppressLeadingZeros; });
        Try(() => { if (options.CanSuppressSpaces()) options.SuppressSpaces = project.SuppressSpaces; });
        Try(() => options.UseDigitGrouping = project.UseDigitGrouping);
        return options;
    }

    /// <summary>
    /// The coarsest valid accuracy that resolves about 0.1 mm, or null when the
    /// project's own accuracy is already that fine (or nothing finer is valid).
    /// Fractional-inch accuracies are tried both in inches and in feet.
    /// </summary>
    private static double? FinerAccuracy(ForgeTypeId unit, double current)
    {
        double wanted;
        try
        {
            wanted = UnitUtils.ConvertFromInternalUnits(WantedFt, unit);
        }
        catch (Exception)
        {
            return null;
        }
        if (current <= wanted * 1.0001) return null;

        var candidates = new List<double>();
        for (var p = 0; p <= 8; p++) candidates.Add(Math.Pow(10, -p));
        for (var n = 1; n <= 8; n++) candidates.Add(1.0 / (1 << n));         // fractional inches, in inches
        for (var n = 1; n <= 8; n++) candidates.Add(1.0 / (1 << n) / 12.0);  // fractional inches, in feet
        foreach (var a in candidates.Where(a => a < current).OrderByDescending(a => a))
        {
            if (a > wanted * 1.0001) continue;
            bool valid;
            try
            {
                valid = FormatOptions.IsValidAccuracy(unit, a);
            }
            catch (Exception)
            {
                valid = false;
            }
            if (valid) return a;
        }
        return null;
    }

    private static void Try(Action set)
    {
        try
        {
            set();
        }
        catch (Exception)
        {
            // Optional display flag the unit does not support.
        }
    }
}
