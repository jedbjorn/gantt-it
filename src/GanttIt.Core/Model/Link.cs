namespace GanttIt.Core.Model;

/// <summary>
/// A finish-to-start link between two neighbouring sections (§3, D5).
/// Invariant: <c>To.Start = From.End + 1 + LagDays</c>. A negative lag is an overlap.
/// The UI shows one link as two checkboxes ("following" on the earlier row,
/// "previous" on the later row) bound to this single record.
/// </summary>
public sealed record Link(Guid FromSectionId, Guid ToSectionId, int LagDays);
