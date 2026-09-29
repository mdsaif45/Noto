using Noto.Core.Activation;
using Noto.Core.Settings;
using Xunit;

namespace Noto.Core.Tests.Activation;

/// <summary>
/// The global hotkey grammar (#16 slice 4), exactly as its design gate closed it.
/// </summary>
public sealed class HotkeyChordTests
{
    // -------------------------------------------------------------- valid

    [Fact]
    public void The_default_chord_parses()
    {
        HotkeyChord chord = HotkeyChord.Parse("Ctrl+Alt+Win+Space");

        Assert.Equal(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Win, chord.Modifiers);
        Assert.Equal("SPACE", chord.Key);
        Assert.Equal("Ctrl+Alt+Win+SPACE", chord.Canonical);
    }

    [Theory]
    [InlineData("Ctrl+A", HotkeyModifiers.Ctrl)]
    [InlineData("Alt+A", HotkeyModifiers.Alt)]
    [InlineData("Win+A", HotkeyModifiers.Win)]
    [InlineData("Ctrl+Shift+A", HotkeyModifiers.Ctrl | HotkeyModifiers.Shift)]
    [InlineData("Alt+Shift+A", HotkeyModifiers.Alt | HotkeyModifiers.Shift)]
    [InlineData("Win+Shift+A", HotkeyModifiers.Win | HotkeyModifiers.Shift)]
    [InlineData("Ctrl+Alt+Shift+Win+A", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Shift | HotkeyModifiers.Win)]
    public void Each_modifier_is_recognised(string text, HotkeyModifiers expected)
    {
        Assert.True(HotkeyChord.TryParse(text, out HotkeyChord chord));
        Assert.Equal(expected, chord.Modifiers);
    }

    [Theory]
    [InlineData("Win+Alt+Ctrl+Space")]
    [InlineData("Alt+Win+Ctrl+Space")]
    [InlineData("Ctrl+Win+Alt+Space")]
    public void Modifiers_may_come_in_any_order(string text)
    {
        Assert.Equal(HotkeyChord.Parse("Ctrl+Alt+Win+Space"), HotkeyChord.Parse(text));
    }

    [Theory]
    [InlineData("ctrl+alt+win+space")]
    [InlineData("CTRL+ALT+WIN+SPACE")]
    [InlineData("cTrL+aLt+WiN+sPaCe")]
    public void Names_are_case_insensitive(string text)
    {
        Assert.Equal("Ctrl+Alt+Win+SPACE", HotkeyChord.Parse(text).Canonical);
    }

    [Theory]
    [InlineData(" Ctrl+Alt+Win+Space ")]
    [InlineData("Ctrl + Alt + Win + Space")]
    [InlineData("\tCtrl+\tAlt +Win+ Space\t")]
    public void Whitespace_around_tokens_is_ignored(string text)
    {
        Assert.Equal("Ctrl+Alt+Win+SPACE", HotkeyChord.Parse(text).Canonical);
    }

    [Fact]
    public void Every_letter_digit_function_key_and_space_is_a_key()
    {
        string[] expected =
        [
            .. Enumerable.Range('A', 26).Select(c => ((char)c).ToString()),
            .. Enumerable.Range('0', 10).Select(c => ((char)c).ToString()),
            .. Enumerable.Range(1, 24).Select(n => $"F{n}"),
            "SPACE",
        ];

        Assert.Equal(expected, HotkeyChord.SupportedKeys);

        foreach (string key in expected)
        {
            Assert.True(HotkeyChord.TryParse("Ctrl+" + key, out HotkeyChord chord), key);
            Assert.Equal(key, chord.Key);
            Assert.True(HotkeyChord.TryParse("Ctrl+" + key.ToLowerInvariant(), out _), key.ToLowerInvariant());
        }
    }

    [Theory]
    [InlineData("Ctrl+Alt+Q")]   // valid syntax: §12a forbids it only as a default
    [InlineData("Win+L")]        // valid syntax: Windows refuses it at registration
    public void Chords_policy_or_windows_rejects_are_still_valid_syntax(string text)
    {
        Assert.True(HotkeyChord.TryParse(text, out _));
    }

    // ------------------------------------------------------------ invalid

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_is_not_a_chord(string? text)
    {
        Assert.False(HotkeyChord.TryParse(text, out _));
    }

    [Theory]
    [InlineData("Ctrl++Space")]
    [InlineData("+Ctrl+Space")]
    [InlineData("Ctrl+Space+")]
    [InlineData("Ctrl+ +Space")]
    [InlineData("+")]
    public void An_empty_token_is_invalid(string text)
    {
        Assert.False(HotkeyChord.TryParse(text, out _));
    }

    [Theory]
    [InlineData("Ct rl+Space")]
    [InlineData("Ctrl+Sp ace")]
    [InlineData("Ctrl+F 5")]
    public void Whitespace_inside_a_token_is_invalid(string text)
    {
        Assert.False(HotkeyChord.TryParse(text, out _));
    }

    [Theory]
    [InlineData("Ctrl+Ctrl+Space")]
    [InlineData("Ctrl+Alt+ctrl+Space")]
    [InlineData("Win+Shift+Shift+A")]
    public void A_modifier_may_appear_only_once(string text)
    {
        Assert.False(HotkeyChord.TryParse(text, out _));
    }

    [Theory]
    [InlineData("Control+Space")]
    [InlineData("Windows+Space")]
    [InlineData("Cmd+Space")]
    [InlineData("Option+Space")]
    [InlineData("Meta+Space")]
    [InlineData("Hyper+Q")]
    [InlineData("Ctrl+Hyper+Q")]
    public void Only_the_exact_modifier_names_are_accepted(string text)
    {
        Assert.False(HotkeyChord.TryParse(text, out _));
    }

    [Theory]
    [InlineData("Ctrl")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+Alt+Win")]
    public void A_chord_needs_a_key(string text)
    {
        Assert.False(HotkeyChord.TryParse(text, out _));
    }

    [Theory]
    [InlineData("Ctrl+A+Alt")]
    [InlineData("Space+Ctrl")]
    [InlineData("Ctrl+A+B")]
    public void The_key_must_be_the_last_and_only_key(string text)
    {
        Assert.False(HotkeyChord.TryParse(text, out _));
    }

    [Theory]
    [InlineData("Ctrl+F0")]
    [InlineData("Ctrl+F25")]
    [InlineData("Ctrl+F01")]
    [InlineData("Ctrl+Enter")]
    [InlineData("Ctrl+Tab")]
    [InlineData("Ctrl+Esc")]
    [InlineData("Ctrl+Delete")]
    [InlineData("Ctrl+Left")]
    [InlineData("Ctrl+NumPad1")]
    [InlineData("Ctrl+10")]
    [InlineData("Ctrl+AB")]
    [InlineData("Ctrl+-")]
    [InlineData("Ctrl+,")]
    [InlineData("Ctrl+`")]
    [InlineData("Ctrl+Spacebar")]
    public void Keys_outside_the_set_are_invalid(string text)
    {
        Assert.False(HotkeyChord.TryParse(text, out _));
    }

    [Theory]
    [InlineData("Ctrl+é")]
    [InlineData("Wın+Space")]    // dotless i: must not be folded to "WIN"
    [InlineData("Ctrl+Ａ")]      // full-width A
    [InlineData("Ctrl+١")]       // Arabic-Indic digit one
    public void Non_ascii_text_is_invalid(string text)
    {
        Assert.False(HotkeyChord.TryParse(text, out _));
    }

    [Theory]
    [InlineData("Space")]
    [InlineData("A")]
    [InlineData("F13")]
    public void A_chord_without_a_modifier_is_invalid(string text)
    {
        Assert.False(HotkeyChord.TryParse(text, out _));
    }

    [Theory]
    [InlineData("Shift+A")]
    [InlineData("Shift+Space")]
    [InlineData("Shift+F5")]
    public void Shift_alone_is_not_enough(string text)
    {
        // Shift+key is ordinary typing; a global hotkey must not swallow it.
        Assert.False(HotkeyChord.TryParse(text, out _));
    }

    [Fact]
    public void Parse_throws_for_invalid_text()
    {
        Assert.Throws<FormatException>(() => HotkeyChord.Parse("Shift+A"));
    }

    // ---------------------------------------------------------- canonical

    [Theory]
    [InlineData("Win+Shift+Alt+Ctrl+f12", "Ctrl+Alt+Shift+Win+F12")]
    [InlineData("shift+win+q", "Shift+Win+Q")]
    [InlineData("alt+7", "Alt+7")]
    public void The_canonical_form_orders_modifiers_and_upper_cases_the_key(string text, string canonical)
    {
        HotkeyChord chord = HotkeyChord.Parse(text);

        Assert.Equal(canonical, chord.Canonical);
        Assert.Equal(canonical, chord.ToString());
    }

    [Fact]
    public void Chords_compare_by_their_canonical_meaning()
    {
        Assert.Equal(HotkeyChord.Parse("win + ALT + ctrl + space"), HotkeyChord.Parse("Ctrl+Alt+Win+Space"));
        Assert.NotEqual(HotkeyChord.Parse("Ctrl+Alt+Win+Space"), HotkeyChord.Parse("Ctrl+Alt+Space"));
        Assert.NotEqual(HotkeyChord.Parse("Ctrl+A"), HotkeyChord.Parse("Ctrl+B"));
    }

    // ------------------------------------------------------------ setting

    [Fact]
    public void The_binding_setting_is_valid_exactly_when_it_parses()
    {
        SettingKey<string> binding = SettingKeys.HotkeyBinding;

        Assert.Equal("Ctrl+Alt+Win+Space", binding.Default);
        Assert.True(binding.IsValid(binding.Default));
        Assert.True(binding.IsValid("ctrl + shift + f5"));
        Assert.False(binding.IsValid("Ctrl+Hyper+Q"));
        Assert.False(binding.IsValid("Q"));
        Assert.False(binding.IsValid(" "));
    }
}
