using Noto.Core.Activation;
using Edge = Noto.Core.Workspace.WorkspaceEdge;
using EscapeBehavior = Noto.Core.Workspace.EscapeBehavior;

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
/// <b>Only keys with a consumer.</b> #9 registered the two hotkey keys because
/// hotkey registration is Phase 1 startup work (architecture-overview.md
/// §startup) and both defaults were decided by the parity specification.
/// #16 slice 3 adds the edge and the width, which the docked window reads at
/// launch; slice 6 adds the Escape behaviour and hiding on deactivation.
/// Hover and tray toggles and the hover delay are still absent: none has a
/// consumer yet.
/// </para>
/// <para>
/// <b>Per-display settings are a family</b>, not a key per display: see
/// <see cref="SettingKeyFamily"/> and <see cref="Families"/>. <c>::</c> is
/// reserved as the family separator, so no key name here may contain it.
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
    /// Stored as text. <b>Valid means it parses as a <see cref="HotkeyChord"/></b>
    /// (#16 slice 4), so a malformed row — <c>Ctrl+Hyper+Q</c>, a bare
    /// <c>Q</c> — falls back to this default through the ordinary #9 path:
    /// reported, cached as the default, and the row left exactly as it was.
    /// The platform never sees text it cannot register. Turning the chord
    /// into virtual-key codes, and registering it, stay in
    /// <c>Noto.Platform.Windows</c>.
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
            static value => HotkeyChord.TryParse(value, out _));

    /// <summary>
    /// The vertical screen edge the workspace docks to.
    /// </summary>
    /// <remarks>
    /// Default <see cref="Edge.Right"/> — parity A2 and J3, "default Right".
    /// Stored by member name and parsed case-sensitively, so <c>left</c> or
    /// <c>0</c> is not a legal value and reads as the default. Read once, at
    /// launch (#16 slice 3); nothing in the application writes it yet.
    /// </remarks>
    public static readonly SettingKey<Edge> WorkspaceEdge =
        new("workspace.edge", Edge.Right);

    /// <summary>
    /// The workspace width last chosen on any display, in DIPs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The fallback for a display with no width of its own
    /// (<see cref="WorkspaceWidthByScope"/>). Default 360 — the nominal
    /// default of ADR-007 §4.
    /// </para>
    /// <para>
    /// <b>Valid is not the same as fits.</b> Any finite positive width is
    /// legal data. Whether it fits the display in use is decided by the
    /// platform's clamp against that display's live work area, which corrects
    /// the width it shows without ever rewriting the stored value: a width
    /// remembered on a large display must survive a session on a small one.
    /// </para>
    /// </remarks>
    public static readonly SettingKey<double> WorkspaceWidth =
        new("workspace.width", 360, IsPositiveFinite);

    /// <summary>
    /// The workspace width for one scope, in DIPs: <c>workspace.width::&lt;scope&gt;</c>.
    /// </summary>
    /// <remarks>
    /// Named for its scope, not for what the scope is: ADR-009 keeps screen
    /// concepts out of Core, and its guard test rejects them by name. The
    /// application passes the platform's opaque display identity as the
    /// scope, so each display has its own width; Core only sees text. Same default and validity
    /// rule as <see cref="WorkspaceWidth"/>. A caller resolving a width uses
    /// <see cref="ISettingsStore.TryRead{T}"/>, so "no width for this display"
    /// is observable without a sentinel.
    /// </remarks>
    public static readonly SettingKeyFamily<double> WorkspaceWidthByScope =
        new("workspace.width", 360, IsPositiveFinite);

    /// <summary>
    /// What Escape does in the workspace (parity A12, G51).
    /// </summary>
    /// <remarks>
    /// Default <see cref="EscapeBehavior.LeaveFolderOrHide"/>, SideNotes' own
    /// default (feature inventory A12, CONFIRMED). Stored by member name and
    /// parsed case-sensitively. Read once, at launch (#16 slice 6); there is no
    /// settings UI to change it yet.
    /// </remarks>
    public static readonly SettingKey<EscapeBehavior> WorkspaceEscape =
        new("workspace.escape", EscapeBehavior.LeaveFolderOrHide);

    /// <summary>
    /// Whether the shown workspace hides when activation moves to another
    /// application — parity A13 and J5, "close on outside click".
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The user's term and the mechanism differ.</b> Parity calls it a click
    /// outside; Noto implements it as <i>workspace deactivation</i>: activation
    /// moving to a window of another process, whether by a click, Alt+Tab, the
    /// Start menu or a virtual-desktop switch. Activation moving to one of
    /// Noto's own windows (a context menu, an IME window) does not count.
    /// </para>
    /// <para>
    /// Default <see langword="true"/> (the slice 6 design gate): the workspace
    /// is topmost while shown, so a drawer that stays over the user's work
    /// after they have moved on would cover it. Pin suppresses it for a
    /// session. Read once, at launch; no settings UI yet.
    /// </para>
    /// </remarks>
    public static readonly SettingKey<bool> WorkspaceHideOnDeactivation =
        new("workspace.hide-on-deactivation", true);

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
        WorkspaceEdge,
        WorkspaceWidth,
        WorkspaceEscape,
        WorkspaceHideOnDeactivation,
    ];

    /// <summary>
    /// Every declared key family.
    /// </summary>
    /// <remarks>
    /// What the store materialises alongside <see cref="All"/>. A row is a
    /// family member only if its name is a prefix declared here, the
    /// separator and a non-blank scope.
    /// </remarks>
    public static IReadOnlyList<SettingKeyFamily> Families { get; } =
    [
        WorkspaceWidthByScope,
    ];

    private static bool IsPositiveFinite(double value) => double.IsFinite(value) && value > 0;
}
