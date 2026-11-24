using Noto.Core.Portability;
using static Noto.Platform.Windows.NativeMethods;

namespace Noto.Platform.Windows;

/// <summary>
/// Moves files to Windows' Recycle Bin (#267; parity J29), where the user can restore them: the shell's own delete with
/// <c>FOF_ALLOWUNDO</c>, silent, one file at a time so each is counted. Never a delete outright.
/// </summary>
/// <remarks>
/// <para>
/// <b>Where there is no Recycle Bin, the shell deletes outright, silently.</b> A network share has none, and nor do many
/// removable drives. So each file is checked first, by <see cref="ImportedFiles.MayRecycle"/>, on its attributes and its
/// drive's type: only a file, not read-only, on a local fixed drive is offered to the shell. As a second guard the shell
/// is asked for its warning when it would delete outright anyway (a file too large for the bin, a bin turned off): the
/// user's No keeps the file.
/// </para>
/// <para>
/// A file the shell refuses (in use, say), or that is still there afterwards, is kept. A path is never written to a
/// trace or a log.
/// </para>
/// </remarks>
public static class ShellRecycleBin
{
    /// <summary>
    /// Moves each of <paramref name="files"/> that may go to Windows' Recycle Bin, the shell's warning (if any) owned by
    /// <paramref name="owner"/>. Called on the UI thread, which the shell expects.
    /// </summary>
    /// <returns>How many were moved, and how many stayed where they were.</returns>
    public static (int Recycled, int Kept) Recycle(IReadOnlyList<string> files, WindowHandle? owner)
    {
        ArgumentNullException.ThrowIfNull(files);

        int recycled = 0;
        foreach (string file in files)
        {
            recycled += ImportedFiles.MayRecycle(file, AttributesOf(file), DriveOf(file)) && ToBin(file, owner) ? 1 : 0;
        }

        return (recycled, files.Count - recycled);
    }

    /// <summary>The shell's recycle of one file; whether it has gone.</summary>
    private static unsafe bool ToBin(string file, WindowHandle? owner)
    {
        // pFrom is a list of paths, each ended by a null, the list by a second.
        fixed (char* from = file + "\0\0")
        {
            var operation = new SHFILEOPSTRUCTW
            {
                hwnd = owner?.Hwnd ?? 0,
                wFunc = FO_DELETE,
                pFrom = from,
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI | FOF_WANTNUKEWARNING,
            };

            return SHFileOperation(&operation) == 0 && operation.fAnyOperationsAborted == 0 && !Path.Exists(file);
        }
    }

    private static FileAttributes? AttributesOf(string file)
    {
        try
        {
            return File.GetAttributes(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>The type of the drive <paramref name="file"/> is on: <see langword="null"/> for a network share's path, or unknown.</summary>
    private static DriveType? DriveOf(string file)
    {
        try
        {
            return Path.GetPathRoot(file) is { Length: > 0 } root ? new DriveInfo(root).DriveType : null;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
