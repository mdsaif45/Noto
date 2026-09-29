namespace Noto.Core.Activation;

/// <summary>The modifiers a global hotkey may hold. Combinable.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Ctrl = 1,
    Alt = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>
/// A global hotkey chord, parsed from its setting text (#16 slice 4).
/// </summary>
/// <remarks>
/// <para>
/// <b>The grammar</b> is the contract closed by the slice 4 design gate, and
/// nothing wider:
/// </para>
/// <list type="bullet">
///   <item>tokens separated by <c>+</c>; whitespace around a token is ignored;
///   whitespace inside a token, an empty token, or any non-ASCII character
///   makes the chord invalid</item>
///   <item>modifiers <c>Ctrl</c>, <c>Alt</c>, <c>Shift</c>, <c>Win</c>:
///   case-insensitive, exact names only, each at most once, in any order</item>
///   <item>exactly one key, and it is the last token: <c>A</c>–<c>Z</c>,
///   <c>0</c>–<c>9</c> (the main row), <c>F1</c>–<c>F24</c> or
///   <c>Space</c>, case-insensitive</item>
///   <item>at least one of <c>Ctrl</c>, <c>Alt</c> or <c>Win</c>: a chord
///   with no modifier, or with <c>Shift</c> alone, would swallow ordinary
///   typing</item>
/// </list>
/// <para>
/// A chord Windows reserves — <c>Win+L</c> — is valid here and refused when
/// it is registered. <c>Ctrl+Alt+&lt;letter&gt;</c> is valid too: parity §12a
/// forbids it as a <i>default</i>, and every chord is rebindable.
/// </para>
/// <para>
/// <b>Canonical form</b> — <c>Ctrl+Alt+Shift+Win+KEY</c>, the key in upper
/// case — is what two chords are compared by and what diagnostics show. It is
/// never written back: the stored setting keeps the text the user gave.
/// </para>
/// <para>
/// A platform-neutral value. Turning it into virtual-key codes is the
/// platform's job; Core knows only the names.
/// </para>
/// </remarks>
public readonly record struct HotkeyChord
{
    private const HotkeyModifiers Primary = HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Win;

    private HotkeyChord(HotkeyModifiers modifiers, string key)
    {
        Modifiers = modifiers;
        Key = key;
    }

    /// <summary>The modifiers held.</summary>
    public HotkeyModifiers Modifiers { get; }

    /// <summary>
    /// The key, in canonical upper case: <c>A</c>–<c>Z</c>, <c>0</c>–<c>9</c>,
    /// <c>F1</c>–<c>F24</c> or <c>SPACE</c>.
    /// </summary>
    public string Key { get; }

    /// <summary>Every key the grammar accepts, in canonical form.</summary>
    public static IReadOnlyList<string> SupportedKeys { get; } =
    [
        .. Enumerable.Range('A', 26).Select(c => ((char)c).ToString()),
        .. Enumerable.Range('0', 10).Select(c => ((char)c).ToString()),
        .. Enumerable.Range(1, 24).Select(n => "F" + n.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        "SPACE",
    ];

    /// <summary>The canonical text: <c>Ctrl+Alt+Shift+Win+KEY</c>, held modifiers only.</summary>
    public string Canonical
    {
        get
        {
            var parts = new List<string>(5);

            if (Modifiers.HasFlag(HotkeyModifiers.Ctrl))
            {
                parts.Add("Ctrl");
            }

            if (Modifiers.HasFlag(HotkeyModifiers.Alt))
            {
                parts.Add("Alt");
            }

            if (Modifiers.HasFlag(HotkeyModifiers.Shift))
            {
                parts.Add("Shift");
            }

            if (Modifiers.HasFlag(HotkeyModifiers.Win))
            {
                parts.Add("Win");
            }

            parts.Add(Key);
            return string.Join('+', parts);
        }
    }

    public override string ToString() => Canonical;

    /// <summary>Parses setting text; <see langword="false"/> for anything outside the grammar.</summary>
    public static bool TryParse(string? text, out HotkeyChord chord)
    {
        chord = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string[] raw = text.Split('+');
        var modifiers = HotkeyModifiers.None;

        for (int i = 0; i < raw.Length; i++)
        {
            string token = raw[i].Trim();

            if (token.Length == 0 || !IsPlainToken(token))
            {
                return false;
            }

            bool last = i == raw.Length - 1;
            HotkeyModifiers modifier = ModifierNamed(token);

            if (!last)
            {
                // Everything before the key must be a modifier, once.
                if (modifier == HotkeyModifiers.None || (modifiers & modifier) != 0)
                {
                    return false;
                }

                modifiers |= modifier;
                continue;
            }

            // The last token is the key, never a modifier.
            if (modifier != HotkeyModifiers.None || KeyNamed(token) is not string key)
            {
                return false;
            }

            if ((modifiers & Primary) == 0)
            {
                return false;
            }

            chord = new HotkeyChord(modifiers, key);
            return true;
        }

        return false;
    }

    /// <summary>Parses setting text.</summary>
    /// <exception cref="FormatException"><paramref name="text"/> is outside the grammar.</exception>
    public static HotkeyChord Parse(string text) => TryParse(text, out HotkeyChord chord)
        ? chord
        : throw new FormatException("Not a hotkey chord this application accepts.");

    /// <summary>ASCII letters and digits only — no whitespace, no punctuation, nothing non-ASCII.</summary>
    private static bool IsPlainToken(string token)
    {
        foreach (char c in token)
        {
            if (!char.IsAsciiLetterOrDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    private static HotkeyModifiers ModifierNamed(string token) => token.ToUpperInvariant() switch
    {
        "CTRL" => HotkeyModifiers.Ctrl,
        "ALT" => HotkeyModifiers.Alt,
        "SHIFT" => HotkeyModifiers.Shift,
        "WIN" => HotkeyModifiers.Win,
        _ => HotkeyModifiers.None,
    };

    private static string? KeyNamed(string token)
    {
        string upper = token.ToUpperInvariant();

        return SupportedKeys.Contains(upper, StringComparer.Ordinal) ? upper : null;
    }
}
