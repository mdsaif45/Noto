using System.Globalization;
using System.Text.Json;

namespace Noto.Core.Themes;

/// <summary>Which of Noto's two looks a theme's colours are for (ADR-023 §1): Settings' Mode decides which shows.</summary>
public enum ThemeMode
{
    Light,
    Dark,
}

/// <summary>
/// A theme file (<c>.nototheme</c>, ADR-023 §1, §2, §9; #269): JSON naming colours for the properties ADR-023 lists, in
/// <c>light</c>, <c>dark</c> and <c>any</c> sections. It is untrusted data and only data: read with
/// <see cref="JsonDocument"/>, capped, every string and colour checked here, nothing from it ever reaching XAML.
/// </summary>
/// <remarks>
/// <para>
/// <b>Refused, or read with problems.</b> A file that is not a theme at all (not JSON, too large, too deep, a property
/// set twice, a newer version, a bad name, no colour) is refused by <see cref="Read"/>. A property of the wrong type or a
/// colour in no allowed format is an <see cref="Errors"/> entry: install refuses it, a load ignores that property. An
/// unknown property is a <see cref="Warnings"/> entry, so a file written for a newer Noto still loads; so is an alpha
/// made opaque (§5).
/// </para>
/// <para>
/// <b>Alpha</b> (§5) is applied as it is read where it does not depend on another colour: the colours that are always
/// opaque are made so here. <c>code</c> and <c>highlight</c> are flattened over the card later, when the card is known
/// (<see cref="ThemePalette.Resolve"/>); <c>border</c> and a note's <c>tint</c> keep theirs.
/// </para>
/// </remarks>
public sealed class ThemeFile
{
    /// <summary>A theme file's extension.</summary>
    public const string Extension = ".nototheme";

    /// <summary>The largest theme file read, in bytes (§9): a complete theme is about 2 KiB.</summary>
    public const int MaxBytes = 64 * 1024;

    /// <summary>The only format version this Noto reads.</summary>
    public const int Version = 1;

    /// <summary>The name Noto's own colours go by; no theme may take it.</summary>
    public const string DefaultName = "Default";

    /// <summary>The colour properties a section may set, other than the note colours (§2), in the schema's order.</summary>
    public static IReadOnlyList<string> Colours { get; } =
    [
        "background", "card", "code", "text", "textSecondary", "textDisabled", "link", "quote", "checkmark", "danger",
        "accent", "border", "highlight", "highlightText",
    ];

    /// <summary>The note colours' properties, <c>note1</c> (Ctrl+1) to <c>note6</c>.</summary>
    public static IReadOnlyList<string> Notes { get; } = ["note1", "note2", "note3", "note4", "note5", "note6"];

    /// <summary>The parts of a note colour: two colours and a name.</summary>
    public static IReadOnlyList<string> NoteParts { get; } = ["tint", "marker", "name"];

    /// <summary>Every colour key a palette has for a theme to set: <see cref="Colours"/>, then each note's tint and marker.</summary>
    public static IReadOnlyList<string> ColourKeys { get; } =
        [.. Colours, .. Notes.SelectMany(n => new[] { $"{n}.tint", $"{n}.marker" })];

    // §5: drawn opaque; code and highlight are flattened over the card; border and a tint keep their alpha.
    private static readonly HashSet<string> Translucent = ["code", "highlight", "border", .. Notes.Select(n => $"{n}.tint")];

    private readonly Dictionary<string, ThemeColor>[] _colours = [[], [], []];
    private readonly Dictionary<int, string>[] _names = [[], [], []];
    private readonly List<string> _warnings = [];
    private readonly List<string> _errors = [];

    private ThemeFile()
    {
    }

    /// <summary>What Settings shows; a theme's identity, ignoring case (§8).</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Who made it, when it says.</summary>
    public string? Author { get; private set; }

    /// <summary>What was ignored without harm: unknown properties, an alpha made opaque.</summary>
    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>Properties that could not be read: install refuses the file; a load ignores each.</summary>
    public IReadOnlyList<string> Errors => _errors;

    /// <summary>
    /// The colours the theme sets for <paramref name="mode"/>: <c>any</c>, with the mode's own section over it property
    /// by property (a note colour part by part). A key absent is Noto's own.
    /// </summary>
    public Dictionary<string, ThemeColor> ColoursFor(ThemeMode mode) => Merge(_colours, mode);

    /// <summary>The note colours' names for <paramref name="mode"/>, by position (0 is note1); <see langword="null"/> for Noto's own.</summary>
    public IReadOnlyList<string?> NoteNamesFor(ThemeMode mode)
    {
        Dictionary<int, string> names = Merge(_names, mode);
        return [.. Enumerable.Range(0, Notes.Count).Select(i => names.GetValueOrDefault(i))];
    }

    /// <summary>
    /// Whether <paramref name="value"/> may be stored as <c>appearance.theme-file</c> (§8): empty for Default, or a bare
    /// <c>*.nototheme</c> file name, with no separator, no <c>..</c> and nothing Windows forbids in a name, so no value
    /// can point outside the themes folder.
    /// </summary>
    public static bool IsFileName(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value.Length == 0
            || (value.Length > Extension.Length
                && value.Length <= 100
                && value.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)
                && !value.Contains("..", StringComparison.Ordinal)
                && value.Trim() == value
                && !value.Any(c => char.IsControl(c) || c is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*'));
    }

    /// <summary>
    /// Reads a theme from its bytes (UTF-8, a byte order mark allowed).
    /// </summary>
    /// <param name="bytes">The whole file.</param>
    /// <param name="refusal">Why it is not a theme Noto can read, in words; <see langword="null"/> when it is one.</param>
    /// <returns>The theme, or <see langword="null"/> when refused.</returns>
    public static ThemeFile? Read(ReadOnlyMemory<byte> bytes, out string? refusal)
    {
        if (bytes.Length > MaxBytes)
        {
            refusal = "It's larger than 64 KB, so it isn't a theme.";
            return null;
        }

        if (bytes.Span.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            bytes = bytes[3..];
        }

        var theme = new ThemeFile();
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions
            {
                MaxDepth = 8,
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });

            refusal = Duplicate(document.RootElement) is { } twice ? $"It sets \"{twice}\" twice." : theme.ReadRoot(document.RootElement);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // InvalidOperationException: text that is not UTF-8, found only when a string is read.
            refusal = "It isn't a theme: it isn't valid JSON in UTF-8.";
        }

        return refusal is null ? theme : null;
    }

    private string? ReadRoot(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return "It isn't a theme: it isn't a JSON object.";
        }

        if (!root.TryGetProperty("version", out JsonElement version) || !version.TryGetInt32(out int number) || number < 1)
        {
            return "It isn't a theme: it has no format version.";
        }

        if (number > Version)
        {
            return "It was made for a newer Noto.";
        }

        if (!root.TryGetProperty("name", out JsonElement name) || name.ValueKind != JsonValueKind.String)
        {
            return "It has no name.";
        }

        string named = name.GetString()!.Trim();
        if (NameProblem(named, 64) is { } problem)
        {
            return $"Its name {problem}.";
        }

        if (string.Equals(named, DefaultName, StringComparison.OrdinalIgnoreCase))
        {
            return "Its name can't be Default: that is Noto's own colours.";
        }

        Name = named;
        bool sections = false;

        foreach (JsonProperty property in root.EnumerateObject())
        {
            switch (property.Name)
            {
                case "version" or "name":
                    break;
                case "$schema":
                    Expect(property.Value, JsonValueKind.String, "$schema");
                    break;
                case "author":
                    ReadAuthor(property.Value);
                    break;
                case "any" or "light" or "dark":
                    sections = true;
                    if (Expect(property.Value, JsonValueKind.Object, property.Name))
                    {
                        int at = property.Name switch { "any" => 0, "light" => 1, _ => 2 };
                        ReadSection(property.Value, property.Name, _colours[at], _names[at]);
                    }

                    break;
                default:
                    _warnings.Add($"Noto doesn't know \"{property.Name}\", so it is ignored.");
                    break;
            }
        }

        return sections && _colours.Any(c => c.Count > 0) ? null : "It sets no colours.";
    }

    private void ReadAuthor(JsonElement value)
    {
        if (!Expect(value, JsonValueKind.String, "author"))
        {
            return;
        }

        string author = value.GetString()!.Trim();
        if (author.Length > 0 && NameProblem(author, 64) is { } problem)
        {
            _errors.Add($"The author {problem}.");
            return;
        }

        Author = author.Length == 0 ? null : author;
    }

    private void ReadSection(JsonElement section, string path, Dictionary<string, ThemeColor> colours, Dictionary<int, string> names)
    {
        foreach (JsonProperty property in section.EnumerateObject())
        {
            string at = $"{path}.{property.Name}";
            int note = Notes.ToList().IndexOf(property.Name);

            if (Colours.Contains(property.Name))
            {
                ReadColour(property.Value, at, property.Name, colours);
            }
            else if (note >= 0)
            {
                if (Expect(property.Value, JsonValueKind.Object, at))
                {
                    ReadNote(property.Value, at, property.Name, note, colours, names);
                }
            }
            else
            {
                _warnings.Add($"Noto doesn't know \"{at}\", so it is ignored.");
            }
        }
    }

    private void ReadNote(JsonElement value, string path, string key, int note, Dictionary<string, ThemeColor> colours, Dictionary<int, string> names)
    {
        foreach (JsonProperty part in value.EnumerateObject())
        {
            string at = $"{path}.{part.Name}";
            switch (part.Name)
            {
                case "tint" or "marker":
                    ReadColour(part.Value, at, $"{key}.{part.Name}", colours);
                    break;
                case "name":
                    if (Expect(part.Value, JsonValueKind.String, at))
                    {
                        string name = part.Value.GetString()!.Trim();
                        if (NameProblem(name, 24) is { } problem)
                        {
                            _errors.Add($"\"{at}\" {problem}.");
                        }
                        else
                        {
                            names[note] = name;
                        }
                    }

                    break;
                default:
                    _warnings.Add($"Noto doesn't know \"{at}\", so it is ignored.");
                    break;
            }
        }
    }

    private void ReadColour(JsonElement value, string path, string key, Dictionary<string, ThemeColor> colours)
    {
        if (value.ValueKind != JsonValueKind.String || !ThemeColor.TryParse(value.GetString(), out ThemeColor colour))
        {
            _errors.Add($"\"{path}\" isn't a colour: use #rgb, #rrggbb or #rrggbbaa.");
            return;
        }

        if (!colour.IsOpaque && !Translucent.Contains(key))
        {
            _warnings.Add($"\"{path}\" is drawn opaque: its alpha is ignored.");
            colour = colour.Opaque;
        }

        colours[key] = colour;
    }

    private bool Expect(JsonElement value, JsonValueKind kind, string path)
    {
        if (value.ValueKind == kind)
        {
            return true;
        }

        _errors.Add($"\"{path}\" should be {(kind == JsonValueKind.Object ? "an object" : "text")}.");
        return false;
    }

    /// <summary>What is wrong with a trimmed name of at most <paramref name="max"/> characters, or <see langword="null"/>.</summary>
    private static string? NameProblem(string name, int max) =>
        name.Length == 0 ? "is empty"
        : name.Length > max ? string.Create(CultureInfo.InvariantCulture, $"is longer than {max} characters")
        : name.Any(c => char.GetUnicodeCategory(c) is UnicodeCategory.Control or UnicodeCategory.Format) ? "has characters that can't be shown"
        : null;

    /// <summary>The first property name set twice in one object, anywhere in the document: which one wins would be a guess.</summary>
    private static string? Duplicate(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!seen.Add(property.Name))
                {
                    return property.Name;
                }

                if (Duplicate(property.Value) is { } inner)
                {
                    return inner;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                if (Duplicate(item) is { } inner)
                {
                    return inner;
                }
            }
        }

        return null;
    }

    private static Dictionary<TKey, TValue> Merge<TKey, TValue>(Dictionary<TKey, TValue>[] sections, ThemeMode mode)
        where TKey : notnull
    {
        var merged = new Dictionary<TKey, TValue>(sections[0]);
        foreach ((TKey key, TValue value) in sections[mode == ThemeMode.Light ? 1 : 2])
        {
            merged[key] = value;
        }

        return merged;
    }
}
