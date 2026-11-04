using Noto.Core.Folders;
using Noto.Core.Notes;
using Noto.Core.Settings;
using Noto.Core.Storage;
using Noto.Core.Workspace;

namespace Noto.UseCases.Workspace;

/// <summary>
/// Whether a workspace opens with its welcome, and recording how the welcome ended (ADR-021).
/// </summary>
/// <remarks>
/// <para>
/// <b>Only a genuinely new workspace.</b> The welcome is owed only when startup has just created the database, and it
/// is shown only while it is still owed and the workspace holds nothing: no folder and no note, in the Recycle Bin or
/// out of it. An existing workspace, one upgraded from a Noto without a welcome, and one restored from a backup are
/// never shown it.
/// </para>
/// <para>
/// <b>Owed until it ends.</b> Only finishing or skipping records an end. A start that stops before either (Noto ended,
/// or crashed, while the welcome was open) leaves it owed, and the next start shows it again. A workspace still owed it
/// that has come to hold data is no longer new: it owes nothing from then on, so it never reads its data at start again,
/// and emptying it later does not bring the welcome back.
/// </para>
/// <para>
/// <b>Never in the way of starting.</b> A setting that cannot be written is not an error here: the decision for this
/// start stands, and the worst outcome is a welcome shown once more, or not at all. Settings themselves never stop
/// startup (#9).
/// </para>
/// </remarks>
public sealed class Onboarding(ISettingsStore settings)
{
    private readonly ISettingsStore _settings = settings ?? throw new ArgumentNullException(nameof(settings));

    /// <summary>Where this workspace stands with its welcome.</summary>
    public WelcomeState State => _settings.Read(SettingKeys.OnboardingWelcome);

    /// <summary>
    /// Settles the welcome for this start and says whether to open with it. Call once, after settings are loaded.
    /// </summary>
    /// <param name="created">Startup created the database just now: the workspace is new.</param>
    /// <param name="restored">A backup replaced the workspace just now: it is an existing one, never new.</param>
    /// <param name="holdsData">Whether the workspace holds any folder or note. Asked only when the welcome is owed.</param>
    public bool ShowAtStart(bool created, bool restored, Func<bool> holdsData)
    {
        ArgumentNullException.ThrowIfNull(holdsData);

        if (restored)
        {
            // A backup taken while its own welcome was open carries Due; the restored workspace owes nothing.
            if (State == WelcomeState.Due)
            {
                _ = TryWrite(WelcomeState.None);
            }

            return false;
        }

        if (created)
        {
            _ = TryWrite(WelcomeState.Due);
        }

        if (!created && State != WelcomeState.Due)
        {
            return false;
        }

        switch (Holds(holdsData))
        {
            case false:
                return true;

            case true:
                // The user's own notes arrived before the welcome ended (an import while it was open, then a crash): the
                // workspace is no longer new, and is not owed the welcome again, however empty it later becomes.
                _ = TryWrite(WelcomeState.None);
                return false;

            default:
                // It could not say: not shown now, and still owed, so the next start decides.
                return false;
        }
    }

    /// <summary>
    /// Whether the workspace holds anything of the user's: a folder or a note, in the Recycle Bin or out of it. A note
    /// out of the bin is always in a folder out of it (schema V3), so three reads cover every row.
    /// </summary>
    public static bool HoldsAnything(IFolderRepository folders, INoteRepository notes)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(notes);

        return folders.ListActive().Count > 0 || folders.ListDeleted().Count > 0 || notes.ListDeleted().Count > 0;
    }

    /// <summary>Records that the welcome was finished.</summary>
    /// <returns>Whether it was stored.</returns>
    public bool Complete() => TryWrite(WelcomeState.Completed);

    /// <summary>Records that the welcome was skipped, so it does not open again.</summary>
    /// <returns>Whether it was stored.</returns>
    public bool Skip() => TryWrite(WelcomeState.Skipped);

    // Whether the workspace holds anything, or null when it cannot say: it may hold the user's notes, so it is not shown
    // the welcome, and nothing is settled on a failed read.
    private static bool? Holds(Func<bool> holdsData)
    {
        try
        {
            return holdsData();
        }
        catch (StorageException)
        {
            return null;
        }
    }

    private bool TryWrite(WelcomeState state)
    {
        try
        {
            return _settings.Write(SettingKeys.OnboardingWelcome, state);
        }
        catch (StorageException)
        {
            return false;
        }
    }
}
