using GanttIt.Core.Model;
using GanttIt.Core.Validation;
using static GanttIt.Core.Tests.Support.TestCharts;

namespace GanttIt.Core.Tests.Validation;

/// <summary>Editor validation (§2.2): Generate stays disabled while any issue exists.</summary>
public class ChartValidatorTests
{
    private static IReadOnlyList<IssueCode> Codes(Chart chart, ProjectSettings? settings = null, params string[] otherViews) =>
        ChartValidator.Validate(chart, settings ?? Settings(), otherViews).Select(i => i.Code).ToList();

    [Fact]
    public void ValidChart_HasNoIssues()
    {
        Assert.Empty(Codes(Sequential(3, D(2026, 1, 1)), null, "Level 1", "Legend 1"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankChartName_IsAnError(string name)
    {
        var chart = Sequential(1, D(2026, 1, 1));
        chart.Name = name;
        Assert.Contains(IssueCode.ChartNameBlank, Codes(chart));
    }

    [Theory]
    [InlineData("Phase: 1")]
    [InlineData("Plan [A]")]
    [InlineData("Line\nbreak")]
    public void ChartName_WithCharactersRevitRejects_IsAnError(string name)
    {
        var chart = Sequential(1, D(2026, 1, 1));
        chart.Name = name;
        Assert.Contains(IssueCode.ChartNameInvalidCharacters, Codes(chart));
    }

    [Theory]
    [InlineData("Test chart")]
    [InlineData("test CHART ")]
    public void ChartName_CollidingWithAnyOtherView_IsAnError(string existing)
    {
        Assert.Contains(IssueCode.ChartNameInUse, Codes(Sequential(1, D(2026, 1, 1)), null, "Level 1", existing));
    }

    [Fact]
    public void BlankSectionName_AndEndBeforeStart_AreErrorsOnThatRow()
    {
        var chart = Sequential(3, D(2026, 1, 1));
        chart.Sections[0].Name = " ";
        chart.Sections[2].End = chart.Sections[2].Start.AddDays(-1);

        var issues = ChartValidator.Validate(chart, Settings(), []);

        Assert.Contains(issues, i => i.Code == IssueCode.SectionNameBlank && i.SectionId == Id(1));
        Assert.Contains(issues, i => i.Code == IssueCode.EndBeforeStart && i.SectionId == Id(3));
    }

    [Fact]
    public void NoSections_IsAnError()
    {
        Assert.Contains(IssueCode.NoSections, Codes(Chart.Create("Empty")));
    }

    [Fact]
    public void DatesOutsideTheSupportedRange_AreErrors()
    {
        var chart = Sequential(1, D(2026, 1, 1));
        chart.Sections[0].End = new DateOnly(9999, 12, 31);
        Assert.Contains(IssueCode.DateOutOfRange, Codes(chart));
    }

    [Fact]
    public void NonNeighbourLink_IsAnError()
    {
        var chart = Sequential(3, D(2026, 1, 1));
        chart.Links.Add(new Link(Id(1), Id(3), 0));
        Assert.Contains(IssueCode.LinkInvalid, Codes(chart));
    }

    [Fact]
    public void Widths_AndSettings_AreChecked()
    {
        var chart = Sequential(1, D(2026, 1, 1));
        chart.TimelineWidth = 0;
        chart.NameMaxWidth = Units.FromMillimeters(2);
        var settings = new ProjectSettings { CellPadding = 0, DateFormat = "yyyy-MM-dd HH:mm", Palette = [] };

        var codes = Codes(chart, settings);

        Assert.Contains(IssueCode.TimelineWidthInvalid, codes);
        Assert.Contains(IssueCode.CellPaddingInvalid, codes);
        Assert.Contains(IssueCode.DateFormatInvalid, codes);
        Assert.Contains(IssueCode.PaletteEmpty, codes);
        Assert.Contains(IssueCode.NameMaxWidthInvalid, Codes(chart));
    }
}
