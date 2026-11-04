using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Noto.Core.Images;
using Noto.Core.Notes;
using Noto.Core.Portability;
using Noto.Infrastructure.Storage;

namespace Noto.Infrastructure.Portability;

/// <summary>
/// Exports notes as Markdown (ADR-020 §4): each note's source byte for byte, in a directory per folder, with the images
/// it refers to beside it so its references resolve unchanged, and an index of order, pins and folds. Written into a
/// temporary directory and renamed when complete: cancelled or failed, nothing remains.
/// </summary>
public static class MarkdownExporter
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Exports <paramref name="scope"/> into a new directory under <paramref name="parent"/>.
    /// </summary>
    /// <returns>What was written, or <see langword="null"/> when the scope holds no note: nothing is written then.</returns>
    /// <exception cref="OperationCanceledException">Cancelled: nothing was written.</exception>
    /// <exception cref="PortabilityException">The export could not be written: nothing was written.</exception>
    public static ExportSummary? Export(
        NotoDatabase database,
        string attachmentsDirectory,
        ExportScope scope,
        string parent,
        TimeProvider time,
        IProgress<PortabilityProgress>? progress = null,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(parent);
        ArgumentNullException.ThrowIfNull(time);

        List<FolderRow> folders;
        try
        {
            folders = Read(database, scope);
        }
        catch (SqliteException ex)
        {
            throw new PortabilityException("The notes could not be read.", ex);
        }

        int total = folders.Sum(f => f.Notes.Count);
        if (total == 0)
        {
            return null;
        }

        DateTimeOffset now = time.GetUtcNow();
        string name = new NameAllocator(Directory.Exists(parent) ? Directory.EnumerateFileSystemEntries(parent).Select(e => Path.GetFileName(e)) : [])
            .Take(string.Create(CultureInfo.InvariantCulture, $"Noto export {now.ToLocalTime():yyyy-MM-dd HHmm}"));
        string final = Path.Join(parent, name);
        string work = Path.Join(parent, $".{name}.partial");

        try
        {
            Directory.CreateDirectory(work);
            var directories = new NameAllocator();
            var indexed = new List<ExportFolder>(folders.Count);
            int done = 0, images = 0;

            // Each missing picture once, however many notes refer to it.
            var missing = new HashSet<string>(StringComparer.Ordinal);
            progress?.Report(new PortabilityProgress(0, total));

            foreach (FolderRow folder in folders)
            {
                string directory = directories.Take(PortableNames.Safe(folder.Name));
                string folderPath = Path.Join(work, directory);
                Directory.CreateDirectory(folderPath);

                var files = new NameAllocator();
                var notes = new List<ExportNote>(folder.Notes.Count);
                foreach (NoteRow note in folder.Notes)
                {
                    cancellation.ThrowIfCancellationRequested();
                    string file = files.Take(PortableNames.Safe(NoteTitle.From(note.Content)), ".md");
                    File.WriteAllText(Path.Join(folderPath, file), note.Content, Utf8);
                    notes.Add(new ExportNote(file, note.Pinned, note.Folded, note.ColorKey));

                    foreach (string image in ImageReference.In(note.Content).Distinct(StringComparer.Ordinal))
                    {
                        string from = Path.Join(attachmentsDirectory, image);
                        string to = Path.Join(folderPath, ImageReference.Folder, image);
                        if (File.Exists(to))
                        {
                            continue;
                        }

                        if (!File.Exists(from))
                        {
                            missing.Add(image);
                            continue;
                        }

                        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                        File.Copy(from, to);
                        images++;
                    }

                    progress?.Report(new PortabilityProgress(++done, total));
                }

                indexed.Add(new ExportFolder(directory, folder.Name, folder.Pinned, folder.ColorKey, notes));
            }

            File.WriteAllText(Path.Join(work, ExportIndex.FileName), new ExportIndex { ExportedAt = now, Folders = indexed }.ToJson(), Utf8);
            cancellation.ThrowIfCancellationRequested();
            Directory.Move(work, final);
            return new ExportSummary(final, folders.Count, total, images, missing.Count);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new PortabilityException("The notes could not be written there.", ex);
        }
        finally
        {
            BackupWriter.TryDeleteDirectory(work);
        }
    }

    /// <summary>The folders and active notes in scope, in the workspace's display order (pinned first, then order).</summary>
    private static List<FolderRow> Read(NotoDatabase database, ExportScope scope)
    {
        using SqliteConnection connection = database.OpenConnection();
        var folders = new List<FolderRow>();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = scope switch
            {
                ExportScope.Folder => "SELECT Id, Name, IsPinned, ColorKey FROM Folders WHERE DeletedAt IS NULL AND Id = $id;",
                ExportScope.Note => "SELECT f.Id, f.Name, f.IsPinned, f.ColorKey FROM Folders f JOIN Notes n ON n.FolderId = f.Id WHERE n.Id = $id AND n.DeletedAt IS NULL AND f.DeletedAt IS NULL;",
                _ => "SELECT Id, Name, IsPinned, ColorKey FROM Folders WHERE DeletedAt IS NULL ORDER BY IsPinned DESC, SortOrder ASC, Id ASC;",
            };
            command.Parameters.AddWithValue("$id", scope.Id ?? string.Empty);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                folders.Add(new FolderRow(reader.GetString(0), reader.GetString(1), reader.GetInt64(2) != 0, reader.IsDBNull(3) ? null : reader.GetString(3), []));
            }
        }

        foreach (FolderRow folder in folders)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = scope is ExportScope.Note
                ? "SELECT Content, IsPinned, IsFolded, ColorKey FROM Notes WHERE Id = $note AND DeletedAt IS NULL;"
                : "SELECT Content, IsPinned, IsFolded, ColorKey FROM Notes WHERE FolderId = $folder AND DeletedAt IS NULL ORDER BY IsPinned DESC, SortOrder ASC, Id ASC;";
            command.Parameters.AddWithValue("$folder", folder.Id);
            command.Parameters.AddWithValue("$note", scope.Id ?? string.Empty);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                folder.Notes.Add(new NoteRow(reader.GetString(0), reader.GetInt64(1) != 0, reader.GetInt64(2) != 0, reader.IsDBNull(3) ? null : reader.GetString(3)));
            }
        }

        return folders;
    }

    private sealed record FolderRow(string Id, string Name, bool Pinned, string? ColorKey, List<NoteRow> Notes);

    private sealed record NoteRow(string Content, bool Pinned, bool Folded, string? ColorKey);
}

/// <summary>What to export: everything, one folder, or one note.</summary>
public abstract record ExportScope(string? Id)
{
    /// <summary>Every folder and its notes; the recycle bin is not exported (a backup keeps it).</summary>
    public sealed record All() : ExportScope((string?)null);

    /// <summary>One folder and its notes.</summary>
    public sealed record Folder(string FolderId) : ExportScope(FolderId);

    /// <summary>One note, in a directory named for its folder.</summary>
    public sealed record Note(string NoteId) : ExportScope(NoteId);
}

/// <summary>What an export wrote: where, how many folders, notes and images, and how many images were missing.</summary>
public sealed record ExportSummary(string Directory, int Folders, int Notes, int Images, int MissingImages);
