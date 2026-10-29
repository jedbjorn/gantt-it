using System.Globalization;
using GanttIt.Core.Dates;

namespace GanttIt.Core.Tests.Dates;

/// <summary>Typed dates in the editor's Start/End fields follow the chart's date format.</summary>
public class DateEntryTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    [Theory]
    [InlineData(DateFormats.Iso, "2026-04-03")]
    [InlineData(DateFormats.UsSlash, "04/03/2026")]
    [InlineData(DateFormats.EuSlash, "03/04/2026")]
    [InlineData(DateFormats.EuDot, "03.04.2026")]
    [InlineData("d MMM yyyy", "3 Apr 2026")]
    [InlineData("MMMM d, yyyy", "April 3, 2026")]
    [InlineData("yyyy/MM/dd", "2026/04/03")]
    [InlineData("dd-MM-yy", "03-04-26")]
    public void EveryPreset_AndCustomPatterns_RoundTrip(string pattern, string text)
    {
        Assert.Equal(pattern, DateFormats.EntryPattern(pattern));
        Assert.Equal(text, DateFormats.Format(D(2026, 4, 3), pattern));
        Assert.True(DateFormats.TryParse(text, pattern, out var date));
        Assert.Equal(D(2026, 4, 3), date);
    }

    [Fact]
    public void DayMonthOrder_FollowsThePattern_NeverTheMachineLocale()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            foreach (var culture in new[] { "en-US", "en-GB", "de-DE", "ja-JP" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                Assert.True(DateFormats.TryParse("03/04/2026", DateFormats.EuSlash, out var eu));
                Assert.Equal(D(2026, 4, 3), eu);
                Assert.True(DateFormats.TryParse("03/04/2026", DateFormats.UsSlash, out var us));
                Assert.Equal(D(2026, 3, 4), us);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Theory]
    [InlineData(DateFormats.UsSlash, "13/04/2026")] // no month 13: not silently read as 13 April
    [InlineData(DateFormats.EuSlash, "04/13/2026")] // no month 13 either way round
    [InlineData(DateFormats.Iso, "03/04/2026")]     // another pattern's layout
    [InlineData(DateFormats.EuDot, "2026-04-03")]
    [InlineData(DateFormats.Iso, "2026-02-30")]
    [InlineData(DateFormats.Iso, "tomorrow")]
    [InlineData(DateFormats.Iso, "")]
    [InlineData(DateFormats.Iso, "   ")]
    public void TextThatDoesNotMatch_IsRejected(string pattern, string text)
    {
        Assert.False(DateFormats.TryParse(text, pattern, out _));
    }

    [Theory]
    [InlineData(DateFormats.Iso, "2026-4-3")]
    [InlineData(DateFormats.UsSlash, "4/3/2026")]
    [InlineData(DateFormats.EuSlash, "3/4/2026")]
    [InlineData(DateFormats.EuDot, " 3.4.2026 ")]
    public void LeadingZerosAndSurroundingSpaces_MayBeLeftOut(string pattern, string text)
    {
        Assert.True(DateFormats.TryParse(text, pattern, out var date));
        Assert.Equal(D(2026, 4, 3), date);
    }

    [Fact]
    public void WithoutLeadingZeros_ShortensDayAndMonth_KeepsLiteralsAndAdjacentFields()
    {
        Assert.Equal("yyyy-M-d", DateFormats.WithoutLeadingZeros("yyyy-MM-dd"));
        Assert.Equal("d MMM yyyy", DateFormats.WithoutLeadingZeros("dd MMM yyyy"));
        Assert.Equal("'dd' d.M.yyyy", DateFormats.WithoutLeadingZeros("'dd' dd.MM.yyyy"));
        Assert.Equal("ddMMyyyy", DateFormats.WithoutLeadingZeros("ddMMyyyy"));
        Assert.Equal("%d", DateFormats.WithoutLeadingZeros("dd"));
    }

    [Theory]
    [InlineData("MMM yyyy")]   // no day
    [InlineData("dd MMM")]     // no year
    [InlineData("yyyy")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("'Q'q")]
    public void PatternsThatCannotIdentifyADay_FallBackToIso(string? pattern)
    {
        Assert.Equal(DateFormats.Iso, DateFormats.EntryPattern(pattern));
    }
}
