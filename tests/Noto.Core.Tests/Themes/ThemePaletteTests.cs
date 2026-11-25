using Noto.Core.Themes;
using Xunit;
using static Noto.Core.Tests.Themes.ThemeFileTests;

namespace Noto.Core.Tests.Themes;

/// <summary>#269 (ADR-023 §2, §4, §5): a theme resolved over Noto's own colours, its derived states, and the readability rules.</summary>
public sealed class ThemePaletteTests
{
    /// <summary>
    /// Noto's own colours as Colors.xaml has them (DesignSystemGuardTests holds the real dictionaries to the same rules);
    /// the accent stands in for a Windows accent.
    /// </summary>
    internal static readonly Dictionary<ThemeMode, Dictionary<string, ThemeColor>> Own = new()
    {
        [ThemeMode.Light] = Palette(
            ("background", "#f3f3f3"), ("card", "#ffffff"), ("code", "#f0f0f0"), ("text", "#1a1a1a"), ("textSecondary", "#5e5e5e"),
            ("textDisabled", "#9a9a9a"), ("link", "#0b5cad"), ("quote", "#5e5e5e"), ("checkmark", "#5e5e5e"), ("danger", "#b42318"),
            ("accent", "#005fb8"), ("border", "#e0e0e0"), ("highlight", "#fff0a0"), ("highlightText", "#1a1a1a"),
            ("note1.tint", "#f5c40040"), ("note1.marker", "#a6780a"), ("note2.tint", "#3fb95040"), ("note2.marker", "#2d8a3e"),
            ("note3.tint", "#3a96f540"), ("note3.marker", "#1f6fd1"), ("note4.tint", "#a371f740"), ("note4.marker", "#8250df"),
            ("note5.tint", "#ec6cb040"), ("note5.marker", "#c2387f"), ("note6.tint", "#8b949e40"), ("note6.marker", "#6e7781"),
            (ThemePalette.Hover, "#eaeaea"), (ThemePalette.Pressed, "#dadada"), (ThemePalette.Selected, "#e1e1e1"),
            (ThemePalette.CardHover, "#f9f9f9"), (ThemePalette.Focus, "#000000e4"), (ThemePalette.FocusInner, "#ffffffb3")),
        [ThemeMode.Dark] = Palette(
            ("background", "#202020"), ("card", "#2d2d2d"), ("code", "#3a3a3a"), ("text", "#f2f2f2"), ("textSecondary", "#a8a8a8"),
            ("textDisabled", "#767676"), ("link", "#8cc2ff"), ("quote", "#a8a8a8"), ("checkmark", "#a8a8a8"), ("danger", "#ff99a4"),
            ("accent", "#99ebff"), ("border", "#383838"), ("highlight", "#5a4b00"), ("highlightText", "#ffffff"),
            ("note1.tint", "#f5c40014"), ("note1.marker", "#f5c400"), ("note2.tint", "#3fb9501c"), ("note2.marker", "#3fb950"),
            ("note3.tint", "#3a96f521"), ("note3.marker", "#3a96f5"), ("note4.tint", "#a371f721"), ("note4.marker", "#a371f7"),
            ("note5.tint", "#ec6cb01f"), ("note5.marker", "#ec6cb0"), ("note6.tint", "#8b949e1f"), ("note6.marker", "#8b949e"),
            (ThemePalette.Hover, "#2d2d2d"), (ThemePalette.Pressed, "#333333"), (ThemePalette.Selected, "#383838"),
            (ThemePalette.CardHover, "#323232"), (ThemePalette.Focus, "#ffffff"), (ThemePalette.FocusInner, "#000000b3")),
    };

    [Theory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void Notos_own_colours_hold_every_key_and_pass_every_rule(ThemeMode mode)
    {
        Assert.Equal(ThemePalette.Keys.Order(), Own[mode].Keys.Order());
        Assert.Empty(ThemePalette.Measure(Own[mode], mode, accent: false));
    }

    [Theory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void The_example_passes_every_rule_with_nothing_dropped(ThemeMode mode)
    {
        Dictionary<string, ThemeColor> set = Read(Example()).ColoursFor(mode);

        Assert.Empty(ThemePalette.Problems(ThemePalette.Resolve(Own[mode], set), set, mode));
        Assert.Equal(0, ThemePalette.Fallback(Own[mode], set, mode).Dropped);
    }

    [Fact]
    public void States_are_derived_toward_the_text_whenever_the_background_card_or_text_is_set()
    {
        var set = new Dictionary<string, ThemeColor> { ["background"] = Colour("#ffffff"), ["text"] = Colour("#000000") };
        Dictionary<string, ThemeColor> palette = ThemePalette.Resolve(Own[ThemeMode.Light], set);

        Assert.Equal(Colour("#f5f5f5"), palette[ThemePalette.Hover]);       // 4 %
        Assert.Equal(Colour("#ebebeb"), palette[ThemePalette.Selected]);    // 8 %
        Assert.Equal(Colour("#e6e6e6"), palette[ThemePalette.Pressed]);     // 10 %
        Assert.Equal(Colour("#f9f9f9"), palette[ThemePalette.CardHover]);   // the card, 2.5 %

        // A theme that sets none of the three keeps Noto's own states.
        Dictionary<string, ThemeColor> linkOnly = ThemePalette.Resolve(Own[ThemeMode.Light], new Dictionary<string, ThemeColor> { ["link"] = Colour("#00f") });
        Assert.Equal(Own[ThemeMode.Light][ThemePalette.Hover], linkOnly[ThemePalette.Hover]);
    }

    [Fact]
    public void Code_and_highlight_are_flattened_over_the_card_tints_and_borders_keep_their_alpha()
    {
        var set = new Dictionary<string, ThemeColor>
        {
            ["card"] = Colour("#000000"),
            ["code"] = Colour("#ffffff80"),
            ["highlight"] = Colour("#ff000080"),
            ["border"] = Colour("#ffffff1f"),
            ["note1.tint"] = Colour("#ff000040"),
        };
        Dictionary<string, ThemeColor> palette = ThemePalette.Resolve(Own[ThemeMode.Dark], set);

        Assert.Equal(Colour("#808080"), palette["code"]);
        Assert.Equal(Colour("#800000"), palette["highlight"]);
        Assert.Equal(0x1f, palette["border"].A);
        Assert.Equal(0x40, palette["note1.tint"].A);
    }

    [Fact]
    public void A_failing_pair_is_said_in_words_naming_the_mode_and_the_minimum()
    {
        var set = new Dictionary<string, ThemeColor> { ["textSecondary"] = Colour("#555555") };

        ThemeFailure failure = Assert.Single(ThemePalette.Problems(ThemePalette.Resolve(Own[ThemeMode.Dark], set), set, ThemeMode.Dark), f => f.Reason.StartsWith("Secondary text on a card", StringComparison.Ordinal));
        Assert.Matches(@"^Secondary text on a card is \d\.\d:1 in dark; it needs 4\.5:1$", failure.Reason);
    }

    [Fact]
    public void The_mode_is_honest_light_is_lighter_than_its_text()
    {
        var set = new Dictionary<string, ThemeColor> { ["background"] = Colour("#101010"), ["card"] = Colour("#181818"), ["text"] = Colour("#f0f0f0") };

        Assert.Contains(ThemePalette.Problems(ThemePalette.Resolve(Own[ThemeMode.Light], set), set, ThemeMode.Light), f => f.Reason == "In light, the background must be lighter than the text");
    }

    [Fact]
    public void The_accent_is_measured_only_when_the_theme_sets_it()
    {
        var background = new Dictionary<string, ThemeColor> { ["background"] = Colour("#005fb8"), ["text"] = Colour("#ffffff"), ["card"] = Colour("#003060") };
        Assert.DoesNotContain(ThemePalette.Measure(ThemePalette.Resolve(Own[ThemeMode.Dark], background), ThemeMode.Dark, accent: false), f => f.Reason.StartsWith("The accent", StringComparison.Ordinal));

        var accent = new Dictionary<string, ThemeColor> { ["accent"] = Colour("#2a2a2a") };
        Assert.Contains(ThemePalette.Problems(ThemePalette.Resolve(Own[ThemeMode.Dark], accent), accent, ThemeMode.Dark), f => f.Reason.StartsWith("The accent on the background", StringComparison.Ordinal));
    }

    [Fact]
    public void A_load_drops_the_foreground_first_and_keeps_what_is_readable()
    {
        // Text near the background fails; the background is fine with Noto's own text, so only the text goes.
        var set = new Dictionary<string, ThemeColor> { ["background"] = Colour("#eef2f5"), ["text"] = Colour("#c0c0c0"), ["link"] = Colour("#174f87") };

        (Dictionary<string, ThemeColor> palette, Dictionary<string, ThemeColor> kept, int dropped) = ThemePalette.Fallback(Own[ThemeMode.Light], set, ThemeMode.Light);

        Assert.Equal(1, dropped);
        Assert.Equal(["background", "link"], kept.Keys.Order());
        Assert.Equal(Own[ThemeMode.Light]["text"], palette["text"]);
        Assert.Empty(ThemePalette.Problems(palette, kept, ThemeMode.Light));
    }

    [Theory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void The_worst_theme_falls_back_to_notos_own_colours_and_ends(ThemeMode mode)
    {
        // Every colour the same mid grey: nothing is readable on anything.
        var set = ThemeFile.ColourKeys.ToDictionary(k => k, _ => Colour("#808080"));

        (Dictionary<string, ThemeColor> palette, Dictionary<string, ThemeColor> kept, _) = ThemePalette.Fallback(Own[mode], set, mode);

        Assert.Empty(ThemePalette.Measure(palette, mode, kept.ContainsKey("accent")));
        Assert.DoesNotContain("text", kept.Keys);
        Assert.DoesNotContain("background", kept.Keys);
        Assert.DoesNotContain("card", kept.Keys);
    }

    private static Dictionary<string, ThemeColor> Palette(params (string Key, string Colour)[] colours) =>
        colours.ToDictionary(c => c.Key, c => Colour(c.Colour));
}
