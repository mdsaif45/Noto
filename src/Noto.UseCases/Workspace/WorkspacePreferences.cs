using Noto.Core.Activation;
using Noto.Core.Folders;
using Noto.Core.Links;
using Noto.Core.Notes;
using Noto.Core.Portability;
using Noto.Core.Settings;
using Noto.Core.Storage;
using Noto.Core.Workspace;

namespace Noto.UseCases.Workspace;

/// <summary>
/// The workspace's preferences: its edge and width (#16 slice 3), and every
/// setting the settings surface changes (Slice 1).
/// </summary>
/// <remarks>
/// <para>
/// <b>Preferences, not a domain mutation.</b> Settings are not entities
/// (<see cref="ISettingsStore"/>), so this reads and writes the store
/// directly rather than dispatching a command (ADR-010's commands are for
/// notes, folders and their kin).
/// </para>
/// <para>
/// <b>The one way the settings surface writes.</b> Each setter writes one key
/// through the store, which raises <see cref="ISettingsStore.SettingChanged"/>;
/// whatever owns that preference at run time (the window, the dock, the
/// hotkey) applies it from there. A setter returns whether the value was
/// kept: a value the key refuses, or a storage failure, returns
/// <see langword="false"/> and changes nothing.
/// </para>
/// <para>
/// <b>Widths here are what the user chose, not what fits.</b> Clamping to a
/// display's live work area is the platform's job, done against that display
/// at the moment of use. A stored width is therefore never rewritten because
/// it did not fit: 5000 DIP stays 5000 DIP in the database however narrow the
/// display it is shown on.
/// </para>
/// <para>
/// The display is identified by an opaque scope — the platform's monitor
/// identity, as text. Nothing here knows what a monitor is.
/// </para>
/// </remarks>
public sealed class WorkspacePreferences(ISettingsStore settings)
{
    private readonly ISettingsStore _settings = settings ?? throw new ArgumentNullException(nameof(settings));

    /// <summary>The edge to dock to (parity A2, J3).</summary>
    public WorkspaceEdge Edge => _settings.Read(SettingKeys.WorkspaceEdge);

    /// <summary>What Escape does (parity A12).</summary>
    public EscapeBehavior Escape => _settings.Read(SettingKeys.WorkspaceEscape);

    /// <summary>
    /// Whether the shown workspace hides when activation moves to another
    /// application (parity A13, "close on outside click").
    /// </summary>
    public bool HideOnDeactivation => _settings.Read(SettingKeys.WorkspaceHideOnDeactivation);

    /// <summary>Whether one click opens a folder or a note (parity J7, C4).</summary>
    public bool SingleClickOpen => _settings.Read(SettingKeys.WorkspaceSingleClickOpen);

    /// <summary>Whether the pointer resting at the docked edge shows Noto: Hot Side (#185; parity A4).</summary>
    public bool HotSide => _settings.Read(SettingKeys.HotSideEnabled);

    /// <summary>Whether dragging something to the docked edge shows Noto (#185; parity A15, J27, J28).</summary>
    public bool DragReveal => _settings.Read(SettingKeys.DragRevealEnabled);

    /// <summary>Whether the Open Bar is there while Noto is hidden, and how it hides (#185; parity A3, J1).</summary>
    public OpenBarMode OpenBar => _settings.Read(SettingKeys.OpenBarMode);

    /// <summary>Whether files dropped on the Open Bar are imported (#185; parity J27).</summary>
    public bool OpenBarDrop => _settings.Read(SettingKeys.OpenBarDrop);

    /// <summary>Whether Noto starts in the folder last opened (parity C14, J10, #193).</summary>
    public bool OpenLastFolder => _settings.Read(SettingKeys.WorkspaceOpenLastFolder);

    /// <summary>The workspace's theme (parity J21).</summary>
    public WorkspaceTheme Theme => _settings.Read(SettingKeys.AppearanceTheme);

    /// <summary>The family notes are shown in: empty for the design system's own (F13, D47).</summary>
    public string NoteFont => _settings.Read(SettingKeys.AppearanceNoteFont);

    /// <summary>The size notes are shown in, in DIPs (F13, D48).</summary>
    public double NoteSize => _settings.Read(SettingKeys.AppearanceNoteSize);

    /// <summary>The note text's line height (#234, D49).</summary>
    public TextSpacing LineHeight => _settings.Read(SettingKeys.AppearanceLineHeight);

    /// <summary>The note text's paragraph spacing (#234, D50).</summary>
    public TextSpacing ParagraphSpacing => _settings.Read(SettingKeys.AppearanceParagraphSpacing);

    /// <summary>How a coloured card shows its colour: tinted with a bar, or the bar only (#191, F10).</summary>
    public NoteColorStyle NoteColorStyle => _settings.Read(SettingKeys.AppearanceNoteColorStyle);

    /// <summary>The colour a new note starts with, or <see langword="null"/> for none (#245, J24).</summary>
    public string? NewNoteColor => _settings.Read(SettingKeys.AppearanceNewNoteColor) is { Length: > 0 } key ? key : null;

    /// <summary>Whether a <c>#rrggbb</c> code shows a chip of its colour on the card (#245, F14).</summary>
    public bool ColorChips => _settings.Read(SettingKeys.AppearanceColorChips);

    /// <summary>Whether the formatting toolbar shows above selected text (F8).</summary>
    public bool FormatToolbar => _settings.Read(SettingKeys.EditorFormatToolbar);

    /// <summary>How a file or folder link is drawn in the editor (J16, #196).</summary>
    public FileLinkStyle FileLinks => _settings.Read(SettingKeys.EditorFileLinks);

    /// <summary>What a picture dropped into a note becomes (J30, #196).</summary>
    public PictureDrop PictureDrop => _settings.Read(SettingKeys.ImportingPicture);

    /// <summary>What a text file dropped into a note becomes (J30, #196).</summary>
    public TextFileDrop TextFileDrop => _settings.Read(SettingKeys.ImportingTextFile);

    /// <summary>Whether files dropped on the list go to Windows' Recycle Bin once their notes are stored (J29, #267).</summary>
    public bool RecycleImportedFiles => _settings.Read(SettingKeys.ImportingRecycleFiles);

    /// <summary>Whether plain text that reads as code is pasted in a fenced code block (D39).</summary>
    public bool PasteCodeBlock => _settings.Read(SettingKeys.EditorPasteCodeBlock);

    /// <summary>Whether checking a task moves it to the bottom of its list (D23, #244).</summary>
    public bool CheckedTasksToBottom => _settings.Read(SettingKeys.EditorCheckedTasksToBottom);

    /// <summary>Whether Noto backs itself up (F15).</summary>
    public bool AutomaticBackups => _settings.Read(SettingKeys.BackupAutomatic);

    /// <summary>How often the automatic backup is taken (#199).</summary>
    public BackupFrequency BackupFrequency => _settings.Read(SettingKeys.BackupFrequency);

    /// <summary>The folder automatic backups go to (#233): empty for the data root's backups.</summary>
    public string BackupFolder => _settings.Read(SettingKeys.BackupFolder);

    /// <summary>Where a new note goes in its folder (parity B5, J8).</summary>
    public NewNotePlacement NewNotePlacement => _settings.Read(SettingKeys.NotesNewPlacement);

    /// <summary>The mode a new note starts in: "New notes are" (#195, D28).</summary>
    public NoteMode NewNoteMode => _settings.Read(SettingKeys.NotesNewMode);

    /// <summary>Whether a note from a global shortcut asks which folder it goes into (#243; B6, J9).</summary>
    public bool AskFolder => _settings.Read(SettingKeys.NotesAskFolder);

    /// <summary>Whether the global show/hide hotkey is registered (parity A7).</summary>
    public bool HotkeyEnabled => _settings.Read(SettingKeys.HotkeyEnabled);

    /// <summary>The show/hide chord, as text (parity G1).</summary>
    public string HotkeyBinding => _settings.Read(SettingKeys.HotkeyBinding);

    /// <summary>Whether the global new-note hotkey is registered (F17, B2).</summary>
    public bool NewNoteHotkeyEnabled => _settings.Read(SettingKeys.NewNoteHotkeyEnabled);

    /// <summary>The new-note chord, as text (F17, G2).</summary>
    public string NewNoteHotkeyBinding => _settings.Read(SettingKeys.NewNoteHotkeyBinding);

    /// <summary>Whether the global note-from-clipboard hotkey is registered (F18, B3).</summary>
    public bool ClipboardNoteHotkeyEnabled => _settings.Read(SettingKeys.ClipboardNoteHotkeyEnabled);

    /// <summary>The note-from-clipboard chord, as text (F18, G4).</summary>
    public string ClipboardNoteHotkeyBinding => _settings.Read(SettingKeys.ClipboardNoteHotkeyBinding);

    /// <summary>Whether the global search hotkey is registered (F21, E2).</summary>
    public bool SearchHotkeyEnabled => _settings.Read(SettingKeys.SearchHotkeyEnabled);

    /// <summary>The search chord, as text (F21, G3).</summary>
    public string SearchHotkeyBinding => _settings.Read(SettingKeys.SearchHotkeyBinding);

    /// <summary>Whether the global change-side hotkey is registered (#198, G5).</summary>
    public bool ChangeSideHotkeyEnabled => _settings.Read(SettingKeys.ChangeSideHotkeyEnabled);

    /// <summary>The change-side chord, as text (#198, G5).</summary>
    public string ChangeSideHotkeyBinding => _settings.Read(SettingKeys.ChangeSideHotkeyBinding);

    /// <summary>Whether the global keep-open hotkey is registered (#198, G6).</summary>
    public bool KeepOpenHotkeyEnabled => _settings.Read(SettingKeys.KeepOpenHotkeyEnabled);

    /// <summary>The keep-open chord, as text (#198, G6).</summary>
    public string KeepOpenHotkeyBinding => _settings.Read(SettingKeys.KeepOpenHotkeyBinding);

    /// <summary>Whether <paramref name="action"/>'s hotkey is registered.</summary>
    public bool HotkeyEnabledFor(GlobalAction action) => action switch
    {
        GlobalAction.NewNote => NewNoteHotkeyEnabled,
        GlobalAction.NoteFromClipboard => ClipboardNoteHotkeyEnabled,
        GlobalAction.Search => SearchHotkeyEnabled,
        GlobalAction.ChangeSide => ChangeSideHotkeyEnabled,
        GlobalAction.KeepOpen => KeepOpenHotkeyEnabled,
        _ => HotkeyEnabled,
    };

    /// <summary><paramref name="action"/>'s chord, as text.</summary>
    public string HotkeyBindingFor(GlobalAction action) => action switch
    {
        GlobalAction.NewNote => NewNoteHotkeyBinding,
        GlobalAction.NoteFromClipboard => ClipboardNoteHotkeyBinding,
        GlobalAction.Search => SearchHotkeyBinding,
        GlobalAction.ChangeSide => ChangeSideHotkeyBinding,
        GlobalAction.KeepOpen => KeepOpenHotkeyBinding,
        _ => HotkeyBinding,
    };

    /// <summary>Stores <paramref name="action"/>'s chord: only its rebind calls this, once the chord is registered.</summary>
    /// <returns>Whether it was kept.</returns>
    public bool SetHotkeyBindingFor(GlobalAction action, HotkeyChord chord) => action switch
    {
        GlobalAction.NewNote => SetNewNoteHotkeyBinding(chord),
        GlobalAction.NoteFromClipboard => SetClipboardNoteHotkeyBinding(chord),
        GlobalAction.Search => SetSearchHotkeyBinding(chord),
        GlobalAction.ChangeSide => SetChangeSideHotkeyBinding(chord),
        GlobalAction.KeepOpen => SetKeepOpenHotkeyBinding(chord),
        _ => SetHotkeyBinding(chord),
    };

    /// <summary>Sets the edge to dock to.</summary>
    /// <returns>Whether it was kept.</returns>
    public bool SetEdge(WorkspaceEdge edge) => TryWrite(SettingKeys.WorkspaceEdge, edge);

    /// <summary>Sets what Escape does.</summary>
    /// <returns>Whether it was kept.</returns>
    public bool SetEscape(EscapeBehavior escape) => TryWrite(SettingKeys.WorkspaceEscape, escape);

    /// <summary>Sets whether the workspace hides when another application is activated.</summary>
    /// <returns>Whether it was kept.</returns>
    public bool SetHideOnDeactivation(bool hide) => TryWrite(SettingKeys.WorkspaceHideOnDeactivation, hide);

    /// <summary>Sets whether one click opens a folder or a note.</summary>
    /// <returns>Whether it was kept.</returns>
    public bool SetSingleClickOpen(bool singleClick) => TryWrite(SettingKeys.WorkspaceSingleClickOpen, singleClick);

    /// <summary>Sets whether the pointer resting at the docked edge shows Noto (#185).</summary>
    /// <returns>Whether it was kept.</returns>
    public bool SetHotSide(bool on) => TryWrite(SettingKeys.HotSideEnabled, on);

    /// <summary>Sets whether dragging something to the docked edge shows Noto (#185).</summary>
    /// <returns>Whether it was kept.</returns>
    public bool SetDragReveal(bool on) => TryWrite(SettingKeys.DragRevealEnabled, on);

    /// <summary>Sets whether the Open Bar is there, and how it hides (#185).</summary>
    /// <returns>Whether it was kept: a value that is not a mode is refused.</returns>
    public bool SetOpenBar(OpenBarMode mode) => TryWrite(SettingKeys.OpenBarMode, mode);

    /// <summary>Sets whether files dropped on the Open Bar are imported (#185).</summary>
    /// <returns>Whether it was kept.</returns>
    public bool SetOpenBarDrop(bool on) => TryWrite(SettingKeys.OpenBarDrop, on);

    /// <summary>Sets the mode a new note starts in (#195, D28). Notes already written keep their own.</summary>
    /// <returns>Whether it was kept: a value that is not a mode is refused.</returns>
    public bool SetNewNoteMode(NoteMode mode) => TryWrite(SettingKeys.NotesNewMode, mode);

    /// <summary>Sets whether a note from a global shortcut asks which folder it goes into (#243; B6, J9).</summary>
    /// <returns>Whether it was kept.</returns>
    public bool SetAskFolder(bool ask) => TryWrite(SettingKeys.NotesAskFolder, ask);

    /// <summary>Sets whether Noto starts in the folder last opened (#193); it takes effect at the next start.</summary>
    /// <returns>Whether it was kept.</returns>
    public bool SetOpenLastFolder(bool open) => TryWrite(SettingKeys.WorkspaceOpenLastFolder, open);

    /// <summary>Sets where a new note goes in its folder (parity B5, J8; #188).</summary>
    /// <returns>Whether it was kept: a value that is not a member is refused.</returns>
    public bool SetNewNotePlacement(NewNotePlacement placement) => TryWrite(SettingKeys.NotesNewPlacement, placement);

    /// <summary>Sets the workspace's theme.</summary>
    /// <returns>Whether it was kept.</returns>
    public bool SetTheme(WorkspaceTheme theme) => TryWrite(SettingKeys.AppearanceTheme, theme);

    /// <summary>Sets whether the formatting toolbar shows above selected text.</summary>
    /// <returns>Whether it was kept.</returns>
    public bool SetFormatToolbar(bool shown) => TryWrite(SettingKeys.EditorFormatToolbar, shown);

    /// <summary>Sets how a file or folder link is drawn in the editor.</summary>
    /// <returns>Whether it was kept.</returns>
    public bool SetFileLinks(FileLinkStyle style) => TryWrite(SettingKeys.EditorFileLinks, style);

    /// <summary>Sets what a picture dropped into a note becomes.</summary>
    /// <returns>Whether it was kept.</returns>
    public bool SetPictureDrop(PictureDrop drop) => TryWrite(SettingKeys.ImportingPicture, drop);

    /// <summary>Sets what a text file dropped into a note becomes.</summary>
    /// <returns>Whether it was kept.</returns>
    public bool SetTextFileDrop(TextFileDrop drop) => TryWrite(SettingKeys.ImportingTextFile, drop);

    /// <summary>Sets whether files dropped on the list go to Windows' Recycle Bin once their notes are stored.</summary>
    /// <returns>Whether it was kept.</returns>
    public bool SetRecycleImportedFiles(bool recycle) => TryWrite(SettingKeys.ImportingRecycleFiles, recycle);

    /// <summary>Sets whether plain text that reads as code is pasted in a fenced code block.</summary>
    /// <returns>Whether it was kept.</returns>
    public bool SetPasteCodeBlock(bool fenced) => TryWrite(SettingKeys.EditorPasteCodeBlock, fenced);

    /// <summary>Sets whether checking a task moves it to the bottom of its list (#244).</summary>
    /// <returns>Whether it was kept.</returns>
    public bool SetCheckedTasksToBottom(bool bottom) => TryWrite(SettingKeys.EditorCheckedTasksToBottom, bottom);

    /// <summary>Sets whether Noto backs itself up.</summary>
    /// <returns>Whether it was kept.</returns>
    public bool SetAutomaticBackups(bool automatic) => TryWrite(SettingKeys.BackupAutomatic, automatic);

    /// <summary>Sets how often the automatic backup is taken (#199).</summary>
    /// <returns>Whether it was kept.</returns>
    public bool SetBackupFrequency(BackupFrequency frequency) => TryWrite(SettingKeys.BackupFrequency, frequency);

    /// <summary>Sets the folder automatic backups go to (#233): empty for the data root's backups.</summary>
    /// <returns>Whether it was kept: a path not in its normal form is refused.</returns>
    public bool SetBackupFolder(string folder) => TryWrite(SettingKeys.BackupFolder, folder);

    /// <summary>Sets the family notes are shown in.</summary>
    /// <returns>Whether it was kept: a family outside <see cref="NoteText.Fonts"/> is refused.</returns>
    public bool SetNoteFont(string font) => TryWrite(SettingKeys.AppearanceNoteFont, font);

    /// <summary>Sets the size notes are shown in.</summary>
    /// <returns>Whether it was kept: a size outside <see cref="NoteText.Sizes"/> is refused.</returns>
    public bool SetNoteSize(double size) => TryWrite(SettingKeys.AppearanceNoteSize, size);

    /// <summary>Sets the note text's line height (#234).</summary>
    /// <returns>Whether it was kept: a value that is not a preset is refused.</returns>
    public bool SetLineHeight(TextSpacing spacing) => TryWrite(SettingKeys.AppearanceLineHeight, spacing);

    /// <summary>Sets the note text's paragraph spacing (#234).</summary>
    /// <returns>Whether it was kept: a value that is not a preset is refused.</returns>
    public bool SetParagraphSpacing(TextSpacing spacing) => TryWrite(SettingKeys.AppearanceParagraphSpacing, spacing);

    /// <summary>Sets how a coloured card shows its colour (#191).</summary>
    /// <returns>Whether it was kept: a value that is not a style is refused.</returns>
    public bool SetNoteColorStyle(NoteColorStyle style) => TryWrite(SettingKeys.AppearanceNoteColorStyle, style);

    /// <summary>Sets the colour a new note starts with (#245), <see langword="null"/> for none. Notes already written keep their own.</summary>
    /// <returns>Whether it was kept: a key that is not one of the six is refused.</returns>
    public bool SetNewNoteColor(string? colorKey) => TryWrite(SettingKeys.AppearanceNewNoteColor, colorKey ?? string.Empty);

    /// <summary>Sets whether a <c>#rrggbb</c> code shows a chip of its colour on the card (#245).</summary>
    /// <returns>Whether it was kept.</returns>
    public bool SetColorChips(bool shown) => TryWrite(SettingKeys.AppearanceColorChips, shown);

    /// <summary>
    /// Stores the show/hide chord (parity G50). Only the rebind calls this, and only once the chord is registered: the
    /// stored chord is the one that works.
    /// </summary>
    /// <returns>Whether it was kept.</returns>
    public bool SetHotkeyBinding(HotkeyChord chord) => TryWrite(SettingKeys.HotkeyBinding, chord.Canonical);

    /// <summary>Sets whether the global show/hide hotkey is registered.</summary>
    /// <returns>Whether it was kept. Registering it is the hotkey's owner's job, and may still fail.</returns>
    public bool SetHotkeyEnabled(bool enabled) => TryWrite(SettingKeys.HotkeyEnabled, enabled);

    /// <summary>Stores the new-note chord (F17): only its rebind calls this, once the chord is registered.</summary>
    /// <returns>Whether it was kept.</returns>
    public bool SetNewNoteHotkeyBinding(HotkeyChord chord) => TryWrite(SettingKeys.NewNoteHotkeyBinding, chord.Canonical);

    /// <summary>Sets whether the global new-note hotkey is registered (F17).</summary>
    /// <returns>Whether it was kept. Registering it is the hotkey's owner's job, and may still fail.</returns>
    public bool SetNewNoteHotkeyEnabled(bool enabled) => TryWrite(SettingKeys.NewNoteHotkeyEnabled, enabled);

    /// <summary>Stores the note-from-clipboard chord (F18): only its rebind calls this, once the chord is registered.</summary>
    /// <returns>Whether it was kept.</returns>
    public bool SetClipboardNoteHotkeyBinding(HotkeyChord chord) => TryWrite(SettingKeys.ClipboardNoteHotkeyBinding, chord.Canonical);

    /// <summary>Sets whether the global note-from-clipboard hotkey is registered (F18).</summary>
    /// <returns>Whether it was kept. Registering it is the hotkey's owner's job, and may still fail.</returns>
    public bool SetClipboardNoteHotkeyEnabled(bool enabled) => TryWrite(SettingKeys.ClipboardNoteHotkeyEnabled, enabled);

    /// <summary>Stores the search chord (F21): only its rebind calls this, once the chord is registered.</summary>
    /// <returns>Whether it was kept.</returns>
    public bool SetSearchHotkeyBinding(HotkeyChord chord) => TryWrite(SettingKeys.SearchHotkeyBinding, chord.Canonical);

    /// <summary>Sets whether the global search hotkey is registered (F21).</summary>
    /// <returns>Whether it was kept. Registering it is the hotkey's owner's job, and may still fail.</returns>
    public bool SetSearchHotkeyEnabled(bool enabled) => TryWrite(SettingKeys.SearchHotkeyEnabled, enabled);

    /// <summary>Stores the change-side chord (#198): only its rebind calls this, once the chord is registered.</summary>
    /// <returns>Whether it was kept.</returns>
    public bool SetChangeSideHotkeyBinding(HotkeyChord chord) => TryWrite(SettingKeys.ChangeSideHotkeyBinding, chord.Canonical);

    /// <summary>Sets whether the global change-side hotkey is registered (#198).</summary>
    /// <returns>Whether it was kept. Registering it is the hotkey's owner's job, and may still fail.</returns>
    public bool SetChangeSideHotkeyEnabled(bool enabled) => TryWrite(SettingKeys.ChangeSideHotkeyEnabled, enabled);

    /// <summary>Stores the keep-open chord (#198): only its rebind calls this, once the chord is registered.</summary>
    /// <returns>Whether it was kept.</returns>
    public bool SetKeepOpenHotkeyBinding(HotkeyChord chord) => TryWrite(SettingKeys.KeepOpenHotkeyBinding, chord.Canonical);

    /// <summary>Sets whether the global keep-open hotkey is registered (#198).</summary>
    /// <returns>Whether it was kept. Registering it is the hotkey's owner's job, and may still fail.</returns>
    public bool SetKeepOpenHotkeyEnabled(bool enabled) => TryWrite(SettingKeys.KeepOpenHotkeyEnabled, enabled);

    /// <summary>
    /// The width to open with on one display, before it is fitted to that
    /// display.
    /// </summary>
    /// <remarks>
    /// The first usable value of, in order: this display's own width, the
    /// width last chosen on any display, and the 360 DIP default. A missing,
    /// corrupt or invalid value at one step moves on to the next; it does not
    /// jump to the default.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="monitorScope"/> is blank.</exception>
    public double WidthFor(string monitorScope)
    {
        if (_settings.TryRead(SettingKeys.WorkspaceWidthByScope.For(monitorScope), out double own))
        {
            return own;
        }

        if (_settings.TryRead(SettingKeys.WorkspaceWidth, out double global))
        {
            return global;
        }

        return SettingKeys.WorkspaceWidth.Default;
    }

    /// <summary>
    /// Remembers a width the user finished resizing to, on one display and as
    /// the width for displays with none of their own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two writes, the display's own first. Each is attempted even if the
    /// other fails, because each is useful alone: the display's own width is
    /// what that display opens with, and the global one is what every other
    /// display falls back to. There is no transaction across them; a failure
    /// of either leaves the other's value in place, which the resolution
    /// order tolerates.
    /// </para>
    /// <para>
    /// A storage failure is not thrown to the caller. The width on screen is
    /// already the one the user chose; failing to remember it must not undo
    /// it.
    /// </para>
    /// </remarks>
    /// <returns>
    /// <see langword="true"/> when both widths were persisted;
    /// <see langword="false"/> when either was rejected as invalid or failed
    /// to write.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="monitorScope"/> is blank.</exception>
    public bool RememberWidth(string monitorScope, double widthDip)
    {
        SettingKey<double> own = SettingKeys.WorkspaceWidthByScope.For(monitorScope);

        bool ownSaved = TryWrite(own, widthDip);
        bool globalSaved = TryWrite(SettingKeys.WorkspaceWidth, widthDip);

        return ownSaved && globalSaved;
    }

    /// <summary>The folders notes were most recently moved to, most recent first (B11).</summary>
    public IReadOnlyList<FolderId> RecentMoveFolders => RecentFolders.Parse(_settings.Read(SettingKeys.NotesRecentMoveFolders));

    /// <summary>
    /// Records a move's destination as the most recent. Folders no longer among <paramref name="active"/> fall out of
    /// the list here, the next time it is written.
    /// </summary>
    /// <returns>Whether it was stored; a storage failure is never a move's failure.</returns>
    public bool RememberMoveTarget(FolderId folder, IReadOnlyList<Folder> active)
    {
        ArgumentNullException.ThrowIfNull(active);

        IReadOnlyList<FolderId> live = [.. RecentMoveFolders.Where(id => active.Any(f => f.Id == id))];
        return TryWrite(SettingKeys.NotesRecentMoveFolders, RecentFolders.Format(RecentFolders.Push(live, folder)));
    }

    /// <summary>The folders most recently opened, most recent first (#193). Not the move list: moving is not opening.</summary>
    public IReadOnlyList<FolderId> RecentOpenedFolders => RecentFolders.Parse(_settings.Read(SettingKeys.WorkspaceRecentFolders));

    /// <summary>
    /// Records <paramref name="folder"/> as the folder last opened. Folders not in <paramref name="existing"/> fall out
    /// of the list here, the next time it is written, as the move list's do.
    /// </summary>
    /// <returns>Whether it was stored; a storage failure never stops a folder opening.</returns>
    public bool RememberOpenedFolder(FolderId folder, IReadOnlyList<FolderId> existing)
    {
        ArgumentNullException.ThrowIfNull(existing);

        IReadOnlyList<FolderId> live = [.. RecentOpenedFolders.Where(existing.Contains)];
        return TryWrite(SettingKeys.WorkspaceRecentFolders, RecentFolders.Format(RecentFolders.Push(live, folder)));
    }

    private bool TryWrite<T>(SettingKey<T> key, T value)
    {
        try
        {
            return _settings.Write(key, value);
        }
        catch (StorageException)
        {
            return false;
        }
    }
}
