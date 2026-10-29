#if DEBUG
namespace GanttIt.Revit.SelfTest;

/// <summary>
/// Debug-only fault hook (A11, §8.1). The Self-Test arms a named point; the next
/// time the generator reaches it, it throws, so the action's rollback can be
/// verified. Release builds contain neither this class nor the call sites.
/// </summary>
internal static class DebugFaults
{
    /// <summary>In <c>ChartGenerator.Draw</c>, after the bars and lines are drawn and before the text is placed.</summary>
    public const string MidGenerate = "mid-generate";

    /// <summary>
    /// In <c>GanttItTransaction</c>, after the fill-type purge deleted at least one type
    /// and before the commit: stands in for a commit the purge made fail.
    /// </summary>
    public const string AfterPurge = "after-purge";

    private static string? _armed;

    public static void Arm(string point) => _armed = point;

    public static bool IsArmed(string point) => string.Equals(_armed, point, StringComparison.Ordinal);

    public static void Disarm() => _armed = null;

    public static void Hit(string point)
    {
        if (!string.Equals(_armed, point, StringComparison.Ordinal)) return;
        _armed = null;
        throw new InvalidOperationException($"Gantt-It debug fault injected at '{point}'.");
    }
}
#endif
