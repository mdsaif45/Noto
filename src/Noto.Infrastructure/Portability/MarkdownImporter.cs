using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Noto.Core.Folders;
using Noto.Core.Images;
using Noto.Core.Notes;
using Noto.Core.Portability;
using Noto.Infrastructure.Storage;

namespace Noto.Infrastructure.Portability;

/// <summary>
/// Imports a folder of Markdown (ADR-020 §5), adding to the workspace and never changing what is there: every folder
/// imported is a new folder (a name already used gains <c> (2)</c>…), every note a new note, its text kept exactly.
/// The images notes refer to inside the source are copied in. All notes go in in one transaction; images copied for an
/// import that fails or is cancelled are removed again.
/// </summary>
public static class MarkdownImporter
{
    /// <summary>The largest file imported as a note, as for an image (ADR-019).</summary>
    public const long MaxNoteBytes = 25L * 1024 * 1024;

    /// <summary>The file types imported as notes.</summary>
    public static IReadOnlyList<string> NoteTypes { get; } = [".md", ".markdown", ".txt"];

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Imports the Markdown under <paramref name="source"/>.</summary>
    /// <returns>What was imported, and what was not and why.</returns>
    /// <exception cref="OperationCanceledException">Cancelled: nothing was imported.</exception>
    /// <exception cref="PortabilityException">The notes could not be saved: nothing was imported.</exception>
    public static ImportReport Import(
        NotoDatabase database,
        AttachmentStore images,
        string source,
        TimeProvider time,
        IProgress<PortabilityProgress>? progress = null,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(images);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(time);

        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
        if (!Directory.Exists(root))
        {
            throw new PortabilityException("That folder is no longer there.");
        }

        var skipped = new List<ImportIssue>();
        var imageIssues = new List<ImportIssue>();
        List<PlannedFolder> plan = Plan(root);
        int total = plan.Sum(f => f.Files.Count);
        progress?.Report(new PortabilityProgress(0, total));

        var copied = new List<string>();
        var keptBySource = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int done = 0;
        try
        {
            var folders = new List<(PlannedFolder Folder, List<(string Content, ExportNote? State)> Notes)>(plan.Count);
            foreach (PlannedFolder folder in plan)
            {
                var notes = new List<(string, ExportNote?)>(folder.Files.Count);
                foreach ((string file, ExportNote? state) in folder.Files)
                {
                    cancellation.ThrowIfCancellationRequested();
                    string relative = Path.GetRelativePath(root, file);
                    if (Text(file) is not { } content)
                    {
                        skipped.Add(new ImportIssue(relative, new FileInfo(file).Length > MaxNoteBytes ? ImportIssueKind.TooLarge : ImportIssueKind.NotText));
                    }
                    else
                    {
                        string directory = Path.GetDirectoryName(file)!;
                        notes.Add((ImageSources.Rewrite(content, s => Image(images, root, directory, s, relative, keptBySource, copied, imageIssues, reported)), state));
                    }

                    progress?.Report(new PortabilityProgress(++done, total));
                }

                if (notes.Count > 0 || folder.FromIndex)
                {
                    folders.Add((folder, notes));
                }
            }

            cancellation.ThrowIfCancellationRequested();
            (int folderCount, int noteCount) = Save(database, folders, time.GetUtcNow());
            return new ImportReport(folderCount, noteCount, copied.Count, skipped, imageIssues);
        }
        catch (Exception ex) when (ex is OperationCanceledException or SqliteException or IOException or UnauthorizedAccessException)
        {
            foreach (string path in copied.Select(images.PathOf).OfType<string>())
            {
                BackupWriter.TryDelete(path);
            }

            if (ex is OperationCanceledException)
            {
                throw;
            }

            throw new PortabilityException("The notes could not be imported.", ex);
        }
    }

    /// <summary>
    /// The folders to create and the files in each, in order: from the export's index where there is one, then every
    /// other note file by name. A note's folder is its directory relative to the source (nested ones joined with
    /// <c> / </c>, Noto's folders being flat); files at the top level go into a folder named after the source.
    /// </summary>
    private static List<PlannedFolder> Plan(string root)
    {
        string indexPath = Path.Join(root, ExportIndex.FileName);
        ExportIndex? index = File.Exists(indexPath) && new FileInfo(indexPath).Length < MaxNoteBytes ? ExportIndex.Read(File.ReadAllText(indexPath)) : null;

        var files = Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System })
            .Where(f => NoteTypes.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .Where(f => !Path.GetRelativePath(root, f).Split(Path.DirectorySeparatorChar).SkipLast(1).Contains(ImageReference.Folder, StringComparer.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var unplaced = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
        var plan = new List<PlannedFolder>();

        foreach (ExportFolder folder in index?.Folders ?? [])
        {
            string directory = Path.GetFullPath(Path.Join(root, folder.Directory));
            if (!Inside(root, directory) || Path.GetDirectoryName(directory) != root)
            {
                continue;
            }

            var planned = new PlannedFolder(FolderName.Normalise(folder.Name) is { Length: > 0 } name ? name : PortableNames.Untitled, folder.Pinned, true, []);
            foreach (ExportNote note in folder.Notes)
            {
                string file = Path.GetFullPath(Path.Join(directory, note.File));
                if (Path.GetDirectoryName(file) == directory && unplaced.Remove(file))
                {
                    planned.Files.Add((file, note));
                }
            }

            plan.Add(planned);
        }

        foreach (var group in unplaced.GroupBy(f => Path.GetRelativePath(root, Path.GetDirectoryName(f)!), StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            string name = group.Key == "." ? Path.GetFileName(root) : group.Key.Replace(Path.DirectorySeparatorChar.ToString(), " / ", StringComparison.Ordinal);
            plan.Add(new PlannedFolder(FolderName.Normalise(name) is { Length: > 0 } n ? n : PortableNames.Untitled, false, false, [.. group.Select(f => new ValueTuple<string, ExportNote?>(f, null))]));
        }

        return plan;
    }

    /// <summary>The file's text exactly (a UTF-8 byte order mark dropped), or <see langword="null"/> when it is too large or not UTF-8.</summary>
    private static string? Text(string file)
    {
        if (new FileInfo(file).Length > MaxNoteBytes)
        {
            return null;
        }

        byte[] bytes = File.ReadAllBytes(file);
        int start = bytes.AsSpan().StartsWith("\uFEFF"u8) ? 3 : 0;
        try
        {
            return StrictUtf8.GetString(bytes, start, bytes.Length - start);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    /// <summary>
    /// The reference to write for an image source: a picture inside the source, copied in (under its own name when it
    /// is a Noto name that is free or holds the same bytes); anything else is left as written, and reported when it
    /// was meant to be a local picture: once per picture, however many notes refer to it.
    /// </summary>
    private static string? Image(
        AttachmentStore images, string root, string directory, string source, string note,
        Dictionary<string, string> keptBySource, List<string> copied, List<ImportIssue> issues, HashSet<string> reported)
    {
        void Report(string picture, ImportIssueKind kind)
        {
            if (reported.Add(picture))
            {
                issues.Add(new ImportIssue(note, kind, source));
            }
        }

        // A web address or any other scheme is a link, not a file of the source's.
        if (Uri.TryCreate(source, UriKind.Absolute, out Uri? uri) && !uri.IsFile || source.StartsWith('/') || source.StartsWith('\\'))
        {
            return null;
        }

        string path;
        try
        {
            path = Path.GetFullPath(Path.Join(directory, Uri.UnescapeDataString(source).Replace('/', Path.DirectorySeparatorChar)));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            Report(source, ImportIssueKind.ImageMissing);
            return null;
        }

        if (!Inside(root, path))
        {
            Report(path, ImportIssueKind.ImageOutsideSource);
            return null;
        }

        if (keptBySource.TryGetValue(path, out string? kept))
        {
            return kept;
        }

        if (!File.Exists(path))
        {
            Report(path, ImportIssueKind.ImageMissing);
            return null;
        }

        byte[] bytes = File.ReadAllBytes(path);
        string candidate = $"{ImageReference.Folder}/{Path.GetFileName(path)}";
        if (ImageReference.TryFileName(candidate, out _))
        {
            bool existed = images.PathOf(candidate) is not null;
            if (images.KeepAs(bytes, candidate) == ImageKeeping.Kept)
            {
                if (!existed)
                {
                    copied.Add(candidate);
                }

                return keptBySource[path] = candidate;
            }
        }

        (ImageKeeping outcome, string? reference) = images.KeepCopyOf(path);
        if (outcome != ImageKeeping.Kept || reference is null)
        {
            Report(path, outcome == ImageKeeping.TooLarge ? ImportIssueKind.ImageTooLarge : ImportIssueKind.ImageNotAnImage);
            return null;
        }

        copied.Add(reference);
        return keptBySource[path] = reference;
    }

    /// <summary>
    /// Inserts the folders and notes in one transaction: new identifiers, folders after the existing ones with unused
    /// names, notes in their folder's order.
    /// </summary>
    private static (int Folders, int Notes) Save(NotoDatabase database, List<(PlannedFolder Folder, List<(string Content, ExportNote? State)> Notes)> folders, DateTimeOffset now)
    {
        using SqliteConnection connection = database.OpenConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();

        var names = new NameAllocator(Strings(connection, transaction, "SELECT Name FROM Folders WHERE DeletedAt IS NULL;"));
        double order = Number(connection, transaction, "SELECT COALESCE(MAX(SortOrder), 0) FROM Folders WHERE DeletedAt IS NULL;");
        string stamp = Timestamps.Format(now);
        int notes = 0;

        foreach (var (folder, content) in folders)
        {
            string folderId = FolderId.New().Value;
            Execute(connection, transaction,
                "INSERT INTO Folders (Id, Name, SortOrder, IsPinned, CreatedAt, UpdatedAt) VALUES ($id, $name, $order, $pinned, $now, $now);",
                ("$id", folderId), ("$name", names.Take(folder.Name)), ("$order", ++order), ("$pinned", folder.Pinned ? 1 : 0), ("$now", stamp));

            int position = 0;
            foreach ((string text, ExportNote? state) in content)
            {
                Execute(connection, transaction,
                    "INSERT INTO Notes (Id, FolderId, Content, SortOrder, IsPinned, IsFolded, CreatedAt, UpdatedAt) VALUES ($id, $folder, $content, $order, $pinned, $folded, $now, $now);",
                    ("$id", NoteId.New().Value), ("$folder", folderId), ("$content", text), ("$order", (double)++position),
                    ("$pinned", state?.Pinned == true ? 1 : 0), ("$folded", state?.Folded == true ? 1 : 0), ("$now", stamp));
                notes++;
            }
        }

        transaction.Commit();
        return (folders.Count, notes);
    }

    private static bool Inside(string root, string path) =>
        path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static List<string> Strings(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        using SqliteDataReader reader = command.ExecuteReader();
        var values = new List<string>();
        while (reader.Read())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private static double Number(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return Convert.ToDouble(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql, params (string Name, object Value)[] parameters)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        command.ExecuteNonQuery();
    }

    private sealed record PlannedFolder(string Name, bool Pinned, bool FromIndex, List<(string File, ExportNote? State)> Files);
}

/// <summary>What an import added, and what it did not and why.</summary>
public sealed record ImportReport(int Folders, int Notes, int Images, IReadOnlyList<ImportIssue> Skipped, IReadOnlyList<ImportIssue> ImageIssues);

/// <summary>A file not imported, or an image a note refers to that was not; <paramref name="Source"/> is the image's source as written.</summary>
public sealed record ImportIssue(string File, ImportIssueKind Kind, string? Source = null);

/// <summary>Why something was not imported.</summary>
public enum ImportIssueKind
{
    /// <summary>The file is not UTF-8 text.</summary>
    NotText,

    /// <summary>The file is larger than <see cref="MarkdownImporter.MaxNoteBytes"/>.</summary>
    TooLarge,

    /// <summary>The image the note refers to is not there.</summary>
    ImageMissing,

    /// <summary>The image the note refers to is outside the folder being imported.</summary>
    ImageOutsideSource,

    /// <summary>The file the note refers to is not an image Noto keeps.</summary>
    ImageNotAnImage,

    /// <summary>The image is larger than Noto keeps.</summary>
    ImageTooLarge,
}
