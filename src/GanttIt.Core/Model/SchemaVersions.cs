namespace GanttIt.Core.Model;

/// <summary>
/// Current JSON schema versions. A stored document with a higher version was
/// written by a newer add-in: it may be shown but is never edited or rewritten
/// in this (older) format (§3, Beyond Intention).
/// </summary>
public static class SchemaVersions
{
    public const int Chart = 1;
    public const int Settings = 1;
}
