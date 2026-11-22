using Noto.Core.Activation;
using Noto.Core.Diagnostics;
using Noto.Core.Folders;
using Noto.Core.Links;
using Noto.Core.Notes;
using AutomaticBackups = Noto.Core.Portability.AutomaticBackups;
using Edge = Noto.Core.Workspace.WorkspaceEdge;
using EscapeBehavior = Noto.Core.Workspace.EscapeBehavior;
using Frequency = Noto.Core.Portability.BackupFrequency;
using NoteText = Noto.Core.Workspace.NoteText;
using TextSpacing = Noto.Core.Workspace.TextSpacing;
using WelcomeState = Noto.Core.Workspace.WelcomeState;
using WorkspaceTheme = Noto.Core.Workspace.WorkspaceTheme;

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
/// Slice 1's settings surface adds the theme and single-click opening, and
/// makes every workspace setting here take effect as it is written. Hover
/// and tray toggles and the hover delay are still absent: none has a
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
    /// Whether the global new-note hotkey is registered (F17, #156; parity B2, G2).
    /// </summary>
    /// <remarks>Default off: a new global chord takes a key from every other application, so the user turns it on.</remarks>
    public static readonly SettingKey<bool> NewNoteHotkeyEnabled =
        new("activation.new-note.enabled", false);

    /// <summary>
    /// The chord that shows Noto with a new note ready (F17, #156).
    /// </summary>
    /// <remarks>
    /// Default <c>Ctrl+Shift+Win+N</c>, the implementation plan's §10.1 final default for G2 (measured free; the
    /// <c>Ctrl+Shift+Win</c> family is not an AltGr chord). Valid means it parses as a <see cref="HotkeyChord"/>, as
    /// for <see cref="HotkeyBinding"/>.
    /// </remarks>
    public static readonly SettingKey<string> NewNoteHotkeyBinding =
        new(
            "activation.new-note.binding",
            "Ctrl+Shift+Win+N",
            static value => HotkeyChord.TryParse(value, out _));

    /// <summary>
    /// Whether the global note-from-clipboard hotkey is registered (F18, #157; parity B3, D43, G4).
    /// </summary>
    /// <remarks>Default off, as every global chord but show/hide (principle 7).</remarks>
    public static readonly SettingKey<bool> ClipboardNoteHotkeyEnabled =
        new("activation.clipboard-note.enabled", false);

    /// <summary>
    /// The chord that shows Noto with a new note holding the clipboard (F18, #157).
    /// </summary>
    /// <remarks>
    /// Default <c>Ctrl+Shift+Win+V</c>, the implementation plan's §10.1 final default for G4 (measured free). Valid
    /// means it parses as a <see cref="HotkeyChord"/>, as for <see cref="HotkeyBinding"/>.
    /// </remarks>
    public static readonly SettingKey<string> ClipboardNoteHotkeyBinding =
        new(
            "activation.clipboard-note.binding",
            "Ctrl+Shift+Win+V",
            static value => HotkeyChord.TryParse(value, out _));

    /// <summary>
    /// Whether the global search hotkey is registered (F21, #159; parity E2, G3).
    /// </summary>
    /// <remarks>Default off, as every global chord but show/hide (principle 7).</remarks>
    public static readonly SettingKey<bool> SearchHotkeyEnabled =
        new("activation.search.enabled", false);

    /// <summary>
    /// The chord that shows Noto with Search open (F21, #159).
    /// </summary>
    /// <remarks>
    /// Default <c>Ctrl+Shift+Win+F</c>, the implementation plan's §10.1 final default for G3 (measured free). Valid
    /// means it parses as a <see cref="HotkeyChord"/>, as for <see cref="HotkeyBinding"/>.
    /// </remarks>
    public static readonly SettingKey<string> SearchHotkeyBinding =
        new(
            "activation.search.binding",
            "Ctrl+Shift+Win+F",
            static value => HotkeyChord.TryParse(value, out _));

    /// <summary>
    /// Whether the global change-side hotkey is registered (#198; parity G5).
    /// </summary>
    /// <remarks>Default off, as every global chord but show/hide (principle 7).</remarks>
    public static readonly SettingKey<bool> ChangeSideHotkeyEnabled =
        new("activation.change-side.enabled", false);

    /// <summary>
    /// The chord that moves Noto to the other screen edge (#198).
    /// </summary>
    /// <remarks>
    /// Default <c>Ctrl+Shift+Win+S</c>, S for side: the <c>Ctrl+Shift+Win</c> family of the other three (no AltGr
    /// chord, parity §12a), and measured free (implementation plan §10: only B, L, X, the digits and Space are taken
    /// there; B and the digits are Windows' own). Used only once the user turns the shortcut on. Valid means it parses
    /// as a <see cref="HotkeyChord"/>, as for <see cref="HotkeyBinding"/>.
    /// </remarks>
    public static readonly SettingKey<string> ChangeSideHotkeyBinding =
        new(
            "activation.change-side.binding",
            "Ctrl+Shift+Win+S",
            static value => HotkeyChord.TryParse(value, out _));

    /// <summary>
    /// Whether the global keep-open hotkey is registered (#198; parity G6). Ctrl+D inside Noto (#146) is unaffected.
    /// </summary>
    /// <remarks>Default off, as every global chord but show/hide (principle 7).</remarks>
    public static readonly SettingKey<bool> KeepOpenHotkeyEnabled =
        new("activation.keep-open.enabled", false);

    /// <summary>
    /// The chord that turns Keep Noto open on or off (#198).
    /// </summary>
    /// <remarks>
    /// Default <c>Ctrl+Shift+Win+K</c>, K for keep, chosen as <see cref="ChangeSideHotkeyBinding"/>'s was. Valid means
    /// it parses as a <see cref="HotkeyChord"/>, as for <see cref="HotkeyBinding"/>.
    /// </remarks>
    public static readonly SettingKey<string> KeepOpenHotkeyBinding =
        new(
            "activation.keep-open.binding",
            "Ctrl+Shift+Win+K",
            static value => HotkeyChord.TryParse(value, out _));

    /// <summary>
    /// Whether the pointer resting at the docked screen edge shows Noto: Hot Side (#185; parity A4).
    /// </summary>
    /// <remarks>
    /// Default off: no surprise reveals, and each activation surface is independently disableable (principle 7).
    /// Applied at once; with this and <see cref="DragRevealEnabled"/> both off, Noto observes no pointer at all (ADR-007 §9).
    /// </remarks>
    public static readonly SettingKey<bool> HotSideEnabled =
        new("activation.hot-side.enabled", false);

    /// <summary>
    /// Whether dragging something to the docked screen edge shows Noto, so it can be dropped there (#185; parity A15,
    /// J27, J28).
    /// </summary>
    /// <remarks>Default off, as <see cref="HotSideEnabled"/>, and independent of it.</remarks>
    public static readonly SettingKey<bool> DragRevealEnabled =
        new("activation.drag-reveal.enabled", false);

    /// <summary>
    /// Whether the Open Bar, a small handle at the docked screen edge that shows Noto when clicked, is there while Noto
    /// is hidden, and how it hides (#185; parity A3, J1): Off, AlwaysShown or HideWhenIdle.
    /// </summary>
    /// <remarks>
    /// Default <see cref="Activation.OpenBarMode.Off"/>: a handle on the user's screen is theirs to ask for, and every activation
    /// surface is independently disableable (principle 7). With it off there is no bar window at all (ADR-007 §10).
    /// Stored by member name; a value that is not a member is refused when written and reads as the default.
    /// </remarks>
    public static readonly SettingKey<OpenBarMode> OpenBarMode =
        new("activation.open-bar.mode", Activation.OpenBarMode.Off, Enum.IsDefined);

    /// <summary>
    /// Whether files dropped on the Open Bar are imported, as a drop on Noto's lists imports them (#185; parity J27).
    /// </summary>
    /// <remarks>
    /// Default on: the bar itself is off by default, and a drop the user aims at it is no surprise. Off, the bar takes no
    /// drop. Independent of drag to reveal (<see cref="DragRevealEnabled"/>), which shows Noto for a drop into it.
    /// </remarks>
    public static readonly SettingKey<bool> OpenBarDrop =
        new("activation.open-bar-drop.enabled", true);

    /// <summary>
    /// The vertical screen edge the workspace docks to.
    /// </summary>
    /// <remarks>
    /// Default <see cref="Edge.Right"/> — parity A2 and J3, "default Right".
    /// Stored by member name and parsed case-sensitively, so <c>left</c> or
    /// <c>0</c> is not a legal value and reads as the default; a value that is
    /// not a member is refused when written. Read at launch (#16 slice 3), and
    /// changed from the settings surface, which re-docks the workspace at once
    /// (Slice 1).
    /// </remarks>
    public static readonly SettingKey<Edge> WorkspaceEdge =
        new("workspace.edge", Edge.Right, Enum.IsDefined);

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
    /// parsed case-sensitively; a value that is not a member is refused when
    /// written. Read at launch (#16 slice 6) and changed from the settings
    /// surface, taking effect on the next Escape (Slice 1).
    /// </remarks>
    public static readonly SettingKey<EscapeBehavior> WorkspaceEscape =
        new("workspace.escape", EscapeBehavior.LeaveFolderOrHide, Enum.IsDefined);

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
    /// session. Read at launch and changed from the settings surface, taking
    /// effect on the next deactivation (Slice 1).
    /// </para>
    /// </remarks>
    public static readonly SettingKey<bool> WorkspaceHideOnDeactivation =
        new("workspace.hide-on-deactivation", true);

    /// <summary>
    /// Whether one click opens a folder or a note (parity J7, C4); otherwise a
    /// double click does.
    /// </summary>
    /// <remarks>
    /// Default <see langword="false"/>: double click, the behaviour Noto has
    /// always had. A pointer setting only: the keyboard paths (<c>Ctrl+Down</c>,
    /// <c>Enter</c>) are the same either way.
    /// </remarks>
    public static readonly SettingKey<bool> WorkspaceSingleClickOpen =
        new("workspace.single-click-open", false);

    /// <summary>
    /// Whether Noto starts in the folder last opened (parity C14, J10, #193), when it still exists; otherwise on the
    /// folder list.
    /// </summary>
    /// <remarks>
    /// Default <see langword="false"/>: the folder list, where Noto has always started. Read once, at start; a change
    /// takes effect at the next start, so nothing applies it live.
    /// </remarks>
    public static readonly SettingKey<bool> WorkspaceOpenLastFolder =
        new("workspace.open-last-folder", false);

    /// <summary>
    /// The folders most recently opened (parity C5, C6, G10, #193): Back's menu, Ctrl+Shift+O and the start in the
    /// last folder.
    /// </summary>
    /// <remarks>
    /// Their ids, most recent first, comma-separated, at most <see cref="RecentFolders.Capacity"/>. Written each time a
    /// folder is entered, by any route, never by a settings UI. Not <see cref="NotesRecentMoveFolders"/>: moving a note
    /// to a folder is not opening it. A value that is not such a list is refused when written and reads as empty.
    /// </remarks>
    public static readonly SettingKey<string> WorkspaceRecentFolders =
        new("workspace.recent-folders", string.Empty, RecentFolders.IsValid);

    /// <summary>
    /// The workspace's theme (parity J21, F1): System, Light or Dark.
    /// </summary>
    /// <remarks>
    /// Default <see cref="WorkspaceTheme.System"/>, following Windows. High
    /// contrast is not a value here: Windows applies it over any of the three.
    /// Stored by member name; a value that is not a member is refused when
    /// written and reads as the default.
    /// </remarks>
    public static readonly SettingKey<WorkspaceTheme> AppearanceTheme =
        new("appearance.theme", WorkspaceTheme.System, Enum.IsDefined);

    /// <summary>
    /// The family notes are written and read in (F13, #154; parity D47, J12).
    /// </summary>
    /// <remarks>
    /// Empty for the design system's own, or one of <see cref="NoteText.Fonts"/>, exactly; anything else is refused when
    /// written and reads as the default. A family later missing from Windows still falls back (D51).
    /// </remarks>
    public static readonly SettingKey<string> AppearanceNoteFont =
        new("appearance.note-font", NoteText.DefaultFont, NoteText.IsFont);

    /// <summary>
    /// The size notes are written and read in, in DIPs (F13, #154; parity D48, J13, G41, G42).
    /// </summary>
    /// <remarks>
    /// Default 14, the design system's body size; one of <see cref="NoteText.Sizes"/>, which the size shortcuts step
    /// through. Anything else is refused when written and reads as the default.
    /// </remarks>
    public static readonly SettingKey<double> AppearanceNoteSize =
        new("appearance.note-size", NoteText.DefaultSize, NoteText.IsSize);

    /// <summary>
    /// The note text's line height (#234; parity D49, J14): Compact, Normal or Relaxed.
    /// </summary>
    /// <remarks>
    /// Default <see cref="TextSpacing.Normal"/>, the font's own line height, Noto's look before the setting. What each
    /// preset measures is <see cref="NoteText.LineHeight"/>. Stored by member name; a value that is not a member is
    /// refused when written and reads as the default.
    /// </remarks>
    public static readonly SettingKey<TextSpacing> AppearanceLineHeight =
        new("appearance.line-height", TextSpacing.Normal, Enum.IsDefined);

    /// <summary>
    /// The note text's paragraph spacing (#234; parity D50, J15): Compact, Normal or Relaxed.
    /// </summary>
    /// <remarks>
    /// Default <see cref="TextSpacing.Normal"/>, Noto's look before the setting. What each preset measures is
    /// <see cref="NoteText.SpaceAfter"/> and <see cref="NoteText.SpacesHeadings"/>. Stored by member name; a value that
    /// is not a member is refused when written and reads as the default.
    /// </remarks>
    public static readonly SettingKey<TextSpacing> AppearanceParagraphSpacing =
        new("appearance.paragraph-spacing", TextSpacing.Normal, Enum.IsDefined);

    /// <summary>
    /// How a coloured card shows its colour (#191; parity B15, F10, J23): Background or Bar.
    /// </summary>
    /// <remarks>
    /// Default <see cref="NoteColorStyle.Background"/>, the tint and marker Noto drew before the setting existed (#150).
    /// Stored by member name; a value that is not a member is refused when written and reads as the default.
    /// </remarks>
    public static readonly SettingKey<NoteColorStyle> AppearanceNoteColorStyle =
        new("appearance.note-color-style", NoteColorStyle.Background, Enum.IsDefined);

    /// <summary>
    /// Whether a small formatting toolbar shows above selected text in the editor (F8, #151).
    /// </summary>
    /// <remarks>Default on, as SideNotes has it; the keys and the Format menu do the same either way.</remarks>
    public static readonly SettingKey<bool> EditorFormatToolbar =
        new("editor.format-toolbar", true);

    /// <summary>
    /// How a file or folder link is drawn in the editor (J16, #196): Standard or Compact.
    /// </summary>
    /// <remarks>
    /// Default <see cref="FileLinkStyle.Standard"/>: the icon, the name, and the type and size below it. Stored by member
    /// name; a value that is not a member is refused when written and reads as the default.
    /// </remarks>
    public static readonly SettingKey<FileLinkStyle> EditorFileLinks =
        new("editor.file-links", FileLinkStyle.Standard, Enum.IsDefined);

    /// <summary>
    /// What a picture dropped or pasted into a note becomes (J30, #196): a copy kept in the note, or a shortcut.
    /// </summary>
    /// <remarks>
    /// Default <see cref="PictureDrop.KeepCopy"/>, Noto's behaviour before the setting existed (ADR-019 §4). Stored by
    /// member name; a value that is not a member is refused when written and reads as the default.
    /// </remarks>
    public static readonly SettingKey<PictureDrop> ImportingPicture =
        new("importing.picture", PictureDrop.KeepCopy, Enum.IsDefined);

    /// <summary>
    /// What a text file dropped or pasted into a note becomes (J30, #196): a shortcut, or its text.
    /// </summary>
    /// <remarks>
    /// Default <see cref="TextFileDrop.Shortcut"/>, the editor's behaviour before the setting existed (D34). Files dropped
    /// on the list are still imported as notes (F9). Stored by member name; a value that is not a member is refused when
    /// written and reads as the default.
    /// </remarks>
    public static readonly SettingKey<TextFileDrop> ImportingTextFile =
        new("importing.text-file", TextFileDrop.Shortcut, Enum.IsDefined);

    /// <summary>
    /// Whether a file dropped on the list is moved to Windows' Recycle Bin once its note is stored (J29, #267).
    /// </summary>
    /// <remarks>
    /// Default off: it touches files Noto does not own, so it is never on unless chosen. Pictures dropped into a note,
    /// Settings' data import and pastes are never affected; which files may go is <c>ImportedFiles</c>'.
    /// </remarks>
    public static readonly SettingKey<bool> ImportingRecycleFiles =
        new("importing.recycle-files", false);

    /// <summary>
    /// Whether plain text that reads as code is pasted in a fenced code block (D39, J20, #187; <c>PastedCode</c>).
    /// </summary>
    /// <remarks>Default off: a paste the user did not ask to reformat stays exactly what they copied.</remarks>
    public static readonly SettingKey<bool> EditorPasteCodeBlock =
        new("editor.paste-code-block", false);

    /// <summary>
    /// Whether Noto backs itself up into the data root's backups, keeping the last seven (F15, #15), as often as
    /// <see cref="BackupFrequency"/> says.
    /// </summary>
    /// <remarks>Default on: a backup nobody remembered to take is the one that is missing when it is needed.</remarks>
    public static readonly SettingKey<bool> BackupAutomatic =
        new("backup.automatic", true);

    /// <summary>
    /// How often the automatic backup is taken (#199; parity I8, J38): once a day, or every 45, 30 or 15 minutes.
    /// </summary>
    /// <remarks>
    /// Default <see cref="Frequency.Daily"/>, what Noto has always done. Stored by member name; a value that is not a
    /// member is refused when written and reads as the default. A change applies at once, without a restart.
    /// </remarks>
    public static readonly SettingKey<Frequency> BackupFrequency =
        new("backup.frequency", Frequency.Daily, Enum.IsDefined);

    /// <summary>
    /// The folder automatic backups go to (#233; parity I11, J39): empty for the data root's backups, or an absolute
    /// path the user chose, OneDrive's say.
    /// </summary>
    /// <remarks>
    /// Default empty, where Noto has always written them. A path not in its normal form is refused when written and
    /// reads as the default (<see cref="AutomaticBackups.IsFolder"/>); one inside the data root is refused before it is
    /// written (<see cref="AutomaticBackups.FolderFor"/>). A change applies at once: the next check reads the new folder.
    /// </remarks>
    public static readonly SettingKey<string> BackupFolder =
        new("backup.folder", AutomaticBackups.DefaultFolder, AutomaticBackups.IsFolder);

    /// <summary>
    /// Where a new note goes in its folder (parity B5, J8): Bottom, Top, AboveSelected or BelowSelected.
    /// </summary>
    /// <remarks>
    /// Default <see cref="NewNotePlacement.Bottom"/>, Noto's behaviour before the setting existed. Stored by member
    /// name; a value that is not a member is refused when written and reads as the default. Chosen in Settings, General
    /// (#188).
    /// </remarks>
    public static readonly SettingKey<NewNotePlacement> NotesNewPlacement =
        new("notes.new-placement", NewNotePlacement.Bottom, Enum.IsDefined);

    /// <summary>
    /// The mode a new note starts in (#195; parity D28, J11): Markdown, Code or PlainText.
    /// </summary>
    /// <remarks>
    /// Default <see cref="NoteMode.Markdown"/>, every note before the setting existed. Applied by note creation (Ctrl+N,
    /// the new-note shortcuts, a note from the clipboard); a note's own mode is changed from its card's menu, and never
    /// changes with this. Stored by member name; a value that is not a member is refused when written and reads as the
    /// default.
    /// </remarks>
    public static readonly SettingKey<NoteMode> NotesNewMode =
        new("notes.new-mode", NoteMode.Markdown, Enum.IsDefined);

    /// <summary>
    /// Whether a note made from a global shortcut asks which folder it goes into (#243; parity B6, J9).
    /// </summary>
    /// <remarks>
    /// Default <see langword="false"/>: the note goes where it always has, the open folder, else the selected one, else
    /// the first. On, the new-note and clipboard-note shortcuts and the tray's New note from clipboard show a folder
    /// chooser first; Ctrl+N, the + button and a <c>noto://</c> link never ask. Read at each press.
    /// </remarks>
    public static readonly SettingKey<bool> NotesAskFolder =
        new("notes.ask-folder", false);

    /// <summary>
    /// The folders notes were most recently moved to (B11): the move menu's first choices.
    /// </summary>
    /// <remarks>
    /// Their ids, most recent first, comma-separated, at most <see cref="RecentFolders.Capacity"/>. Written after each
    /// move, never by a settings UI. A value that is not such a list is refused when written and reads as empty.
    /// </remarks>
    public static readonly SettingKey<string> NotesRecentMoveFolders =
        new("notes.recent-move-folders", string.Empty, RecentFolders.IsValid);

    /// <summary>
    /// Where this workspace stands with its welcome (ADR-021).
    /// </summary>
    /// <remarks>
    /// Default <see cref="WelcomeState.None"/>: a workspace with no row, one opened, upgraded or restored, never shows
    /// the welcome. Only startup writes <see cref="WelcomeState.Due"/>, and only for a database it has just created;
    /// finishing or skipping the welcome writes how it ended. Stored by member name; a value that is not a member is
    /// refused when written and reads as the default. No settings row: Settings' Welcome tour opens it without
    /// changing this.
    /// </remarks>
    public static readonly SettingKey<WelcomeState> OnboardingWelcome =
        new("onboarding.welcome", WelcomeState.None, Enum.IsDefined);

    /// <summary>
    /// How much Noto writes to its log (#7).
    /// </summary>
    /// <remarks>
    /// Default <see cref="LogLevel.Information"/>. The log runs at that level
    /// from before settings load — a database or settings failure must reach
    /// it — and takes this value once settings are read, and again whenever it
    /// changes. No settings UI yet.
    /// </remarks>
    public static readonly SettingKey<LogLevel> DiagnosticsLogLevel =
        new("diagnostics.log-level", LogLevel.Information);

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
        NewNoteHotkeyEnabled,
        NewNoteHotkeyBinding,
        ClipboardNoteHotkeyEnabled,
        ClipboardNoteHotkeyBinding,
        SearchHotkeyEnabled,
        SearchHotkeyBinding,
        ChangeSideHotkeyEnabled,
        ChangeSideHotkeyBinding,
        KeepOpenHotkeyEnabled,
        KeepOpenHotkeyBinding,
        HotSideEnabled,
        DragRevealEnabled,
        OpenBarMode,
        OpenBarDrop,
        WorkspaceEdge,
        WorkspaceWidth,
        WorkspaceEscape,
        WorkspaceHideOnDeactivation,
        WorkspaceSingleClickOpen,
        WorkspaceOpenLastFolder,
        WorkspaceRecentFolders,
        AppearanceTheme,
        AppearanceNoteFont,
        AppearanceNoteSize,
        AppearanceLineHeight,
        AppearanceParagraphSpacing,
        AppearanceNoteColorStyle,
        EditorFormatToolbar,
        EditorFileLinks,
        ImportingPicture,
        ImportingTextFile,
        ImportingRecycleFiles,
        EditorPasteCodeBlock,
        BackupAutomatic,
        BackupFrequency,
        BackupFolder,
        NotesNewPlacement,
        NotesNewMode,
        NotesAskFolder,
        NotesRecentMoveFolders,
        OnboardingWelcome,
        DiagnosticsLogLevel,
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
