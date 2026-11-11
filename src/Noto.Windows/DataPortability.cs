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
    /// Takes an automatic backup into the data root's backups when one is due, then removes the automatic backups past
    /// the seven newest (F15, #15). Nothing is done while another backup, restore, export or import runs: the next check
    /// takes it.
    /// </summary>
    /// <returns>Whether a backup was written.</returns>
    /// <exception cref="PortabilityException">The backup could not be taken or written: nothing was written.</exception>
    public async Task<bool> BackupIfDue()
    {
        if (Busy)
        {
            return false;
        }

        try
        {
            return await Run(() =>
            {
                string directory = paths.BackupsDirectory;
                Directory.CreateDirectory(directory);
                var names = Directory.EnumerateFiles(directory).Select(f => Path.GetFileName(f)).ToList();
                DateTimeOffset now = TimeProvider.System.GetLocalNow();

                if (!AutomaticBackups.IsDue(names.Select(AutomaticBackups.TakenAt).Max(), now))
                {
                    return false;
                }

                string name = AutomaticBackups.FileName(now);
                BackupWriter.Create(database, paths.AttachmentsDirectory, Path.Join(directory, name), AppVersion, TimeProvider.System);

                foreach (string expired in AutomaticBackups.Expired([.. names, name]))
                {
                    try
                    {
                        File.Delete(Path.Join(directory, expired));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // Kept for now (open elsewhere, say): the next backup prunes it.
                    }
                }

                return true;
            });
        }
        catch (InvalidOperationException)
        {
            // Other work began between the check and the start: the next check takes it.
            return false;
        }
    }

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

    /// <summary>Asks for a backup to restore; <see langword="null"/> when the user cancels.</summary>
    public async Task<string?> PickBackup()
    {
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
