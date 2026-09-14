namespace Noto.Infrastructure.Tests.Storage;

/// <summary>
/// An isolated database file in its own temporary directory.
/// </summary>
/// <remarks>
/// Test isolation is not optional here: a test that touched
/// <c>%LOCALAPPDATA%\Noto</c> could destroy a developer's real notes. Every
/// persistence test constructs one of these and passes its path explicitly, so
/// production paths are never resolved during a test run.
/// </remarks>
public sealed class TempDatabase : IDisposable
{
    private readonly string _directory;

    public TempDatabase()
    {
        _directory = Path.Combine(Path.GetTempPath(), "noto-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        DatabasePath = Path.Combine(_directory, "test.db");
    }

    /// <summary>Full path to the database file. It does not exist until opened.</summary>
    public string DatabasePath { get; }

    public void Dispose()
    {
        // Pooled connections can still hold the file briefly after disposal,
        // which is the usual cause of a flaky cleanup on Windows.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leaked temp directory is noise, not a test failure.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
