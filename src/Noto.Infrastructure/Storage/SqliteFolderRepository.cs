using Microsoft.Data.Sqlite;
using Noto.Core.Folders;
using Noto.Core.Storage;

namespace Noto.Infrastructure.Storage;

/// <summary>
/// The SQLite implementation of <see cref="IFolderRepository"/>.
/// </summary>
/// <remarks>
/// Read-only in Slice 3: it exists because <c>ListDeletedFolders</c> has to
/// return folders, and contract §11 Q7 keeps notes and folders separate. The
/// folder commands are Slice 4.
/// </remarks>
public sealed class SqliteFolderRepository(NotoDatabase database) : IFolderRepository
{
    private readonly NotoDatabase _database = database
        ?? throw new ArgumentNullException(nameof(database));

    public IReadOnlyList<Folder> ListDeleted()
    {
        try
        {
            using var connection = _database.OpenConnection();
            using var command = connection.CreateCommand();

            // The I2 inversion for folders, mirroring the note bin.
            command.CommandText =
                """
                SELECT Id, Name, ColorKey, IsPinned, IsCollapsed,
                       SortOrder, CreatedAt, UpdatedAt, DeletedAt
                FROM Folders
                WHERE DeletedAt IS NOT NULL
                ORDER BY DeletedAt DESC, Id ASC;
                """;

            var folders = new List<Folder>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                folders.Add(new Folder
                {
                    Id = FolderId.From(reader.GetString(0)),
                    Name = reader.GetString(1),
                    ColorKey = reader.IsDBNull(2) ? null : reader.GetString(2),
                    IsPinned = reader.GetInt64(3) != 0,
                    IsCollapsed = reader.GetInt64(4) != 0,
                    SortOrder = reader.GetDouble(5),
                    CreatedAt = Parse(reader.GetString(6)),
                    UpdatedAt = Parse(reader.GetString(7)),
                    DeletedAt = reader.IsDBNull(8) ? null : Parse(reader.GetString(8)),
                });
            }

            return folders;
        }
        catch (SqliteException ex)
        {
            throw new StorageException(
                StorageFailure.Unknown,
                "Could not read the folder recycle bin.",
                ex);
        }
    }

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(
            value,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind);
}
