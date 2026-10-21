using Noto.Core.Activation;
using Noto.Core.Notes;
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

    /// <summary>The workspace's theme (parity J21).</summary>
    public WorkspaceTheme Theme => _settings.Read(SettingKeys.AppearanceTheme);

    /// <summary>Where a new note goes in its folder (parity B5, J8). Read only until its settings row exists.</summary>
    public NewNotePlacement NewNotePlacement => _settings.Read(SettingKeys.NotesNewPlacement);

    /// <summary>Whether the global show/hide hotkey is registered (parity A7).</summary>
    public bool HotkeyEnabled => _settings.Read(SettingKeys.HotkeyEnabled);

    /// <summary>The show/hide chord, as text (parity G1).</summary>
    public string HotkeyBinding => _settings.Read(SettingKeys.HotkeyBinding);

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

    /// <summary>Sets the workspace's theme.</summary>
    /// <returns>Whether it was kept.</returns>
    public bool SetTheme(WorkspaceTheme theme) => TryWrite(SettingKeys.AppearanceTheme, theme);

    /// <summary>
    /// Stores the show/hide chord (parity G50). Only the rebind calls this, and only once the chord is registered: the
    /// stored chord is the one that works.
    /// </summary>
    /// <returns>Whether it was kept.</returns>
    public bool SetHotkeyBinding(HotkeyChord chord) => TryWrite(SettingKeys.HotkeyBinding, chord.Canonical);

    /// <summary>Sets whether the global show/hide hotkey is registered.</summary>
    /// <returns>Whether it was kept. Registering it is the hotkey's owner's job, and may still fail.</returns>
    public bool SetHotkeyEnabled(bool enabled) => TryWrite(SettingKeys.HotkeyEnabled, enabled);

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
