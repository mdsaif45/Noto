using Microsoft.Data.Sqlite;
using Noto.Core.Settings;
using Noto.Core.Storage;
using Noto.Infrastructure.Storage;
using Noto.Infrastructure.Tests.Storage;

namespace Noto.Infrastructure.Tests.Settings;

/// <summary>
/// A real SQLite database plus a settings store, shared by the #9 tests.
/// </summary>
/// <remarks>
/// <para>
/// Real SQLite rather than a fake, matching every other persistence test
/// (ADR-003). What is worth proving here — that a value survives a restart,
/// that a corrupt row is left on disk rather than repaired, that an unknown
/// key is still there afterwards — is exactly what a fake cannot prove.
/// </para>
/// <para>
/// Built on <see cref="TempDatabase"/>, so the developer's real database at
/// <c>%LOCALAPPDATA%\Noto</c> is never opened. No parallel test
/// infrastructure is introduced.
/// </para>
/// </remarks>
public sealed class SettingsTestContext : IDisposable
{
    private readonly TempDatabase _temp = new();

    public SettingsTestContext()
    {
        DatabasePath = _temp.DatabasePath;
        Database = new NotoDatabase(DatabasePath);
        Database.Initialize();
    }

    /// <summary>The database file, so a test can reopen it independently.</summary>
    public string DatabasePath { get; }

    public NotoDatabase Database { get; }

    public RecordingStorageLog Log { get; } = new();

    /// <summary>A store that has already loaded.</summary>
    /// <param name="keys">
    /// Defaults to the production registry plus the test-only keys, so one
    /// helper serves both. A test that cares which set was loaded passes it.
    /// </param>
    public SqliteSettingsStore LoadedStore(IReadOnlyList<SettingKey>? keys = null)
    {
        var store = new SqliteSettingsStore(Database, Log);
        store.Load(keys ?? TestSettingKeys.AllIncludingProduction);
        return store;
    }

    /// <summary>
    /// Writes a row directly, bypassing the store.
    /// </summary>
    /// <remarks>
    /// Raw SQL on purpose: a fallback test has to plant text the store would
    /// never write — <c>"not-a-number"</c> for an int, a value outside a
    /// key's range. Seeding through <c>Write</c> would validate it away and
    /// the test would prove nothing.
    /// </remarks>
    public void SeedRaw(string key, string value)
    {
        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();

        command.CommandText =
            """
            INSERT INTO Settings (Key, Value) VALUES ($key, $value)
            ON CONFLICT (Key) DO UPDATE SET Value = excluded.Value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    /// <summary>The raw stored text, or null when the key has no row.</summary>
    public string? RawValueOf(string key)
    {
        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();

        command.CommandText = "SELECT Value FROM Settings WHERE Key = $key;";
        command.Parameters.AddWithValue("$key", key);

        return command.ExecuteScalar() as string;
    }

    /// <summary>How many rows the table holds.</summary>
    public long RowCount()
    {
        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();

        command.CommandText = "SELECT COUNT(*) FROM Settings;";

        return (long)command.ExecuteScalar()!;
    }

    /// <summary>Drops the table, so the next read fails rather than returns nothing.</summary>
    /// <remarks>
    /// A missing table is the cleanest way to make <c>SELECT</c> throw without
    /// corrupting the file — the distinction the store draws between "read
    /// failed" and "no rows" is otherwise untestable.
    /// </remarks>
    public void DropSettingsTable()
    {
        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();

        command.CommandText = "DROP TABLE Settings;";
        command.ExecuteNonQuery();

        SqliteConnection.ClearPool(connection);
    }

    public void Dispose() => _temp.Dispose();
}

/// <summary>Captures what the store reported, so tests can assert on it.</summary>
public sealed class RecordingStorageLog : IStorageLog
{
    public List<(string Key, SettingFallbackReason Reason, string ExpectedType)> Fallbacks { get; } = [];

    public List<Exception> Unreadable { get; } = [];

    public void SafetyCopyCreated(int fromVersion, string path)
    {
    }

    public void MigrationStarting(int fromVersion, int toVersion, int pendingCount)
    {
    }

    public void MigrationApplied(int version, string description)
    {
    }

    public void MigrationCompleted(int version)
    {
    }

    public void MigrationFailed(int version, Exception exception)
    {
    }

    public void SettingFellBackToDefault(string key, SettingFallbackReason reason, string expectedType) =>
        Fallbacks.Add((key, reason, expectedType));

    public void SettingsUnreadable(Exception exception) => Unreadable.Add(exception);
}

/// <summary>Keys used only by the tests, to exercise types #9 registers none of.</summary>
/// <remarks>
/// The contract supports five value types but the two production keys are a
/// <c>bool</c> and a <c>string</c>. Declaring test-only keys proves the other
/// three round-trip without inventing production settings that #16 has not
/// asked for — which is the failure mode this slice is specifically avoiding.
/// </remarks>
public static class TestSettingKeys
{
    public static readonly SettingKey<bool> Flag = new("test.flag", false);

    public static readonly SettingKey<int> Count = new("test.count", 7, static v => v is >= 0 and <= 100);

    public static readonly SettingKey<double> Ratio = new("test.ratio", 1.5, static v => v is > 0 and < 1000);

    public static readonly SettingKey<string> Label = new("test.label", "default-label");

    public static readonly SettingKey<TestEdge> Edge = new("test.edge", TestEdge.Right);

    /// <summary>The production keys plus these, for the shared fixture.</summary>
    public static IReadOnlyList<SettingKey> AllIncludingProduction { get; } =
    [
        .. SettingKeys.All,
        Flag,
        Count,
        Ratio,
        Label,
        Edge,
    ];
}

/// <summary>A two-valued enum, standing in for any enum-typed setting.</summary>
public enum TestEdge
{
    Left,
    Right,
}
