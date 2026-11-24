namespace Noto.Core.Portability;

/// <summary>
/// Which files a drop import may move to Windows' Recycle Bin once their notes are stored (#267; parity J29), when
/// Settings › Importing › Move imported files to the Windows Recycle Bin is on. Never Noto's own Recycle Bin for notes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Recycled, never deleted.</b> The shell's recycle silently deletes outright where a drive has no Recycle Bin: a
/// network share, and many removable drives. So a file is recycled only on a local fixed drive, under a drive letter;
/// anything else, a read-only file, or one that cannot be read, stays where it is and is counted as kept.
/// </para>
/// <para>
/// Only files the import read and stored as notes are offered: never a folder, a file skipped, or the copy Noto makes of
/// a virtual file (a browser's picture), which is Noto's own and removed after the drop anyway.
/// </para>
/// </remarks>
public static class ImportedFiles
{
    /// <summary>
    /// The files to offer to the Recycle Bin: those <paramref name="stored"/> (each a note's source), each once, without
    /// any under <paramref name="ownCopies"/>, the folder Noto copied a drop's virtual files into.
    /// </summary>
    public static IReadOnlyList<string> ToRecycle(IReadOnlyList<string> stored, string? ownCopies)
    {
        ArgumentNullException.ThrowIfNull(stored);

        string? copies = ownCopies is null ? null : Path.TrimEndingDirectorySeparator(ownCopies) + Path.DirectorySeparatorChar;
        return [.. stored.Where(f => copies is null || !f.StartsWith(copies, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Whether <paramref name="path"/> may be moved to the Recycle Bin, given what is known of it: its attributes
    /// (<see langword="null"/> when it cannot be read) and its drive's type (<see langword="null"/> when unknown).
    /// </summary>
    /// <returns>
    /// <see langword="true"/> only for a file, not read-only, at a drive-letter path (never a network share or a device
    /// path) on a <see cref="DriveType.Fixed"/> drive.
    /// </returns>
    public static bool MayRecycle(string path, FileAttributes? attributes, DriveType? drive)
    {
        ArgumentNullException.ThrowIfNull(path);

        return OnDriveLetter(path)
            && drive == DriveType.Fixed
            && attributes is { } a
            && (a & (FileAttributes.Directory | FileAttributes.ReadOnly)) == 0;
    }

    /// <summary>A full path whose root is a drive letter, <c>C:\</c>: not <c>\\server\share</c>, <c>\\?\</c> or <c>\\.\</c>.</summary>
    private static bool OnDriveLetter(string path) =>
        Path.IsPathFullyQualified(path)
        && Path.GetPathRoot(path) is { Length: 3 } root
        && char.IsAsciiLetter(root[0])
        && root[1] == ':'
        && root[2] == Path.DirectorySeparatorChar;
}
