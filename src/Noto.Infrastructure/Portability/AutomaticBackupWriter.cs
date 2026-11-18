using Noto.Core.Portability;
using Noto.Infrastructure.Storage;

namespace Noto.Infrastructure.Portability;

/// <summary>What an automatic-backup check did (F15, #15; #233).</summary>
/// <param name="Written">Whether a backup was written.</param>
/// <param name="Newest">When the newest automatic backup where it looked was taken; <see langword="null"/> when there is none.</param>
/// <param name="FellBack">Whether the chosen folder could not be used, so the backup went to the data root's backups.</param>
public sealed record AutomaticBackupCheck(bool Written, DateTimeOffset? Newest, bool FellBack);

/// <summary>
/// Takes an automatic backup into its folder when one is due, and removes the automatic backups there past the seven
/// newest (F15, #15). The folder is the data root's backups or one the user chose (#233).
/// </summary>
/// <remarks>
/// <para>
/// <b>A chosen folder that is gone or cannot be written never costs a backup.</b> Gone (a drive unplugged, a folder
/// removed), it is not made again: the check is the data root's backups', as if none were chosen. One that refuses the
/// backup has it written to the data root's backups instead. Either way the result says it fell back, for the user to
/// be told.
/// </para>
/// <para>
/// Only files at the folder's top level named as automatic backups are ever removed (<see cref="AutomaticBackups.Expired"/>):
/// nothing else in a user's folder, OneDrive's say, is touched.
/// </para>
/// </remarks>
public static class AutomaticBackupWriter
{
    /// <summary>Takes an automatic backup into <paramref name="chosenFolder"/>, or the data root's backups when it is empty, if one is due.</summary>
    /// <exception cref="PortabilityException">The backup could not be taken or written anywhere: nothing was written.</exception>
    public static AutomaticBackupCheck BackupIfDue(
        NotoDatabase database,
        NotoStoragePaths paths,
        string chosenFolder,
        BackupFrequency frequency,
        string appVersion,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(chosenFolder);
        ArgumentNullException.ThrowIfNull(time);

        string fallback = paths.BackupsDirectory;
        bool fellBack = chosenFolder.Length > 0 && !Directory.Exists(chosenFolder);
        string folder = chosenFolder.Length == 0 || fellBack ? fallback : chosenFolder;
        List<string> names;

        try
        {
            names = Names(folder, create: folder == fallback);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && folder != fallback)
        {
            (folder, fellBack) = (fallback, true);
            names = Names(folder, create: true);
        }

        DateTimeOffset now = time.GetLocalNow();
        DateTimeOffset? newest = names.Select(AutomaticBackups.TakenAt).Max();

        if (!AutomaticBackups.IsDue(newest, now, frequency))
        {
            return new AutomaticBackupCheck(false, newest, false);
        }

        string name = AutomaticBackups.FileName(now);

        try
        {
            BackupWriter.Create(database, paths.AttachmentsDirectory, Path.Join(folder, name), appVersion, time);
        }
        catch (PortabilityException) when (folder != fallback)
        {
            (folder, fellBack) = (fallback, true);
            names = Names(folder, create: true);
            BackupWriter.Create(database, paths.AttachmentsDirectory, Path.Join(folder, name), appVersion, time);
        }

        foreach (string expired in AutomaticBackups.Expired([.. names, name]))
        {
            try
            {
                File.Delete(Path.Join(folder, expired));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Kept for now (open elsewhere, say): the next backup prunes it.
            }
        }

        return new AutomaticBackupCheck(true, AutomaticBackups.TakenAt(name), fellBack);
    }

    /// <summary>The names of the files at <paramref name="folder"/>'s top level; the data root's backups is made if missing.</summary>
    private static List<string> Names(string folder, bool create)
    {
        if (create)
        {
            Directory.CreateDirectory(folder);
        }

        return [.. Directory.EnumerateFiles(folder).Select(f => Path.GetFileName(f))];
    }
}
