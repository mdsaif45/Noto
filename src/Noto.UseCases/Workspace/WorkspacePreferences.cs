using Noto.Core.Settings;
using Noto.Core.Storage;
using Noto.Core.Workspace;

namespace Noto.UseCases.Workspace;

/// <summary>
/// The docked workspace's remembered edge and width (#16 slice 3).
/// </summary>
/// <remarks>
/// <para>
/// <b>Preferences, not a domain mutation.</b> Settings are not entities
/// (<see cref="ISettingsStore"/>), so this reads and writes the store
/// directly rather than dispatching a command (ADR-010's commands are for
/// notes, folders and their kin).
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

    /// <summary>The edge to dock to. Read at launch; restart-only in this slice.</summary>
    public WorkspaceEdge Edge => _settings.Read(SettingKeys.WorkspaceEdge);

    /// <summary>What Escape does (parity A12). Read at launch; restart-only.</summary>
    public EscapeBehavior Escape => _settings.Read(SettingKeys.WorkspaceEscape);

    /// <summary>
    /// Whether the shown workspace hides when activation moves to another
    /// application (parity A13, "close on outside click"). Read at launch;
    /// restart-only.
    /// </summary>
    public bool HideOnDeactivation => _settings.Read(SettingKeys.WorkspaceHideOnDeactivation);

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

    private bool TryWrite(SettingKey<double> key, double value)
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
