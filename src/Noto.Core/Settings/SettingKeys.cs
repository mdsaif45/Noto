namespace Noto.Core.Settings;

/// <summary>
/// Every setting Noto has.
/// </summary>
/// <remarks>
/// <para>
/// <b>The only place a setting key exists.</b> Adding a setting is adding a
/// field here; there is no other way to name one, because
/// <see cref="ISettingsStore"/> takes a <see cref="SettingKey{T}"/> and never
/// a string. A key nobody declared cannot be read.
/// </para>
/// <para>
/// <b>Two keys, and deliberately only two.</b> #16 will need the edge side,
/// the panel width, the hover and tray toggles and the hover delay — none is
/// here, because none has a consumer yet and one of them (width) has no
/// documented default to give it. The two below are registered now because
/// hotkey registration is Phase 1 startup work
/// (architecture-overview.md §startup), which runs before any window exists,
/// and because both of their defaults are already decided by the parity
/// specification rather than by this slice.
/// </para>
/// </remarks>
public static class SettingKeys
{
    /// <summary>
    /// Whether the global show/hide hotkey is registered at all.
    /// </summary>
    /// <remarks>
    /// Default <see langword="true"/>. Parity A7 and G1 are both MUST rows and
    /// both describe a working global shortcut, and principle 7 requires every
    /// primary action to have a keyboard path — an application that starts
    /// with no way to summon it has neither. The independently-disableable
    /// requirement (#16, parity J1/J2) is about the *edge bar* and tray being
    /// switchable off without losing access, which is why this exists as a
    /// setting rather than being hard-wired on.
    /// </remarks>
    public static readonly SettingKey<bool> HotkeyEnabled =
        new("activation.hotkey.enabled", true);

    /// <summary>
    /// The chord that shows and hides the workspace.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Default <c>Ctrl+Alt+Win+Space</c> — <b>not chosen here</b>. Parity row
    /// G1 maps SideNotes' <c>⌃⌥⌘␣</c> (inventory A7, CONFIRMED, configurable)
    /// to exactly this chord, and A7 requires the Windows equivalent to be
    /// configurable and to work unfocused.
    /// </para>
    /// <para>
    /// It is stored as text and <b>not parsed here</b>. Turning a chord into
    /// virtual-key codes needs Win32 types that ADR-009 keeps out of the
    /// domain; that belongs to <c>Noto.Platform.Windows</c> with #16, along
    /// with registration and conflict reporting. The validity rule below is
    /// therefore only "not blank" — asserting more would be asserting a
    /// grammar this slice cannot enforce.
    /// </para>
    /// <para>
    /// <b>Known and disclosed:</b> parity §12a records that this chord "may
    /// collide with IME switchers", resolution "configurable; test first".
    /// That verification belongs to #16. It is not the <c>Ctrl+Alt</c> AltGr
    /// hazard, which §12a scopes to <c>Ctrl+Alt+&lt;letter&gt;</c> on non-US
    /// layouts and lists by row — G1 is not among them, and the extra
    /// <c>Win</c> modifier is not produced by any layout's AltGr.
    /// </para>
    /// </remarks>
    public static readonly SettingKey<string> HotkeyBinding =
        new(
            "activation.hotkey.binding",
            "Ctrl+Alt+Win+Space",
            static value => !string.IsNullOrWhiteSpace(value));

    /// <summary>
    /// Every declared key.
    /// </summary>
    /// <remarks>
    /// What the store loads at startup, and what a settings UI would enumerate
    /// when one exists (M8). A key absent from here is an <i>unknown</i> key:
    /// the store ignores it and leaves its row alone, so settings written by a
    /// newer Noto survive an older one opening the same database.
    /// </remarks>
    public static IReadOnlyList<SettingKey> All { get; } =
    [
        HotkeyEnabled,
        HotkeyBinding,
    ];
}
