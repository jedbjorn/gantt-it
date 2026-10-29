using System.Windows.Markup;
using GanttIt.Revit.Resources;

namespace GanttIt.Revit.UI.Localization;

/// <summary>
/// <c>{l:Tr Key}</c>: the UI string <c>Key</c> from Strings.resx in the UI language
/// (<see cref="Core.Localization.UiLanguage"/>), resolved when the XAML loads. Every
/// Gantt-It window is created after the language is applied and lives inside one
/// modal session, so the text never needs to change while it is shown.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension()
    {
    }

    public TrExtension(string key)
    {
        Key = key;
    }

    /// <summary>The resource key (ResourceTests checks every key used in XAML exists).</summary>
    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) => Strings.Get(Key);
}
