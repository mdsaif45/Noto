using System.Globalization;
using System.Reflection;
using Noto.Core.Portability;
using Noto.Infrastructure.Portability;
using Noto.Infrastructure.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Noto;

/// <summary>
/// Backup, restore, export and import for the window and Settings (ADR-020 §6): Windows' own pickers, the work off the
/// UI thread with progress back on it, and the restart a restore needs. One piece of work at a time.
/// </summary>
/// <param name="paths">The data root.</param>
/// <param name="database">The open database.</param>
/// <param name="images">The image store beside it.</param>
/// <param name="window">The window the pickers belong to.</param>
/// <param name="saveOpenNote">Saves the note being edited, so a backup or an export holds what is on screen.</param>
/// <param name="restart">Ends this process and starts Noto again, which applies a committed restore before anything opens.</param>
internal sealed class DataPortability(
    NotoStoragePaths paths,
    NotoDatabase database,
    AttachmentStore images,
    Func<nint> window,
    Action saveOpenNote,
    Action restart)
{
    private static readonly string AppVersion =
        typeof(DataPortability).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    private int _busy;

    /// <summary>Whether a piece of work is running; another waits for it to end.</summary>
    public bool Busy => Volatile.Read(ref _busy) != 0;

    /// <summary>Writes a backup to <paramref name="file"/>.</summary>
    public async Task<BackupCounts> Backup(string file, IProgress<PortabilityProgress>? progress, CancellationToken cancellation)
    {
        saveOpenNote();
        try
        {
            return await Run(() => BackupWriter.Create(database, paths.AttachmentsDirectory, file, AppVersion, TimeProvider.System, progress, cancellation));
        }
        catch (Exception ex) when (ex is OperationCanceledException or PortabilityException)
        {
            RemoveEmpty(file);
            throw;
        }
    }

    /// <summary>
    /// When the newest automatic backup was taken, once a check has read the backups (#199); <see langword="null"/>
    /// before, or when there is none.
    /// </summary>
    public DateTimeOffset? NewestAutomaticBackup { get; private set; }

    /// <summary>The backup folder setting <see cref="NewestAutomaticBackup"/> was read for (#233): another folder is read again.</summary>
    private string? _newestFor;

    /// <summary>
    /// The chosen backup folder the last automatic backup could not go to, so it went to the data root's backups
    /// (#233); <see langword="null"/> once one has gone where it should. Settings says so while it is still the one chosen.
    /// </summary>
    public string? UnavailableBackupFolder { get; private set; }

    /// <summary>
    /// Takes an automatic backup into <paramref name="folder"/> (the stored backup folder: empty for the data root's
    /// backups, #233) when one is due at <paramref name="frequency"/>, then removes the automatic backups there past the
    /// seven newest (F15, #15). A chosen folder that is gone or cannot be written falls back to the data root's backups
    /// (<see cref="AutomaticBackupWriter"/>). Nothing is done while another backup, restore, export or import runs: the
    /// next check takes it. Once a folder's backups have been read, a check with nothing due is a comparison with the
    /// clock and reads nothing (#199); a changed folder is read again, so a folder with none has one due.
    /// </summary>
    /// <returns>Whether a backup was written.</returns>
    /// <exception cref="PortabilityException">The backup could not be taken or written: nothing was written.</exception>
    public async Task<bool> BackupIfDue(BackupFrequency frequency, string folder)
    {
        if (_newestFor == folder && NewestAutomaticBackup is { } known && !AutomaticBackups.IsDue(known, TimeProvider.System.GetLocalNow(), frequency))
        {
            return false;
        }

        if (Busy)
        {
            return false;
        }

        try
        {
            AutomaticBackupCheck check = await Run(() => AutomaticBackupWriter.BackupIfDue(database, paths, folder, frequency, AppVersion, TimeProvider.System));
            (NewestAutomaticBackup, _newestFor) = (check.Newest, folder);
            if (check.Written)
            {
                UnavailableBackupFolder = check.FellBack ? folder : null;
            }

            return check.Written;
        }
        catch (InvalidOperationException)
        {
            // Other work began between the check and the start: the next check takes it.
            return false;
        }
    }

    /// <summary>
    /// What to store for a folder the user picked as the backup folder (#233); <see langword="null"/> when it is inside
    /// the data root, other than its own backups (which is the default).
    /// </summary>
    public string? BackupFolderFor(string picked) => AutomaticBackups.FolderFor(picked, paths.Root, paths.BackupsDirectory);

    /// <summary>
    /// Windows' save dialog creates the file it returns (measured: a cancelled backup left an empty one). A backup that
    /// did not finish removes it, and only while it is empty: a file the user chose to replace is never deleted.
    /// </summary>
    private static void RemoveEmpty(string file)
    {
        try
        {
            if (new FileInfo(file) is { Exists: true, Length: 0 })
            {
                File.Delete(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An empty file is all that is left; nothing of the backup is in it.
        }
    }

    /// <summary>Checks a backup completely, reading it into the staging directory; nothing is replaced yet.</summary>
    public Task<(BackupManifest? Manifest, BackupProblem? Problem)> Check(string file, IProgress<PortabilityProgress>? progress, CancellationToken cancellation) =>
        Run(() => PendingRestore.Stage(paths, file, progress, cancellation));

    /// <summary>Drops a checked backup the user decided not to restore.</summary>
    public void Discard() => PendingRestore.Discard(paths);

    /// <summary>
    /// Restores a checked backup: what is here now is backed up first (into the data root's backups), then the restore
    /// is committed and Noto restarts to apply it before anything opens.
    /// </summary>
    public async Task RestoreAndRestart(BackupManifest manifest, IProgress<PortabilityProgress>? progress, CancellationToken cancellation)
    {
        saveOpenNote();
        string safety = Path.Join(paths.BackupsDirectory, string.Create(CultureInfo.InvariantCulture, $"before-restore-{DateTimeOffset.Now:yyyy-MM-dd HHmmss}{BackupManifest.Extension}"));
        await Run(() =>
        {
            Directory.CreateDirectory(paths.BackupsDirectory);
            BackupWriter.Create(database, paths.AttachmentsDirectory, safety, AppVersion, TimeProvider.System, progress, cancellation);
            PendingRestore.Commit(paths, manifest);
            return 0;
        });
        restart();
    }

    /// <summary>Exports <paramref name="scope"/> into a new directory under <paramref name="parent"/>; <see langword="null"/> when there is nothing to export.</summary>
    public Task<ExportSummary?> Export(ExportScope scope, string parent, IProgress<PortabilityProgress>? progress, CancellationToken cancellation)
    {
        saveOpenNote();
        return Run(() => MarkdownExporter.Export(database, paths.AttachmentsDirectory, scope, parent, TimeProvider.System, progress, cancellation));
    }

    /// <summary>Imports the Markdown under <paramref name="folder"/>.</summary>
    public Task<ImportReport> Import(string folder, IProgress<PortabilityProgress>? progress, CancellationToken cancellation) =>
        Run(() => MarkdownImporter.Import(database, images, folder, TimeProvider.System, progress, cancellation));

    /// <summary>Imports files dropped on <paramref name="folder"/> (F9, #152): each a new note, first in it.</summary>
    public Task<FilesImport> ImportFiles(IReadOnlyList<string> files, Noto.Core.Folders.FolderId folder) =>
        Run(() => MarkdownImporter.ImportFiles(database, images, files, folder, TimeProvider.System));

    /// <summary>Asks where to save a backup; <see langword="null"/> when the user cancels.</summary>
    public async Task<string?> PickBackupDestination()
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = string.Create(CultureInfo.InvariantCulture, $"Noto backup {DateTimeOffset.Now:yyyy-MM-dd HHmm}"),
        };
        picker.FileTypeChoices.Add("Noto backup", [BackupManifest.Extension]);
        InitializeWithWindow.Initialize(picker, window());
        return (await picker.PickSaveFileAsync())?.Path;
    }

    /// <summary>
    /// Asks for a backup to restore; <see langword="null"/> when the user cancels. While a chosen backup folder is there
    /// (#233), it opens in it, where the automatic backups are: Windows App SDK's picker, since Windows' own can only
    /// start in a known location. Otherwise Windows' own, in Documents, as before.
    /// </summary>
    public async Task<string?> PickBackup(string backupFolder)
    {
        if (backupFolder.Length > 0 && Directory.Exists(backupFolder))
        {
            var inFolder = new Microsoft.Windows.Storage.Pickers.FileOpenPicker(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(window()))
            {
                SuggestedFolder = backupFolder,
            };
            inFolder.FileTypeFilter.Add(BackupManifest.Extension);
            return (await inFolder.PickSingleFileAsync())?.Path;
        }

        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(BackupManifest.Extension);
        InitializeWithWindow.Initialize(picker, window());
        return (await picker.PickSingleFileAsync())?.Path;
    }

    /// <summary>Asks for a folder, to export into or import from; <see langword="null"/> when the user cancels.</summary>
    public async Task<string?> PickFolder()
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, window());
        return (await picker.PickSingleFolderAsync())?.Path;
    }

    /// <summary>Runs <paramref name="work"/> off the UI thread, one at a time; the caller awaits it on the UI thread.</summary>
    private async Task<T> Run<T>(Func<T> work)
    {
        if (Interlocked.Exchange(ref _busy, 1) != 0)
        {
            throw new InvalidOperationException("Another backup, restore, export or import is running.");
        }

        try
        {
            return await Task.Run(work);
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }

    /// <summary>What a backup holds, as a reader is told it.</summary>
    public static string Describe(BackupCounts counts) =>
        string.Create(CultureInfo.CurrentCulture, $"{Count(counts.Folders, "folder")}, {Count(counts.Notes, "note")} and {Count(counts.Images, "picture")}");

    /// <summary>Why a backup cannot be restored, as a reader is told it. Nothing was changed in any case.</summary>
    public static string Explain(BackupProblem problem) => problem switch
    {
        BackupProblem.NotABackup => "That file isn't a Noto backup.",
        BackupProblem.Corrupted => "That backup is damaged or was changed after it was made, so it can't be restored.",
        BackupProblem.Incomplete => "That backup is incomplete, so it can't be restored.",
        BackupProblem.NewerFormat or BackupProblem.NewerSchema => "That backup was made by a newer version of Noto. Update Noto to restore it.",
        _ => "That backup couldn't be read.",
    };

    public static string Count(int n, string noun) =>
        string.Create(CultureInfo.CurrentCulture, $"{n} {noun}{(n == 1 ? string.Empty : "s")}");
}
