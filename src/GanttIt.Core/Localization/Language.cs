using System.Globalization;
using System.Resources;

namespace GanttIt.Core.Localization;

/// <summary>
/// The languages Gantt-It speaks (spec §2.6, decision #22): the UI follows Revit's
/// language, and each project picks the language of the text drawn on its charts.
/// Stored by name in the settings JSON ("chartLanguage": "French"), so the order
/// may change but a name never may.
/// </summary>
public enum Language
{
    English,
    French,
    Spanish,
    German,
}

/// <summary>Pure helpers around <see cref="Language"/>: Revit's language, codes, cultures and display names.</summary>
public static class Languages
{
    /// <summary>Every supported language, in the order Settings lists them.</summary>
    public static IReadOnlyList<Language> All { get; } = [Language.English, Language.French, Language.Spanish, Language.German];

    /// <summary>
    /// The UI language for Revit's language, given as the name of its
    /// <c>Autodesk.Revit.ApplicationServices.LanguageType</c> value (e.g. <c>"French"</c>,
    /// <c>"English_GB"</c>). A French, German or Spanish variant (any name starting
    /// with that word) maps to that language; every other value, including every
    /// English variant, Japanese, an unknown or future value, or null, maps to English.
    /// Works on the name so Core needs no Revit reference and tests can cover every value.
    /// </summary>
    public static Language FromRevitLanguage(string? languageTypeName)
    {
        var name = languageTypeName?.Trim() ?? "";
        if (name.StartsWith("French", StringComparison.OrdinalIgnoreCase)) return Language.French;
        if (name.StartsWith("German", StringComparison.OrdinalIgnoreCase)) return Language.German;
        if (name.StartsWith("Spanish", StringComparison.OrdinalIgnoreCase)) return Language.Spanish;
        return Language.English;
    }

    /// <summary>Reads a two-letter code (<c>en</c>, <c>fr</c>, <c>es</c>, <c>de</c>, any case); false for anything else.</summary>
    public static bool TryParseCode(string? code, out Language language)
    {
        foreach (var candidate in All)
        {
            if (string.Equals(code?.Trim(), Code(candidate), StringComparison.OrdinalIgnoreCase))
            {
                language = candidate;
                return true;
            }
        }
        language = Language.English;
        return false;
    }

    /// <summary>The two-letter code: <c>en</c>, <c>fr</c>, <c>es</c> or <c>de</c>. Also the satellite folder name.</summary>
    public static string Code(Language language) => language switch
    {
        Language.French => "fr",
        Language.Spanish => "es",
        Language.German => "de",
        _ => "en",
    };

    /// <summary>The language's own name, as Settings lists it: English, Français, Español, Deutsch.</summary>
    public static string NativeName(Language language) => language switch
    {
        Language.French => "Français",
        Language.Spanish => "Español",
        Language.German => "Deutsch",
        _ => "English",
    };

    /// <summary>
    /// The culture used to look up UI resources. English is the invariant culture, so
    /// the neutral (English) resources are used without probing for a satellite and
    /// never depend on the machine's culture.
    /// </summary>
    public static CultureInfo Culture(Language language) => language switch
    {
        Language.French => CultureInfo.GetCultureInfo("fr"),
        Language.Spanish => CultureInfo.GetCultureInfo("es"),
        Language.German => CultureInfo.GetCultureInfo("de"),
        _ => CultureInfo.InvariantCulture,
    };
}

/// <summary>
/// The UI language of this Revit session (spec §2.6). The Revit add-in sets it from
/// Revit's language at startup and whenever the Gantt-It window opens; every UI
/// string (Core's messages and the add-in's own resources) is looked up with
/// <see cref="Culture"/>. Gantt-It never changes the thread cultures, which Revit and
/// other add-ins rely on: the culture is passed to the resource managers explicitly.
/// Defaults to English, so tests and any code that never sets it read English.
/// </summary>
public static class UiLanguage
{
    private static volatile int _current = (int)Language.English;

    /// <summary>The current UI language.</summary>
    public static Language Current
    {
        get => (Language)_current;
        set => _current = Enum.IsDefined(value) ? (int)value : (int)Language.English;
    }

    /// <summary>The culture for resource lookups in <see cref="Current"/>.</summary>
    public static CultureInfo Culture => Languages.Culture(Current);

    /// <summary>
    /// <paramref name="key"/> from <paramref name="resources"/> in the UI language,
    /// falling back to English (the neutral resources), then to the key itself.
    /// </summary>
    public static string GetString(ResourceManager resources, string key)
    {
        try
        {
            return resources.GetString(key, Culture) ?? key;
        }
        catch (MissingManifestResourceException)
        {
            return key;
        }
    }

    /// <summary>Formats the UI string <paramref name="key"/> with <paramref name="args"/>.</summary>
    public static string Format(ResourceManager resources, string key, params object?[] args) =>
        string.Format(Culture, GetString(resources, key), args);
}
