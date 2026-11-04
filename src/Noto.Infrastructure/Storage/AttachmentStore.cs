using Noto.Core.Images;
using Noto.Core.Storage;

namespace Noto.Infrastructure.Storage;

/// <summary>
/// The image files beside the database (ADR-019 §4): each kept under a name Noto made, checked to be the image its type
/// says, and removed only when no note, binned or not, refers to it and it is older than the sweep's grace.
/// </summary>
/// <param name="directory">The data root's attachments folder (<see cref="NotoStoragePaths.AttachmentsDirectory"/>).</param>
public sealed class AttachmentStore(string directory) : IImageStore
{
    /// <summary>How long an unreferenced file is kept: undo, the bin and a crash mid-edit all keep their image.</summary>
    public static readonly TimeSpan SweepGrace = TimeSpan.FromDays(7);

    private readonly string _directory = Path.IsPathFullyQualified(directory)
        ? directory
        : throw new ArgumentException("The attachments folder is a full path.", nameof(directory));

    /// <summary>Keeps <paramref name="content"/> as an image of <paramref name="type"/>; the reference to write in the note.</summary>
    public (ImageKeeping Outcome, string? Reference) Keep(byte[] content, string type)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(type);

        if (ImageReference.TypeOf("x." + type) is not { } normal || !LooksLike(content, normal))
        {
            return (ImageKeeping.NotAnImage, null);
        }

        if (content.LongLength > ImageReference.MaxBytes)
        {
            return (ImageKeeping.TooLarge, null);
        }

        string reference = ImageReference.New(normal);
        try
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllBytes(PathFor(reference), content);
            return (ImageKeeping.Kept, reference);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (ImageKeeping.Failed, null);
        }
    }

    /// <summary>
    /// Keeps <paramref name="content"/> under <paramref name="reference"/>, a name Noto made, when that name is free
    /// (ADR-020 §5: an exported note's images come back under their own names, so its text needs no rewriting). The same
    /// bytes already there are kept as they are; a name that holds other bytes is refused.
    /// </summary>
    /// <returns><see cref="ImageKeeping.Kept"/>, or why not; <see cref="ImageKeeping.Failed"/> when the name is taken.</returns>
    public ImageKeeping KeepAs(byte[] content, string reference)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!ImageReference.TryFileName(reference, out string name) || ImageReference.TypeOf(name) is not { } type || !LooksLike(content, type))
        {
            return ImageKeeping.NotAnImage;
        }

        if (content.LongLength > ImageReference.MaxBytes)
        {
            return ImageKeeping.TooLarge;
        }

        string path = Path.Join(_directory, name);
        try
        {
            Directory.CreateDirectory(_directory);
            if (File.Exists(path))
            {
                return File.ReadAllBytes(path).AsSpan().SequenceEqual(content) ? ImageKeeping.Kept : ImageKeeping.Failed;
            }

            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            file.Write(content);
            return ImageKeeping.Kept;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ImageKeeping.Failed;
        }
    }

    /// <summary>Keeps a copy of the image file at <paramref name="path"/>; the reference to write in the note.</summary>
    public (ImageKeeping Outcome, string? Reference) KeepCopyOf(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (ImageReference.TypeOf(path) is not { } type)
        {
            return (ImageKeeping.NotAnImage, null);
        }

        try
        {
            var file = new FileInfo(path);
            if (file.Length > ImageReference.MaxBytes)
            {
                return (ImageKeeping.TooLarge, null);
            }

            return Keep(File.ReadAllBytes(path), type);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (ImageKeeping.Failed, null);
        }
    }

    /// <summary>The file of a reference Noto made, when it is there; otherwise <see langword="null"/>.</summary>
    public string? PathOf(string? reference)
    {
        if (!ImageReference.TryFileName(reference, out string name))
        {
            return null;
        }

        string path = Path.Join(_directory, name);
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Removes the files Noto made that no note refers to and that were last written before
    /// <paramref name="now"/> less <see cref="SweepGrace"/>. Files Noto did not name are never touched.
    /// </summary>
    /// <param name="referenced">The file names every note refers to, binned notes included.</param>
    /// <param name="now">The current time.</param>
    /// <returns>How many files were removed.</returns>
    public int Sweep(IReadOnlySet<string> referenced, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(referenced);
        if (!Directory.Exists(_directory))
        {
            return 0;
        }

        int removed = 0;
        foreach (string path in Directory.EnumerateFiles(_directory))
        {
            string name = Path.GetFileName(path);
            if (!ImageReference.TryFileName(ImageReference.Folder + "/" + name, out _) || referenced.Contains(name)
                || File.GetLastWriteTimeUtc(path) > (now - SweepGrace).UtcDateTime)
            {
                continue;
            }

            try
            {
                File.Delete(path);
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // In use or locked: the next sweep tries again.
            }
        }

        return removed;
    }

    /// <summary>
    /// The image file names every note refers to, binned notes included: what <see cref="Sweep"/> keeps. One read of
    /// the notes that mention the folder at all.
    /// </summary>
    public static HashSet<string> ReferencedBy(NotoDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        var names = new HashSet<string>(StringComparer.Ordinal);

        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Content FROM Notes WHERE instr(Content, '](attachments/') > 0;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            names.UnionWith(ImageReference.In(reader.GetString(0)));
        }

        return names;
    }

    /// <summary>
    /// <see cref="Sweep"/> over what every note refers to, as one call that never throws: a database or a folder that
    /// cannot be read leaves every file where it is, and the next start tries again.
    /// </summary>
    /// <returns>How many files were removed; 0 when the sweep could not run.</returns>
    public int SweepUnreferenced(NotoDatabase database, DateTimeOffset now)
    {
        try
        {
            return Sweep(ReferencedBy(database), now);
        }
        catch (Exception ex) when (ex is StorageException or Microsoft.Data.Sqlite.SqliteException or IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private string PathFor(string reference) =>
        ImageReference.TryFileName(reference, out string name) ? Path.Join(_directory, name) : throw new ArgumentException("Not a reference Noto made.", nameof(reference));

    /// <summary>The file's first bytes are its type's signature: a renamed file is not taken for an image.</summary>
    private static bool LooksLike(byte[] content, string type) => type switch
    {
        "png" => Starts(content, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A),
        "jpg" or "jpeg" => Starts(content, 0xFF, 0xD8, 0xFF),
        "gif" => Starts(content, 0x47, 0x49, 0x46, 0x38),
        "bmp" => Starts(content, 0x42, 0x4D),
        "webp" => Starts(content, 0x52, 0x49, 0x46, 0x46) && content.Length >= 12 && content[8] == 0x57 && content[9] == 0x45 && content[10] == 0x42 && content[11] == 0x50,
        _ => false,
    };

    private static bool Starts(byte[] content, params byte[] signature) =>
        content.Length >= signature.Length && content.AsSpan(0, signature.Length).SequenceEqual(signature);
}
