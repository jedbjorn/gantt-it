namespace GanttIt.Core.Model;

/// <summary>
/// Length conversions. Every length in Core is in Revit internal units (feet);
/// the legend is generated at 1:1, so model feet equal paper feet (§6.4).
/// </summary>
public static class Units
{
    public const double MillimetersPerFoot = 304.8;

    public static double FromMillimeters(double mm) => mm / MillimetersPerFoot;

    public static double ToMillimeters(double feet) => feet * MillimetersPerFoot;
}
