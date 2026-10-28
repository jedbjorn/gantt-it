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
    /// </summary>
    public static ActionResult Run(Document doc, string name, Func<(ElementId? View, string? Note)> body)
    {
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
            var status = t.Commit();
            if (status != TransactionStatus.Committed)
            {
                var why = failures.Errors.Count > 0 ? string.Join(" ", failures.Errors) : $"status {status}";
                return new ActionResult(ActionStatus.Failed, $"{name} was rolled back: {why}");
            }
            return new ActionResult(ActionStatus.Ok, note, view);
        }
        catch (Exception ex)
        {
            if (t.HasStarted() && !t.HasEnded()) t.RollBack();
            return new ActionResult(ActionStatus.Failed, $"{name} failed and was rolled back: {ex.Message}");
        }
    }
}
