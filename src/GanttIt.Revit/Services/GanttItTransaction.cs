using Autodesk.Revit.DB;

namespace GanttIt.Revit.Services;

/// <summary>
/// Pre-processes Revit failures in every Gantt-It transaction (§7.4): warnings are
/// recorded and deleted, so no warning dialog pops up; any error rolls the whole
/// transaction back. The messages are kept for the action's report.
/// </summary>
internal sealed class GanttItFailures : IFailuresPreprocessor
{
    public List<string> Warnings { get; } = [];

    public List<string> Errors { get; } = [];

    public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
    {
        var rollBack = false;
        foreach (var message in failuresAccessor.GetFailureMessages())
        {
            var text = message.GetDescriptionText();
            if (message.GetSeverity() == FailureSeverity.Warning)
            {
                Warnings.Add(text);
                failuresAccessor.DeleteWarning(message);
            }
            else
            {
                Errors.Add(text);
                rollBack = true;
            }
        }
        return rollBack ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
    }
}

/// <summary>
/// One Gantt-It action = one Revit transaction = one Undo step (§7.4), named
/// <c>Gantt-It: &lt;action&gt; &lt;name&gt;</c>. Any exception or Revit error rolls
/// everything back, so a chart is never left half drawn.
/// </summary>
internal static class GanttItTransaction
{
    /// <summary>The Undo-list name of an action.</summary>
    public static string Name(string action, string? subject) =>
        string.IsNullOrWhiteSpace(subject) ? $"Gantt-It: {action}" : $"Gantt-It: {action} {subject}";

    /// <summary>
    /// Runs <paramref name="body"/> in one transaction. The body returns the view the
    /// action produced or changed (or null) plus an optional note for the user.
    /// <para>
    /// Every Gantt-It action ends here with <see cref="FillTypes.DeleteUnused"/>, in
    /// the same transaction (§7.3, §7.4): no action leaves unused Gantt-It fill types
    /// behind, whoever orphaned them (a chart legend deleted by hand, an earlier
    /// session), and the purge is undone or rolled back with the action. With
    /// <paramref name="reportPurge"/> the note names the types removed.
    /// </para>
    /// <para>
    /// The purge must never cost the user their action. In a workshared model the
    /// checkout and central-update status it relies on are cached, and deleting a type
    /// can still need a borrow that fails at commit, which rolls everything back. So
    /// when an attempt that purged at least one type fails after the purge (commit
    /// refused, rolled back by <see cref="GanttItFailures"/>, or an exception) in a
    /// workshared model, the action is run once more in a fresh transaction without
    /// the purge; a later action purges those types. A failure in the body itself
    /// happens before the purge and is never retried. Every body is safe to run twice:
    /// after a rollback the document is exactly as before the first attempt, each body
    /// builds its Revit state (fill types, measurer, new elements) inside the
    /// transaction, and what it captured beforehand (views, stored charts, styles) are
    /// elements that existed before and are unchanged by the rollback.
    /// </para>
    /// </summary>
    public static ActionResult Run(Document doc, string name, Func<(ElementId? View, string? Note)> body, bool reportPurge = false)
    {
        var first = Attempt(doc, name, body, purge: true, reportPurge, out var retryWithoutPurge);
        return retryWithoutPurge ? Attempt(doc, name, body, purge: false, reportPurge, out _) : first;
    }

    private static ActionResult Attempt(
        Document doc, string name, Func<(ElementId? View, string? Note)> body, bool purge, bool reportPurge, out bool retryWithoutPurge)
    {
        retryWithoutPurge = false;
        var purged = 0;
        var simulated = false;
        var failures = new GanttItFailures();
        using var t = new Transaction(doc, name);
        var options = t.GetFailureHandlingOptions();
        options.SetFailuresPreprocessor(failures);
        options.SetClearAfterRollback(true);
        t.SetFailureHandlingOptions(options);

        try
        {
            if (t.Start() != TransactionStatus.Started)
                return new ActionResult(ActionStatus.Failed, $"{name}: Revit did not start the transaction.");

            var (view, note) = body();
            if (purge)
            {
                var result = FillTypes.DeleteUnused(doc);
                purged = result.Deleted.Count;
                if (reportPurge && purged > 0)
                {
                    var removed = $"Removed unused fill types: {string.Join(", ", result.Deleted)}.";
                    note = string.IsNullOrEmpty(note) ? removed : $"{note} {removed}";
                }
#if DEBUG
                // Self-Test: stands in for a commit refused because of the purge (a failed borrow).
                if (purged > 0 && SelfTest.DebugFaults.IsArmed(SelfTest.DebugFaults.AfterPurge))
                {
                    simulated = true;
                    SelfTest.DebugFaults.Hit(SelfTest.DebugFaults.AfterPurge);
                }
#endif
            }
            var status = t.Commit();
            if (status != TransactionStatus.Committed)
            {
                retryWithoutPurge = purged > 0 && doc.IsWorkshared;
                var why = failures.Errors.Count > 0 ? string.Join(" ", failures.Errors) : $"status {status}";
                return new ActionResult(ActionStatus.Failed, $"{name} was rolled back: {why}");
            }
            return new ActionResult(ActionStatus.Ok, note, view);
        }
        catch (Exception ex)
        {
            if (t.HasStarted() && !t.HasEnded()) t.RollBack();
            retryWithoutPurge = purged > 0 && (doc.IsWorkshared || simulated);
            return new ActionResult(ActionStatus.Failed, $"{name} failed and was rolled back: {ex.Message}");
        }
    }
}
