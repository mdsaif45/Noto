using System.Text;
using System.Text.Json;
using Noto.Core.Themes;
using Xunit;

namespace Noto.Core.Tests.Themes;

/// <summary>#269 (ADR-023 §1, §2, §5, §9): a theme file is read as untrusted data, and only the properties ADR-023 lists.</summary>
public sealed class ThemeFileTests
{
    [Theory]
    [InlineData("#abc", 0xAA, 0xBB, 0xCC, 0xFF)]
    [InlineData("#A1b2C3", 0xA1, 0xB2, 0xC3, 0xFF)]
    [InlineData("#a1b2c380", 0xA1, 0xB2, 0xC3, 0x80)]
    public void A_colour_is_rgb_rrggbb_or_rrggbbaa_with_alpha_last_in_either_case(string text, int r, int g, int b, int a)
    {
        Assert.True(ThemeColor.TryParse(text, out ThemeColor colour));
        Assert.Equal(new ThemeColor((byte)r, (byte)g, (byte)b, (byte)a), colour);
    }

    [Theory]
    [InlineData("#12")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("red")]
    [InlineData("rgb(1,2,3)")]
    [InlineData("{ThemeResource TextPrimaryColor}")]
    [InlineData("#ggg")]
    [InlineData("abcdef")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_else_is_not_a_colour(string? text) => Assert.False(ThemeColor.TryParse(text, out _));

    [Fact]
    public void Contrast_is_wcag_and_composites_a_translucent_foreground()
    {
        Assert.Equal(21, ThemeColor.Contrast(new(0, 0, 0), new(255, 255, 255)), 2);
        Assert.Equal(1, ThemeColor.Contrast(new(0, 0, 0, 0), new(255, 255, 255)), 2);
    }

    [Fact]
    public void The_example_reads_with_no_warning_and_any_is_merged_property_by_property_and_part_by_part()
    {
        ThemeFile theme = Read(Example());

        Assert.Equal("Harbour", theme.Name);
        Assert.Equal("Noto", theme.Author);
        Assert.Empty(theme.Warnings);
        Assert.Empty(theme.Errors);

        // any.danger in light; dark's own danger over it.
        Assert.Equal(Colour("#c4314b"), theme.ColoursFor(ThemeMode.Light)["danger"]);
        Assert.Equal(Colour("#ff99a4"), theme.ColoursFor(ThemeMode.Dark)["danger"]);

        // any.note6.name with light.note6.tint: the parts combine.
        Assert.Equal("Slate", theme.NoteNamesFor(ThemeMode.Light)[5]);
        Assert.Equal("Slate", theme.NoteNamesFor(ThemeMode.Dark)[5]);
        Assert.Null(theme.NoteNamesFor(ThemeMode.Light)[0]);
        Assert.Equal(Colour("#64748b40"), theme.ColoursFor(ThemeMode.Light)["note6.tint"]);

        // #fff is doubled; the flattened and kept alphas are still there to flatten or keep (§5).
        Assert.Equal(Colour("#ffffff"), theme.ColoursFor(ThemeMode.Dark)["highlightText"]);
        Assert.Equal(Colour("#ffffff14"), theme.ColoursFor(ThemeMode.Dark)["code"]);
        Assert.Equal(Colour("#ffffff1f"), theme.ColoursFor(ThemeMode.Dark)["border"]);
    }

    [Fact]
    public void The_parsers_property_list_is_the_schemas()
    {
        // ADR-023 §10: the schema in docs is what editors complete from; Noto's own parser does the checking. Read from
        // docs at test time (no embedded copy that nothing at run time would read).
        using JsonDocument schema = JsonDocument.Parse(File.ReadAllText(DocsPath("noto-theme.schema.json")));
        JsonElement root = schema.RootElement;
        static string[] Names(JsonElement properties) => [.. properties.EnumerateObject().Select(p => p.Name)];

        Assert.Equal(["$schema", "version", "name", "author", "light", "dark", "any"], Names(root.GetProperty("properties")));
        Assert.Equal([.. ThemeFile.Colours, .. ThemeFile.Notes], Names(root.GetProperty("$defs").GetProperty("section").GetProperty("properties")));
        Assert.Equal(ThemeFile.NoteParts, Names(root.GetProperty("$defs").GetProperty("note").GetProperty("properties")));
        Assert.Equal(ThemeFile.Version, root.GetProperty("properties").GetProperty("version").GetProperty("const").GetInt32());
    }

    [Fact]
    public void Comments_trailing_commas_and_a_byte_order_mark_are_accepted()
    {
        string text = "// a theme\n{ \"version\": 1, /* mine */ \"name\": \"Mine\", \"dark\": { \"text\": \"#fff\", }, }";
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(text)];

        Assert.Equal("Mine", Read(bytes).Name);
    }

    [Fact]
    public void Unknown_properties_are_warned_at_every_level_never_refused()
    {
        ThemeFile theme = Read("""{ "version": 1, "name": "N", "extra": 1, "light": { "backgound": "#fff", "text": "#000", "note1": { "glow": "#fff" } } }""");

        Assert.Empty(theme.Errors);
        Assert.Equal(3, theme.Warnings.Count);
        Assert.Contains(theme.Warnings, w => w.Contains("\"extra\"", StringComparison.Ordinal));
        Assert.Contains(theme.Warnings, w => w.Contains("\"light.backgound\"", StringComparison.Ordinal));
        Assert.Contains(theme.Warnings, w => w.Contains("\"light.note1.glow\"", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("""{ "version": 1, "name": "N", "name": "M", "dark": { "text": "#fff" } }""", "twice")]
    [InlineData("""{ "version": 1, "name": "N", "dark": { "text": "#fff", "text": "#000" } }""", "twice")]
    [InlineData("""{ "version": 2, "name": "N", "dark": { "text": "#fff" } }""", "newer Noto")]
    [InlineData("""{ "name": "N", "dark": { "text": "#fff" } }""", "format version")]
    [InlineData("""{ "version": 1, "dark": { "text": "#fff" } }""", "no name")]
    [InlineData("""{ "version": 1, "name": "  ", "dark": { "text": "#fff" } }""", "empty")]
    [InlineData("""{ "version": 1, "name": "default", "dark": { "text": "#fff" } }""", "Default")]
    [InlineData("""{ "version": 1, "name": "N\u202Eevil", "dark": { "text": "#fff" } }""", "can't be shown")]
    [InlineData("""{ "version": 1, "name": "N\u0007", "dark": { "text": "#fff" } }""", "can't be shown")]
    [InlineData("""{ "version": 1, "name": "N" }""", "no colours")]
    [InlineData("""{ "version": 1, "name": "N", "dark": {} }""", "no colours")]
    [InlineData("""[1, 2]""", "JSON object")]
    [InlineData("""{ "version": 1, "name": "N", """, "valid JSON")]
    public void A_file_that_is_not_a_theme_is_refused_with_the_reason(string json, string reason)
    {
        Assert.Null(ThemeFile.Read(Encoding.UTF8.GetBytes(json), out string? refusal));
        Assert.Contains(reason, refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void A_name_longer_than_64_characters_is_refused()
    {
        Assert.Null(ThemeFile.Read(Encoding.UTF8.GetBytes($$"""{ "version": 1, "name": "{{new string('a', 65)}}", "dark": { "text": "#fff" } }"""), out string? refusal));
        Assert.Contains("longer than 64", refusal, StringComparison.Ordinal);
        Assert.NotNull(Read($$"""{ "version": 1, "name": "{{new string('a', 64)}}", "dark": { "text": "#fff" } }"""));
    }

    [Fact]
    public void The_size_and_the_depth_are_capped_and_invalid_utf8_is_refused()
    {
        byte[] large = Encoding.UTF8.GetBytes("{ \"version\": 1, \"name\": \"N\", \"dark\": { \"text\": \"#fff\" }, \"pad\": \"" + new string(' ', ThemeFile.MaxBytes) + "\" }");
        Assert.Null(ThemeFile.Read(large, out string? refusal));
        Assert.Contains("64 KB", refusal, StringComparison.Ordinal);

        string deep = """{ "version": 1, "name": "N", "dark": { "text": "#fff" }, "x": """ + string.Concat(Enumerable.Repeat("[", 9)) + string.Concat(Enumerable.Repeat("]", 9)) + "}";
        Assert.Null(ThemeFile.Read(Encoding.UTF8.GetBytes(deep), out refusal));

        byte[] invalid = [.. Encoding.UTF8.GetBytes("{ \"version\": 1, \"name\": \"N"), 0xFF, .. Encoding.UTF8.GetBytes("\", \"dark\": { \"text\": \"#fff\" } }")];
        Assert.Null(ThemeFile.Read(invalid, out refusal));
    }

    [Fact]
    public void A_property_of_the_wrong_type_or_format_is_an_error_and_is_not_set()
    {
        ThemeFile theme = Read("""{ "version": 1, "name": "N", "author": 3, "light": { "text": "black", "card": 12, "note1": { "name": "" } }, "dark": "#000", "any": { "link": "#123456" } }""");

        Assert.Equal(5, theme.Errors.Count);
        Assert.False(theme.ColoursFor(ThemeMode.Light).ContainsKey("text"));
        Assert.Equal(["link"], theme.ColoursFor(ThemeMode.Light).Keys);
        Assert.Null(theme.Author);
    }

    [Fact]
    public void Alpha_is_made_opaque_with_a_warning_except_where_it_is_kept_or_flattened()
    {
        ThemeFile theme = Read("""{ "version": 1, "name": "N", "light": { "text": "#00000080", "background": "#ffffff80", "border": "#00000020", "code": "#00000010", "highlight": "#ffff0080", "note1": { "tint": "#ff000040", "marker": "#ff000080" } } }""");
        Dictionary<string, ThemeColor> light = theme.ColoursFor(ThemeMode.Light);

        Assert.Equal(3, theme.Warnings.Count);
        Assert.True(light["text"].IsOpaque);
        Assert.True(light["background"].IsOpaque);
        Assert.True(light["note1.marker"].IsOpaque);
        Assert.Equal(0x20, light["border"].A);
        Assert.Equal(0x40, light["note1.tint"].A);
        Assert.Equal(0x10, light["code"].A);
        Assert.Equal(0x80, light["highlight"].A);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("Harbour.nototheme", true)]
    [InlineData("Harbour (2).NOTOTHEME", true)]
    [InlineData(".nototheme", false)]
    [InlineData("Harbour.json", false)]
    [InlineData(@"..\Harbour.nototheme", false)]
    [InlineData("a..b.nototheme", false)]
    [InlineData(@"C:\themes\Harbour.nototheme", false)]
    [InlineData("sub/Harbour.nototheme", false)]
    [InlineData(" Harbour.nototheme", false)]
    [InlineData("Har\nbour.nototheme", false)]
    public void The_setting_keeps_only_a_bare_installed_file_name(string value, bool kept) =>
        Assert.Equal(kept, ThemeFile.IsFileName(value));

    internal static ThemeFile Read(string json) => Read(Encoding.UTF8.GetBytes(json));

    internal static ThemeFile Read(byte[] bytes)
    {
        ThemeFile? theme = ThemeFile.Read(bytes, out string? refusal);
        Assert.True(theme is not null, refusal);
        return theme;
    }

    internal static byte[] Example() => File.ReadAllBytes(DocsPath("example.nototheme"));

    internal static ThemeColor Colour(string text) => ThemeColor.TryParse(text, out ThemeColor c) ? c : throw new ArgumentException(text);

    /// <summary>A file in docs/themes, found by walking up to the repository; a test that silently skips is worse than none.</summary>
    internal static string DocsPath(string file)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Join(directory.FullName, "docs", "themes", file);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Could not locate docs/themes/{file} from {AppContext.BaseDirectory}");
    }
}
