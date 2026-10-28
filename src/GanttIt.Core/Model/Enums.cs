namespace GanttIt.Core.Model;

/// <summary>
/// The header timescale (§2.2, §5.3). <see cref="Auto"/> picks tiers from the
/// chart span; the other values name the tier-2 (finer) unit explicitly.
/// </summary>
public enum Timescale
{
    Auto,
    Days,
    Weeks,
    Months,
    Quarters,
}

/// <summary>Optional table columns. The name column is always shown (§2.2).</summary>
[Flags]
public enum ChartColumns
{
    None = 0,
    Start = 1,
    End = 2,
    Duration = 4,
    All = Start | End | Duration,
}
