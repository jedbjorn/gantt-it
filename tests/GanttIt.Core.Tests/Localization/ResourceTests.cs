using System.Text.RegularExpressions;
using System.Xml.Linq;
using GanttIt.Core.Editing;
using GanttIt.Core.Localization;
using GanttIt.Core.Model;
using GanttIt.Core.Validation;

namespace GanttIt.Core.Tests.Localization;

/// <summary>
/// A24: every UI string exists in English, French, Spanish and German. Reads the .resx
/// files of both projects (Core and the Revit add-in) as data from the source tree, so
/// the add-in's resources are checked without referencing Revit.
/// </summary>
public partial class ResourceTests
{
    private static readonly string[] Cultures = ["fr", "es", "de"];

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "GanttIt.sln"))) return dir.FullName;
        throw new InvalidOperationException("GanttIt.sln not found above the test output.");
    }

    public static TheoryData<string> NeutralResx() =>
    [
        "src/GanttIt.Core/Resources/CoreStrings.resx",
        "src/GanttIt.Revit/Resources/Strings.resx",
    ];

    private static Dictionary<string, string> Read(string path) =>
        XDocument.Load(path).Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? "");

    private static SortedSet<int> Placeholders(string text) =>
        [.. PlaceholderPattern().Matches(text.Replace("{{", "").Replace("}}", "")).Select(m => int.Parse(m.Groups[1].Value))];

    [GeneratedRegex(@"\{(\d+)(?:[,:][^}]*)?\}")]
    private static partial Regex PlaceholderPattern();

    [Theory]
    [MemberData(nameof(NeutralResx))]
    public void EveryKey_IsTranslated_NonEmpty_WithTheSamePlaceholders(string neutral)
    {
        var path = Path.Combine(RepoRoot(), neutral);
        var english = Read(path);
        Assert.NotEmpty(english);
        Assert.All(english, e => Assert.False(string.IsNullOrWhiteSpace(e.Value), $"{neutral}: {e.Key} is empty"));

        foreach (var culture in Cultures)
        {
            var localized = Read(Path.ChangeExtension(path, $".{culture}.resx"));
            Assert.True(english.Keys.ToHashSet().SetEquals(localized.Keys),
                $"{culture}: missing [{string.Join(", ", english.Keys.Except(localized.Keys))}], " +
                $"extra [{string.Join(", ", localized.Keys.Except(english.Keys))}]");
            foreach (var (key, value) in localized)
            {
                Assert.False(string.IsNullOrWhiteSpace(value), $"{culture}: {key} is empty");
                Assert.True(Placeholders(english[key]).SetEquals(Placeholders(value)),
                    $"{culture}: {key} has placeholders {{{string.Join(",", Placeholders(value))}}}, English {{{string.Join(",", Placeholders(english[key]))}}}");
                _ = string.Format(value, new object[Placeholders(english[key]).Count]); // well-formed braces
            }
        }
    }

    [Theory]
    [MemberData(nameof(NeutralResx))]
    public void GeneratedAccessors_MatchTheResx(string neutral)
    {
        // tools/gen_strings.py writes one accessor per key: a property, or a method with one
        // argument per placeholder. A stale *.g.cs would hide a new key or keep a removed one.
        var path = Path.Combine(RepoRoot(), neutral);
        var english = Read(path);
        var className = Path.GetFileNameWithoutExtension(path);
        var generated = File.ReadAllText(Path.Combine(Path.GetDirectoryName(path)!, className + ".g.cs"));
        var accessors = AccessorPattern().Matches(generated)
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Success ? m.Groups[2].Value.Split(',').Length : 0);

        Assert.True(english.Keys.ToHashSet().SetEquals(accessors.Keys),
            $"{className}.g.cs is stale (run python3 tools/gen_strings.py): missing [{string.Join(", ", english.Keys.Except(accessors.Keys))}], " +
            $"extra [{string.Join(", ", accessors.Keys.Except(english.Keys))}]");
        foreach (var (key, value) in english)
            Assert.True(accessors[key] == (Placeholders(value).Count == 0 ? 0 : Placeholders(value).Max() + 1),
                $"{className}.{key}: accessor arity {accessors[key]} does not match '{value}' (run python3 tools/gen_strings.py)");
    }

    [GeneratedRegex(@"internal static string (\w+)(?:\(([^)]*)\))? => (?:Get|Format)\(")]
    private static partial Regex AccessorPattern();

    [Fact]
    public void EveryXamlKey_ExistsInTheAddInResources()
    {
        var root = RepoRoot();
        var keys = Read(Path.Combine(root, "src/GanttIt.Revit/Resources/Strings.resx")).Keys.ToHashSet();
        var used = Directory.EnumerateFiles(Path.Combine(root, "src/GanttIt.Revit"), "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(f => XamlKeyPattern().Matches(File.ReadAllText(f)).Select(m => (File: Path.GetFileName(f), Key: m.Groups[1].Value)))
            .ToList();

        Assert.NotEmpty(used);
        Assert.All(used, u => Assert.True(keys.Contains(u.Key), $"{u.File}: {{l:Tr {u.Key}}} is not in Strings.resx"));
    }

    [GeneratedRegex(@"\{l:Tr (\w+)\}")]
    private static partial Regex XamlKeyPattern();

    [Fact]
    public void NoXaml_HasAHardCodedEnglishTextAttribute()
    {
        // Every user-visible attribute is a binding, a {l:Tr} key, a glyph or a symbol.
        var root = RepoRoot();
        var attribute = new Regex("""\b(Text|Content|ToolTip|Title|AutomationProperties\.Name|FieldName)="([^"{]*)" """.TrimEnd());
        var glyph = new Regex("&#x[0-9A-Fa-f]+;");
        var offenders = Directory.EnumerateFiles(Path.Combine(root, "src/GanttIt.Revit"), "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(f => attribute.Matches(File.ReadAllText(f))
                .Where(m => glyph.Replace(m.Groups[2].Value, "").Any(char.IsLetter))
                .Select(m => $"{Path.GetFileName(f)}: {m.Value}"))
            .ToList();

        Assert.Empty(offenders);
    }
}

/// <summary>Switches <see cref="UiLanguage"/>, so it never runs alongside tests that read English messages.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class UiLanguageCollection
{
    public const string Name = "UiLanguage";
}

[Collection(UiLanguageCollection.Name)]
public class UiLanguageTests
{
    private static T In<T>(Language language, Func<T> read)
    {
        var before = UiLanguage.Current;
        UiLanguage.Current = language;
        try
        {
            return read();
        }
        finally
        {
            UiLanguage.Current = before;
        }
    }

    [Theory]
    [InlineData(Language.English, "Add at least one section.")]
    [InlineData(Language.French, "Ajoutez au moins une section.")]
    [InlineData(Language.Spanish, "Agregue al menos una sección.")]
    [InlineData(Language.German, "Fügen Sie mindestens einen Abschnitt hinzu.")]
    public void CoreMessages_AreInTheUiLanguage(Language language, string noSections)
    {
        var issues = In(language, () => ChartValidator.Validate(Chart.Create("c"), new ProjectSettings(), []));

        Assert.Contains(issues, i => i.Code == IssueCode.NoSections && i.Message == noSections);
    }

    [Fact]
    public void CoreFormattedMessages_FillTheirArguments()
    {
        Assert.Equal("Sur 3 feuilles", In(Language.French, () => ChartSummary.DescribePlacement(3)));
        Assert.Equal("1 Abschnitt", In(Language.German, () => ChartSummary.DescribeSections(1)));
        Assert.Equal("Las fechas deben estar entre 1900 y 2199.",
            In(Language.Spanish, () => EditorInput.IsSupportedDate(new DateOnly(1800, 1, 1), out var e) ? null : e));
    }

    [Fact]
    public void HomeCardSpan_NamesMonthsInTheUiLanguage_NotTheChartLanguage()
    {
        var chart = Chart.Create("c");
        chart.Sections.Add(Support.TestCharts.Section(1, new DateOnly(2026, 3, 2), new DateOnly(2026, 5, 29)));

        Assert.Equal("2 Mär 2026 – 29 Mai 2026", In(Language.German, () => ChartSummary.DescribeSpan(chart, "d MMM yyyy")));
        Assert.Equal("2 Mar 2026 – 29 May 2026", ChartSummary.DescribeSpan(chart, "d MMM yyyy"));
    }

    [Fact]
    public void TypedDateErrors_UseTheGivenLanguageForTheExample()
    {
        var error = In(Language.French, () => Core.Dates.DateFormats.TypedDateError("31 xyz", "d MMM yyyy", Language.French));

        Assert.Contains("31 déc. 2026", error);
        Assert.StartsWith("«", error);
    }

    [Fact]
    public void UndefinedLanguage_FallsBackToEnglish()
    {
        In((Language)42, () =>
        {
            Assert.Equal(Language.English, UiLanguage.Current);
            return 0;
        });
    }
}
