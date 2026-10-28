using System.Text.Json;
using System.Text.Json.Serialization;
using GanttIt.Core.Model;

namespace GanttIt.Core.Serialization;

public enum LoadStatus
{
    /// <summary>Loaded and editable.</summary>
    Ok,

    /// <summary>
    /// Written by a newer add-in (higher SchemaVersion). <see cref="LoadResult{T}.Value"/>
    /// holds a structurally valid best-effort read for display, or null when none
    /// could be made; it must not be edited or saved.
    /// </summary>
    NewerVersion,

    /// <summary>Not valid JSON, or not a valid document. <see cref="LoadResult{T}.Error"/> says why.</summary>
    Invalid,
}

/// <summary>The outcome of reading stored JSON. Loading never throws.</summary>
public sealed record LoadResult<T>(LoadStatus Status, T? Value, string? Error) where T : class
{
    /// <summary>True only for a document this version may edit and save.</summary>
    public bool IsEditable => Status == LoadStatus.Ok;

    internal static LoadResult<T> Invalid(string error) => new(LoadStatus.Invalid, null, error);
}

/// <summary>
/// Shared System.Text.Json plumbing for versioned documents: camelCase names,
/// string enums, <c>#RRGGBB</c> colors, ISO dates. Culture-invariant.
/// </summary>
internal static class JsonStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(), new RgbJsonConverter() },
    };

    public static string Serialize<T>(T value, int version, int currentVersion, string kind)
    {
        if (version > currentVersion)
            throw new InvalidOperationException(
                $"This {kind} has SchemaVersion {version}, newer than {currentVersion}. Refusing to rewrite it in an older format.");
        if (version < currentVersion)
            throw new InvalidOperationException($"This {kind} has SchemaVersion {version}; migrate it before saving.");
        return JsonSerializer.Serialize(value, Options);
    }

    /// <summary>
    /// Reads <paramref name="json"/>: rejects malformed text, reads the version
    /// first, then deserializes and validates. Never throws.
    /// </summary>
    public static LoadResult<T> Deserialize<T>(string? json, int currentVersion, Func<T, string?> validate)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return LoadResult<T>.Invalid("The stored JSON is empty.");

        int version;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return LoadResult<T>.Invalid("The stored JSON is not an object.");
            if (!doc.RootElement.TryGetProperty("schemaVersion", out var v)
                || v.ValueKind != JsonValueKind.Number
                || !v.TryGetInt32(out version))
                return LoadResult<T>.Invalid("The stored JSON has no integer schemaVersion.");
        }
        catch (JsonException ex)
        {
            return LoadResult<T>.Invalid($"The stored JSON is malformed: {ex.Message}");
        }

        if (version < 1) return LoadResult<T>.Invalid($"Unsupported schemaVersion {version}.");

        if (version > currentVersion)
        {
            // Show what we can, but never edit or downgrade it. The preview passes the
            // same structural checks as an editable load, so displaying it (e.g. a
            // home-screen thumbnail) can't fail; otherwise there is no preview.
            var preview = TryRead<T>(json, out _);
            if (preview is not null && validate(preview) is not null) preview = null;
            return new LoadResult<T>(LoadStatus.NewerVersion, preview,
                $"Written by a newer version of Gantt-It (schema {version}; this version reads {currentVersion}). It is read-only here.");
        }

        // Older versions would be migrated forward here; version 1 is the first.
        var value = TryRead<T>(json, out var error);
        if (value is null) return LoadResult<T>.Invalid(error ?? "The stored JSON is empty.");
        var problem = validate(value);
        return problem is null ? new LoadResult<T>(LoadStatus.Ok, value, null) : LoadResult<T>.Invalid(problem);
    }

    private static T? TryRead<T>(string json, out string? error) where T : class
    {
        error = null;
        try
        {
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or FormatException or ArgumentException or InvalidOperationException)
        {
            error = $"The stored JSON does not match the expected shape: {ex.Message}";
            return null;
        }
    }
}

/// <summary>Reads and writes <see cref="Rgb"/> as <c>"#RRGGBB"</c>.</summary>
internal sealed class RgbJsonConverter : JsonConverter<Rgb>
{
    public override Rgb Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String && Rgb.TryParse(reader.GetString(), out var color)
            ? color
            : throw new JsonException("Expected a color string \"#RRGGBB\".");

    public override void Write(Utf8JsonWriter writer, Rgb value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToHex());
}
