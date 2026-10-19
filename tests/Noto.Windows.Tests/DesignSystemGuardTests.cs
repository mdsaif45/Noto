using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// Slice 1 (interaction foundation): the design system's rules, held by the build (ADR-011). The XAML is read as XML,
/// not run: an unresolved resource ends the process at start-up (0xC0000C04) before any handler sees it, and the
/// themes, contrast and states are properties of the dictionaries themselves. What a running window shows is validated
/// on a frozen build, with screenshots, separately.
/// </summary>
public sealed class DesignSystemGuardTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly string[] Themes = ["Light", "Dark", "HighContrast"];

    private static readonly string DesignSystem = Path.GetDirectoryName(Path.GetDirectoryName(SourcePath("src", "Noto.UI", "Themes", "Generic.xaml")))!;
    private static readonly string MainWindowPath = SourcePath("src", "Noto.Windows", "MainWindow.xaml");

    // The OS supplies these to every dictionary: high contrast's palette and the user's accent.
    private static readonly Regex SystemKey = new(@"^System(Color\w+Color|AccentColor((Light|Dark)[123])?)$");

    private static readonly Regex Reference = new(@"\{(StaticResource|ThemeResource)\s+(?:ResourceKey=)?(\w+)\s*\}");

    // ---------------------------------------------------------------- themes

    [Fact]
    public void Every_theme_defines_exactly_the_same_keys()
    {
        // ADR-011: "No token may exist in only one theme — a missing dark value is a bug, not a fallback."
        var keys = Themes.ToDictionary(t => t, t => ThemeDictionary(t).Elements().Select(KeyOf).ToHashSet());

        Assert.True(keys["Light"].Count > 0);
        foreach (string theme in Themes)
        {
            Assert.True(keys["Light"].SetEquals(keys[theme]), $"{theme} differs from Light: {string.Join(", ", keys["Light"].Except(keys[theme]).Concat(keys[theme].Except(keys["Light"])))}");
        }
    }

    [Fact]
    public void Colours_and_brushes_are_declared_only_inside_a_theme_dictionary()
    {
        // A brush outside the theme dictionaries is one object shared by every theme, so it cannot follow a theme set
        // on the window's content at run time (Slice 1's Light/Dark setting). One per theme, as WinUI declares its own.
        var outside = DesignSystemFiles()
            .SelectMany(f => Load(f).Descendants()
                .Where(e => e.Name.LocalName is "SolidColorBrush" or "Color" && e.Attribute(X + "Key") is not null)
                .Where(e => !e.Ancestors(Xaml + "ResourceDictionary").Any(d => Themes.Contains((string?)d.Attribute(X + "Key"))))
                .Select(e => $"{Path.GetFileName(f)}: {KeyOf(e)}"))
            .ToList();

        Assert.True(outside.Count == 0, "Outside a theme dictionary: " + string.Join(", ", outside));
    }

    [Fact]
    public void Text_and_the_focus_ring_meet_WCAG_contrast_in_light_and_dark()
    {
        // Text 4.5:1 (WCAG 1.4.3); the focus ring 3:1 against every fill it is drawn on (1.4.11). High contrast is the
        // user's own palette and the selection pill is the user's accent, so neither is Noto's to measure. Disabled
        // text is exempt by definition.
        (string Fore, string Back, double Minimum)[] pairs =
        [
            ("TextPrimaryColor", "SurfaceSidebarColor", 4.5),
            ("TextPrimaryColor", "SurfaceInputColor", 4.5),
            ("TextSecondaryColor", "SurfaceSidebarColor", 4.5),
            ("TextSecondaryColor", "SurfaceInputColor", 4.5),
            ("TextDangerColor", "SurfaceSidebarColor", 4.5),
            ("InteractionTextColor", "InteractionHoverColor", 4.5),
            ("InteractionTextColor", "InteractionPressedColor", 4.5),
            ("InteractionSelectedTextColor", "InteractionSelectedColor", 4.5),
            ("BorderFocusColor", "SurfaceSidebarColor", 3),
            ("BorderFocusColor", "SurfaceInputColor", 3),
            ("BorderFocusColor", "InteractionHoverColor", 3),
            ("BorderFocusColor", "InteractionPressedColor", 3),
            ("BorderFocusColor", "InteractionSelectedColor", 3),
        ];

        var failures = new List<string>();

        foreach (string theme in new[] { "Light", "Dark" })
        {
            var colours = ThemeDictionary(theme);

            foreach (var (fore, back, minimum) in pairs)
            {
                double ratio = Contrast(ColourOf(colours, fore), ColourOf(colours, back));

                if (ratio < minimum)
                {
                    failures.Add($"{theme} {fore} on {back}: {ratio:F2} < {minimum}");
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join("; ", failures));
    }

    [Fact]
    public void High_contrast_pairs_every_highlight_with_highlight_text_and_draws_focus_in_the_users_colours()
    {
        // Hover and selection both fill with Highlight there, as in WinUI's own list; the text on it must be
        // HighlightText, the colour the user paired with it, and the pill and weight tell selection apart.
        var hc = ThemeDictionary("HighContrast");

        Assert.Equal("SystemColorHighlightColor", Target(hc, "InteractionHoverBrush"));
        Assert.Equal("SystemColorHighlightColor", Target(hc, "InteractionPressedBrush"));
        Assert.Equal("SystemColorHighlightColor", Target(hc, "InteractionSelectedBrush"));
        Assert.Equal("SystemColorHighlightTextColor", Target(hc, "InteractionTextBrush"));
        Assert.Equal("SystemColorHighlightTextColor", Target(hc, "InteractionSelectedTextBrush"));
        Assert.Equal("SystemColorHighlightTextColor", Target(hc, "SelectionIndicatorBrush"));
        Assert.Equal("SystemColorWindowTextColor", Target(hc, "BorderFocusBrush"));
        Assert.Equal("SystemColorWindowColor", Target(hc, "BorderFocusInnerBrush"));

        // Nothing in high contrast is a colour Noto chose.
        Assert.Empty(hc.Elements(Xaml + "Color"));
    }

    // ---------------------------------------------------------------- resolution

    [Fact]
    public void Every_resource_reference_resolves_within_its_own_dictionarys_merge_tree_and_is_defined_before_use()
    {
        // ADR-011 rule 6: a reference inside a merged dictionary resolves against that dictionary and what IT merges,
        // never a sibling. Inside a theme dictionary, only that theme's keys. A StaticResource must also be defined
        // before it is used. Each failure here is a start-up crash, not a wrong colour.
        var failures = new List<string>();

        foreach (string file in DesignSystemFiles())
        {
            var document = Load(file);
            var merged = Closure(file).Where(f => f != file).SelectMany(f => KeysOf(Load(f))).ToHashSet();
            var elements = document.Descendants().ToList();

            foreach (var (element, kind, key) in References(document))
            {
                if (SystemKey.IsMatch(key))
                {
                    continue;
                }

                XElement? theme = element.Ancestors(Xaml + "ResourceDictionary").FirstOrDefault(d => Themes.Contains((string?)d.Attribute(X + "Key")));
                var local = (theme?.Descendants() ?? document.Descendants())
                    .Where(e => KeyOf(e) == key && e.Parent?.Name.LocalName != "ThemeDictionaries")
                    .ToList();

                if (local.Count == 0 && (theme is not null || !merged.Contains(key)))
                {
                    failures.Add($"{Path.GetFileName(file)}: {key} does not resolve{(theme is null ? string.Empty : " in " + KeyOf(theme))}");
                }
                else if (kind == "StaticResource" && local.Count > 0 && !merged.Contains(key) && elements.IndexOf(local[0]) > elements.IndexOf(element))
                {
                    failures.Add($"{Path.GetFileName(file)}: {key} is used before it is defined");
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join("; ", failures));
    }

    [Fact]
    public void The_window_uses_only_the_design_systems_own_resources()
    {
        // Views consume Noto's semantic tokens and components, never WinUI's or a primitive (ADR-011), and every one it
        // names exists in the dictionary the application merges.
        var available = Closure(Path.Join(DesignSystem, "Themes", "Generic.xaml")).SelectMany(f => KeysOf(Load(f))).ToHashSet();
        var missing = References(Load(MainWindowPath)).Select(r => r.Key).Where(k => !available.Contains(k)).Distinct().ToList();

        Assert.True(missing.Count == 0, "Not in the design system: " + string.Join(", ", missing));
    }

    // ---------------------------------------------------------------- literals

    [Fact]
    public void No_view_or_component_style_carries_a_literal_visual_value()
    {
        // ADR-011 rule 1. Values live in Tokens.xaml and Colors.xaml only; everything else names a token. Zero, Auto, *
        // and Transparent are absences, not values, and 0/1 opacity is a state switch.
        string[] visual =
        [
            "Background", "Foreground", "BorderBrush", "Fill", "Stroke", "Color", "Margin", "Padding", "FontSize",
            "FontFamily", "FontWeight", "CornerRadius", "BorderThickness", "Width", "Height", "MinWidth", "MinHeight",
            "MaxWidth", "MaxHeight", "Spacing", "RowSpacing", "ColumnSpacing", "LineHeight", "Opacity", "Duration",
        ];

        var files = Directory.EnumerateFiles(Path.Join(DesignSystem, "Styles"), "*.xaml").Append(MainWindowPath);
        var literals = new List<string>();

        foreach (string file in files)
        {
            foreach (XElement element in Load(file).Descendants())
            {
                var settings = element.Name.LocalName == "Setter"
                    ? [((string?)element.Attribute("Property") ?? (string?)element.Attribute("Target") ?? string.Empty, (string?)element.Attribute("Value") ?? string.Empty)]
                    : element.Attributes().Select(a => (a.Name.LocalName, a.Value));

                foreach (var (property, value) in settings)
                {
                    string name = property.Split('.')[^1];

                    if (visual.Contains(name) && !IsAllowed(name, value))
                    {
                        literals.Add($"{Path.GetFileName(file)}: {property}=\"{value}\"");
                    }
                }

                literals.AddRange(element.Attributes()
                    .Where(a => Regex.IsMatch(a.Value, "#[0-9A-Fa-f]{3,8}\\b"))
                    .Select(a => $"{Path.GetFileName(file)}: {a.Name.LocalName}=\"{a.Value}\""));
            }
        }

        Assert.True(literals.Count == 0, "Literal visual values: " + string.Join("; ", literals.Distinct()));

        static bool IsAllowed(string property, string value) =>
            value.StartsWith('{') || value is "0" or "Auto" or "*" or "Transparent" || (property == "Opacity" && value == "1");
    }

    // ---------------------------------------------------------------- states and focus

    [Fact]
    public void Every_control_template_draws_the_two_tone_focus_ring_for_keyboard_focus_only()
    {
        // ADR-011 rule 3, "focus is mandatory": a ring whose outer and inner strokes are opposite tones, so it shows on
        // every fill, shown in Focused and never in PointerFocused, so a click leaves no ring behind.
        var templates = DesignSystemFiles().SelectMany(f => Load(f).Descendants(Xaml + "ControlTemplate").Select(t => (File: f, Template: t))).ToList();
        var failures = new List<string>();

        Assert.NotEmpty(templates);

        foreach (var (file, template) in templates)
        {
            string name = $"{Path.GetFileName(file)}:{(string?)template.Attribute(X + "Key") ?? template.Ancestors(Xaml + "Style").Select(KeyOf).FirstOrDefault()}";
            XElement? ring = template.Descendants(Xaml + "Border").FirstOrDefault(b => (string?)b.Attribute(X + "Name") == "FocusRing");

            bool twoTone = ring is not null
                && (string?)ring.Attribute("BorderBrush") == "{ThemeResource BorderFocusBrush}"
                && ring.Elements(Xaml + "Border").Any(inner => (string?)inner.Attribute("BorderBrush") == "{ThemeResource BorderFocusInnerBrush}");
            bool keyboardOnly = Setters(State(template, "Focused")).Contains(("FocusRing.Opacity", "1"))
                && State(template, "PointerFocused") is { } pointer && Setters(pointer).Count == 0;

            if (!twoTone || !keyboardOnly)
            {
                failures.Add($"{name} (two-tone {twoTone}, keyboard only {keyboardOnly})");
            }
        }

        // A style that turns off the system's focus visual must bring its own.
        failures.AddRange(DesignSystemFiles().SelectMany(f => Load(f).Descendants(Xaml + "Style")
            .Where(s => s.Elements(Xaml + "Setter").Any(x => (string?)x.Attribute("Property") == "UseSystemFocusVisuals" && (string?)x.Attribute("Value") == "False"))
            .Where(s => !s.Elements(Xaml + "Setter").Any(x => (string?)x.Attribute("Property") == "Template"))
            .Select(s => $"{KeyOf(s)} hides the system focus visual and has no template")));

        Assert.True(failures.Count == 0, string.Join("; ", failures));
    }

    [Fact]
    public void A_list_row_has_every_state_and_only_selection_shows_the_pill()
    {
        var template = Load(Path.Join(DesignSystem, "Styles", "ListRow.xaml")).Descendants(Xaml + "ControlTemplate").Single();
        string[] selected = ["Selected", "PointerOverSelected", "PressedSelected"];
        string[] others = ["Normal", "PointerOver", "Pressed", "Disabled", "Focused", "PointerFocused"];

        foreach (string state in others.Concat(selected))
        {
            Assert.True(State(template, state) is not null, $"No {state} state");
        }

        foreach (XElement state in template.Descendants(Xaml + "VisualState"))
        {
            bool pill = Setters(state).Contains(("SelectionIndicator.Opacity", "1"));
            Assert.True(pill == selected.Contains((string?)state.Attribute(X + "Name")), $"{(string?)state.Attribute(X + "Name")} pill={pill}");
        }

        foreach (string row in new[] { "FolderRow.xaml", "NoteRow.xaml" })
        {
            Assert.Contains(
                Load(Path.Join(DesignSystem, "Styles", row)).Descendants(Xaml + "Setter"),
                s => (string?)s.Attribute("Property") == "Template" && (string?)s.Attribute("Value") == "{StaticResource ListRowTemplate}");
        }
    }

    [Fact]
    public void Hover_and_press_set_the_text_that_belongs_on_their_fill_in_every_template()
    {
        // In high contrast the hover and pressed fills are Highlight; text left as WindowText on Highlight is the
        // pairing the OS never makes. Every template sets InteractionText with the fill.
        var failures = new List<string>();

        foreach (XElement template in DesignSystemFiles().SelectMany(f => Load(f).Descendants(Xaml + "ControlTemplate")))
        {
            foreach (string state in new[] { "PointerOver", "Pressed" })
            {
                var setters = Setters(State(template, state));

                if (!setters.Contains(("Root.Background", $"{{ThemeResource Interaction{(state == "Pressed" ? "Pressed" : "Hover")}Brush}}"))
                    || !setters.Contains(("Content.Foreground", "{ThemeResource InteractionTextBrush}")))
                {
                    failures.Add($"{(string?)template.Attribute(X + "Key") ?? (string?)template.Attribute("TargetType")} {state}");
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join("; ", failures));
    }

    // ---------------------------------------------------------------- accessibility and harness contracts

    [Fact]
    public void Every_interactive_control_has_an_accessible_name_and_focus_follows_the_visual_order()
    {
        // A screen reader announces each control by its name; tab order is the document order, which is the visual
        // order, so nothing may reorder it.
        var window = Load(MainWindowPath);
        var unnamed = window.Descendants()
            .Where(e => e.Name.LocalName is "Button" or "ToggleButton" or "TextBox" or "ListView")
            .Where(e => e.Attribute("AutomationProperties.Name") is null)
            .Select(e => (string?)e.Attribute(X + "Name") ?? e.Name.LocalName)
            .ToList();
        var reordering = window.Descendants().SelectMany(e => e.Attributes())
            .Where(a => a.Name.LocalName is "TabIndex" || a.Name.LocalName.StartsWith("XYFocus", StringComparison.Ordinal))
            .Select(a => a.Name.LocalName)
            .ToList();

        Assert.True(unnamed.Count == 0, "No accessible name: " + string.Join(", ", unnamed));
        Assert.True(reordering.Count == 0, "Focus order overridden: " + string.Join(", ", reordering));
    }

    [Fact]
    public void Every_element_the_validation_harness_finds_by_automation_id_still_exists()
    {
        // The runtime harness finds elements by their x:Name (their UI Automation id). Renaming one breaks every
        // script that uses it, silently, until the next desktop run.
        string[] contract = ["FolderList", "NoteList", "NoteEditor", "EditorNoticeText", "FolderNameInput", "BackButton", "EditorBackButton", "PinToggle"];
        var names = Load(MainWindowPath).Descendants().Select(e => (string?)e.Attribute(X + "Name")).OfType<string>().ToHashSet();
        string scripts = string.Join('\n', Directory.EnumerateFiles(Path.Join(Path.GetDirectoryName(Path.GetDirectoryName(DesignSystem))!, "tools", "validation"), "*.ps1").Select(File.ReadAllText));

        var lookedUp = Regex.Matches(scripts, @"(?<![\w-])(?:Find-ById|Items|Visible-Id|FirstItem|Element)\s+(?:\$[\w.]+\s+)?['""](\w+)['""]")
            .Select(m => m.Groups[1].Value)
            .Where(id => id != "Noto")
            .ToHashSet();

        Assert.True(contract.All(names.Contains), "Missing: " + string.Join(", ", contract.Where(id => !names.Contains(id))));
        Assert.True(lookedUp.All(names.Contains), "The harness looks up ids the window lacks: " + string.Join(", ", lookedUp.Where(id => !names.Contains(id))));
        Assert.True(contract.All(id => scripts.Contains($"'{id}'", StringComparison.Ordinal)), "A contract id is no longer used by the harness; update the list.");
    }

    // ---------------------------------------------------------------- helpers

    private static IEnumerable<string> DesignSystemFiles() =>
        Directory.EnumerateFiles(DesignSystem, "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static XDocument Load(string path) => XDocument.Load(path);

    private static string? KeyOf(XElement element) => (string?)element.Attribute(X + "Key");

    private static IEnumerable<string> KeysOf(XDocument document) => document.Descendants().Select(KeyOf).OfType<string>();

    private static XElement ThemeDictionary(string theme) =>
        Load(Path.Join(DesignSystem, "Themes", "Colors.xaml")).Descendants(Xaml + "ResourceDictionary").Single(d => KeyOf(d) == theme);

    /// <summary>This file and every dictionary it merges, recursively.</summary>
    private static HashSet<string> Closure(string file)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>([Path.GetFullPath(file)]);

        while (pending.TryPop(out string? next))
        {
            if (!seen.Add(next))
            {
                continue;
            }

            foreach (string source in Load(next).Descendants(Xaml + "ResourceDictionary").Select(d => (string?)d.Attribute("Source")).OfType<string>())
            {
                const string Prefix = "ms-appx:///Noto.UI/";
                Assert.StartsWith(Prefix, source, StringComparison.Ordinal);
                string merged = Path.GetFullPath(Path.Join(DesignSystem, source[Prefix.Length..]));
                Assert.True(File.Exists(merged), $"{Path.GetFileName(next)} merges a missing dictionary: {source}");
                pending.Push(merged);
            }
        }

        return seen;
    }

    private static IEnumerable<(XElement Element, string Kind, string Key)> References(XDocument document)
    {
        foreach (XElement element in document.Descendants())
        {
            if (element.Name.LocalName == "StaticResource" && (string?)element.Attribute("ResourceKey") is { } aliased)
            {
                yield return (element, "StaticResource", aliased);
            }

            foreach (XAttribute attribute in element.Attributes())
            {
                foreach (Match match in Reference.Matches(attribute.Value))
                {
                    yield return (element, match.Groups[1].Value, match.Groups[2].Value);
                }
            }
        }
    }

    private static XElement? State(XElement template, string name) =>
        template.Descendants(Xaml + "VisualState").FirstOrDefault(s => (string?)s.Attribute(X + "Name") == name);

    private static List<(string Target, string Value)> Setters(XElement? state) =>
        state is null ? [] : state.Descendants(Xaml + "Setter").Select(s => ((string?)s.Attribute("Target") ?? string.Empty, (string?)s.Attribute("Value") ?? string.Empty)).ToList();

    /// <summary>The system colour a high-contrast brush or alias names.</summary>
    private static string Target(XElement theme, string key)
    {
        XElement element = theme.Elements().Single(e => KeyOf(e) == key);
        string value = (string?)element.Attribute("Color") ?? (string?)element.Attribute("ResourceKey") ?? string.Empty;
        return Reference.Match(value) is { Success: true } match ? match.Groups[2].Value : value;
    }

    /// <summary>A colour's sRGB value; an alias is followed within its theme. Alpha is kept for compositing.</summary>
    private static (double A, double R, double G, double B) ColourOf(XElement theme, string key)
    {
        XElement element = theme.Elements().Single(e => KeyOf(e) == key);

        if ((string?)element.Attribute("ResourceKey") is { } alias)
        {
            return ColourOf(theme, alias);
        }

        string hex = element.Value.Trim().TrimStart('#');
        uint value = uint.Parse(hex.Length == 6 ? "FF" + hex : hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return (((value >> 24) & 0xFF) / 255.0, ((value >> 16) & 0xFF) / 255.0, ((value >> 8) & 0xFF) / 255.0, (value & 0xFF) / 255.0);
    }

    /// <summary>WCAG 2.x contrast ratio, the foreground composited over an opaque background.</summary>
    private static double Contrast((double A, double R, double G, double B) fore, (double A, double R, double G, double B) back)
    {
        double Over(double f, double b) => (fore.A * f) + ((1 - fore.A) * b);
        double l1 = Luminance(Over(fore.R, back.R), Over(fore.G, back.G), Over(fore.B, back.B));
        double l2 = Luminance(back.R, back.G, back.B);
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);

        static double Luminance(double r, double g, double b) => (0.2126 * Linear(r)) + (0.7152 * Linear(g)) + (0.0722 * Linear(b));
        static double Linear(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    /// <summary>Walks up to the repository root; a guard that silently skips is worse than none.</summary>
    private static string SourcePath(params string[] parts)
    {
        string relative = Path.Join(parts);

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Join(directory.FullName, relative);

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Could not locate {relative} by walking up from {AppContext.BaseDirectory}");
    }
}
