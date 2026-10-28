using GanttIt.Core.Dates;
using GanttIt.Core.Model;
using GanttIt.Core.Scheduling;

namespace GanttIt.Core.Validation;

public enum IssueCode
{
    ChartNameBlank,
    ChartNameInvalidCharacters,
    ChartNameInUse,
    NoSections,
    SectionNameBlank,
    EndBeforeStart,
    DateOutOfRange,
    LinkInvalid,
    TimelineWidthInvalid,
    NameMaxWidthInvalid,
    CellPaddingInvalid,
    DateFormatInvalid,
    PaletteEmpty,
}

/// <summary>One validation error. <see cref="SectionId"/> is set when it belongs to a row.</summary>
public sealed record ValidationIssue(IssueCode Code, string Message, Guid? SectionId = null);

/// <summary>
/// Editor validation (§2.2). Generate stays disabled while <see cref="Validate"/>
/// returns any issue. Core cannot see Revit, so the caller supplies the names
/// of the model's other views.
/// </summary>
public static class ChartValidator
{
    /// <summary>Characters Revit refuses in view names, plus line breaks and tabs.</summary>
    public static IReadOnlyList<char> InvalidViewNameChars => InvalidChars;

    private static readonly char[] InvalidChars =
        ['\\', ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~', '\r', '\n', '\t'];

    /// <summary>
    /// Validates a chart and the settings it will be drawn with, before
    /// Generate/Update. <paramref name="otherViewNames"/> holds the names of every
    /// view in the model except this chart's own legend; they are compared
    /// trimmed and case-insensitively, which is stricter than Revit and so never
    /// lets a colliding name through.
    /// </summary>
    public static IReadOnlyList<ValidationIssue> Validate(
        Chart chart, ProjectSettings settings, IEnumerable<string> otherViewNames)
    {
        var issues = new List<ValidationIssue>();
        ValidateName(chart.Name, otherViewNames, issues);

        if (!(chart.TimelineWidth > 0))
            issues.Add(new(IssueCode.TimelineWidthInvalid, "The timeline width must be greater than zero."));
        if (!(chart.NameMaxWidth > 2 * settings.CellPadding))
            issues.Add(new(IssueCode.NameMaxWidthInvalid, "The name column must be wider than twice the cell padding."));

        if (chart.Sections.Count == 0)
            issues.Add(new(IssueCode.NoSections, "Add at least one section."));

        foreach (var s in chart.Sections)
        {
            if (string.IsNullOrWhiteSpace(s.Name))
                issues.Add(new(IssueCode.SectionNameBlank, "A section has no name.", s.Id));
            if (s.End < s.Start)
                issues.Add(new(IssueCode.EndBeforeStart, $"\"{s.Name}\" ends before it starts.", s.Id));
            if (!SupportedDates.Contains(s.Start) || !SupportedDates.Contains(s.End))
                issues.Add(new(IssueCode.DateOutOfRange,
                    $"\"{s.Name}\" has a date outside {SupportedDates.Min.Year}–{SupportedDates.Max.Year}.", s.Id));
        }

        foreach (var link in chart.Links)
        {
            var from = chart.IndexOf(link.FromSectionId);
            var to = chart.IndexOf(link.ToSectionId);
            if (from < 0 || to != from + 1)
                issues.Add(new(IssueCode.LinkInvalid, "A link does not join two neighbouring sections."));
            else if (link.LagDays != Scheduler.CurrentLag(chart.Sections[from], chart.Sections[to]))
                issues.Add(new(IssueCode.LinkInvalid, "A link's lag does not match its sections' dates.", link.ToSectionId));
        }

        issues.AddRange(ValidateSettings(settings));
        return issues;
    }

    /// <summary>Structural checks on settings. Whether each style still exists is a Revit-side check.</summary>
    public static IReadOnlyList<ValidationIssue> ValidateSettings(ProjectSettings settings)
    {
        var issues = new List<ValidationIssue>();
        if (!(settings.CellPadding > 0))
            issues.Add(new(IssueCode.CellPaddingInvalid, "The cell padding must be greater than zero."));
        if (!DateFormats.IsValidPattern(settings.DateFormat, out var error))
            issues.Add(new(IssueCode.DateFormatInvalid, error!));
        if (settings.Palette.Count == 0)
            issues.Add(new(IssueCode.PaletteEmpty, "The palette has no colors."));
        return issues;
    }

    private static void ValidateName(string name, IEnumerable<string> otherViewNames, List<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            issues.Add(new(IssueCode.ChartNameBlank, "The chart needs a name."));
            return;
        }

        if (name.IndexOfAny(InvalidChars) >= 0)
            issues.Add(new(IssueCode.ChartNameInvalidCharacters,
                "The chart name contains a character Revit does not allow in view names."));

        var trimmed = name.Trim();
        if (otherViewNames.Any(n => string.Equals(n.Trim(), trimmed, StringComparison.OrdinalIgnoreCase)))
            issues.Add(new(IssueCode.ChartNameInUse, $"A view named \"{trimmed}\" already exists."));
    }
}
