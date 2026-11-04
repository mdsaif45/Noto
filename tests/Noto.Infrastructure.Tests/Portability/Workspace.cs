using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Noto.Core.Folders;
using Noto.Core.Notes;
using Noto.Infrastructure.Storage;

namespace Noto.Infrastructure.Tests.Portability;

/// <summary>
/// A data root of its own in the test's temporary directory, with the real database and image store, seeded by raw
/// SQL so a backup or an import is checked against rows it did not write.
/// </summary>
internal sealed class Workspace : IDisposable
{
    public Workspace(string? root = null)
    {
        Root = root ?? Directory.CreateTempSubdirectory("noto-portability-").FullName;
        Paths = NotoStoragePaths.At(Root);
        Paths.EnsureCreated();
        Database = new NotoDatabase(Paths.DatabaseFile);
        Database.Initialize();
        Images = new AttachmentStore(Paths.AttachmentsDirectory);
    }

    public string Root { get; }

    public NotoStoragePaths Paths { get; }

    public NotoDatabase Database { get; private set; }

    public AttachmentStore Images { get; }

    public static TimeProvider Time { get; } = new FixedTime(new DateTimeOffset(2026, 11, 4, 9, 30, 0, TimeSpan.Zero));

    /// <summary>A directory beside the workspace, removed with it.</summary>
    public string Scratch(string name)
    {
        string path = Path.Join(Root, "..", Path.GetFileName(Root) + "-" + name);
        Directory.CreateDirectory(path);
        _scratch.Add(path);
        return Path.GetFullPath(path);
    }

    private readonly List<string> _scratch = [];

    public FolderId Folder(string name, double order = 1, bool pinned = false, bool collapsed = false, bool deleted = false)
    {
        var id = FolderId.New();
        Execute(
            "INSERT INTO Folders (Id, Name, SortOrder, IsPinned, IsCollapsed, CreatedAt, UpdatedAt, DeletedAt) VALUES ($id, $name, $order, $pinned, $collapsed, $now, $now, $deleted);",
            ("$id", id.Value), ("$name", name), ("$order", order), ("$pinned", pinned ? 1 : 0), ("$collapsed", collapsed ? 1 : 0),
            ("$now", "2026-10-01T08:00:00.0000000+00:00"), ("$deleted", deleted ? "2026-10-02T08:00:00.0000000+00:00" : DBNull.Value));
        return id;
    }

    public NoteId Note(FolderId folder, string content, double order = 1, bool pinned = false, bool folded = false, bool deleted = false)
    {
        var id = NoteId.New();
        Execute(
            "INSERT INTO Notes (Id, FolderId, Content, SortOrder, IsPinned, IsFolded, CreatedAt, UpdatedAt, DeletedAt) VALUES ($id, $folder, $content, $order, $pinned, $folded, $now, $now, $deleted);",
            ("$id", id.Value), ("$folder", folder.Value), ("$content", content), ("$order", order), ("$pinned", pinned ? 1 : 0), ("$folded", folded ? 1 : 0),
            ("$now", "2026-10-01T08:00:00.0000000+00:00"), ("$deleted", deleted ? "2026-10-02T08:00:00.0000000+00:00" : DBNull.Value));
        return id;
    }

    public void Setting(string key, string value) =>
        Execute("INSERT INTO Settings (Key, Value) VALUES ($k, $v) ON CONFLICT(Key) DO UPDATE SET Value = $v;", ("$k", key), ("$v", value));

    /// <summary>An image file under a Noto name, with the given bytes after a PNG signature; its reference.</summary>
    public string Image(byte fill, int length = 64)
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. Enumerable.Repeat(fill, length)];
        (_, string? reference) = Images.Keep(png, "png");
        return reference!;
    }

    /// <summary>
    /// Everything the workspace holds, as text: every folder and note row (identifiers, names, content, order, state,
    /// the bin), the settings, and each image file's name and bytes. Two workspaces with the same dump are the same.
    /// </summary>
    public string Dump()
    {
        var dump = new StringBuilder();
        using (SqliteConnection connection = Database.OpenConnection())
        {
            Rows(connection, dump, "SELECT Id, Name, SortOrder, IsPinned, IsCollapsed, ColorKey, CreatedAt, UpdatedAt, DeletedAt FROM Folders ORDER BY Id;");
            Rows(connection, dump, "SELECT Id, FolderId, Content, SortOrder, IsPinned, IsFolded, ColorKey, CreatedAt, UpdatedAt, DeletedAt FROM Notes ORDER BY Id;");
            Rows(connection, dump, "SELECT Key, Value FROM Settings ORDER BY Key;");
        }

        foreach (string file in Directory.Exists(Paths.AttachmentsDirectory) ? [.. Directory.EnumerateFiles(Paths.AttachmentsDirectory).Order(StringComparer.Ordinal)] : Array.Empty<string>())
        {
            dump.Append(Path.GetFileName(file)).Append('=').AppendLine(Convert.ToHexString(File.ReadAllBytes(file)));
        }

        return dump.ToString();
    }

    /// <summary>The notes the search index finds for <paramref name="word"/>: the index is consistent with the rows.</summary>
    public int Found(string word)
    {
        using SqliteConnection connection = Database.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM NotesFts WHERE NotesFts MATCH $q;";
        command.Parameters.AddWithValue("$q", $"\"{word}\"*");
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public long Count(string sql)
    {
        using SqliteConnection connection = Database.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    /// <summary>Closes the database as Noto does at exit, so its files can move (a restore applies at the next start).</summary>
    public void Close()
    {
        Database.Release();
        SqliteConnection.ClearAllPools();
    }

    /// <summary>Opens the database again, as the next start does.</summary>
    public void Reopen()
    {
        Database = new NotoDatabase(Paths.DatabaseFile);
        Database.Initialize();
    }

    public void Dispose()
    {
        Close();
        foreach (string path in _scratch.Append(Root))
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch (IOException)
            {
                // A file still held by the system (an indexer, a scanner) is left in the temporary folder Windows clears;
                // the test's result does not depend on it.
            }
        }
    }

    private void Execute(string sql, params (string Name, object Value)[] parameters)
    {
        using SqliteConnection connection = Database.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        command.ExecuteNonQuery();
    }

    private static void Rows(SqliteConnection connection, StringBuilder dump, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            for (int i = 0; i < reader.FieldCount; i++)
            {
                dump.Append(reader.IsDBNull(i) ? "<null>" : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture)).Append('|');
            }

            dump.AppendLine();
        }
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
