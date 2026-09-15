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
        // Built by appending one known-relative segment at a time onto an
        // absolute root, so no call can discard what came before it.
        var root = new DirectoryInfo(Path.GetTempPath());
        var suite = root.CreateSubdirectory("noto-tests");
        var isolated = suite.CreateSubdirectory(Guid.NewGuid().ToString("N"));

        _directory = isolated.FullName;
        DatabasePath = Path.Join(_directory, "test.db");
    }

    /// <summary>Full path to the database file. It does not exist until opened.</summary>
    public string DatabasePath { get; }

    public void Dispose()
    {
        ReleaseOwnPools();

        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leaked temp directory is noise, not a test failure — the OS
            // reclaims it. Reported rather than swallowed silently, so a
            // systematic cleanup problem is visible instead of invisible.
            Console.Error.WriteLine(
                $"TempDatabase cleanup left '{_directory}' behind: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Releases the SQLite connection pools for the databases in this
    /// fixture's own directory — and no others.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This deliberately does <b>not</b> call <c>ClearAllPools()</c>. That API
    /// is process-global: it disposes every pooled connection in the process,
    /// including one another test class is mid-query on, whose next call then
    /// throws <see cref="ObjectDisposedException"/> from deep inside the native
    /// handle. xUnit runs test classes in parallel, so one fixture's teardown
    /// was intermittently killing another fixture's live connection —
    /// reproduced at roughly one failure in three full-assembly runs, landing
    /// on a different test each time.
    /// </para>
    /// <para>
    /// The pool is keyed by the <b>exact connection string</b>, so each file
    /// has to be named with the same builder settings that opened it. Two
    /// shapes exist here:
    /// </para>
    /// <list type="bullet">
    ///   <item>the note database, opened by <c>NotoDatabase</c>;</item>
    ///   <item>any <c>*-premigration-*.db</c> safety copy, opened by
    ///   <c>MigrationSafetyCopy</c> with a narrower builder.</item>
    /// </list>
    /// <para>
    /// Both live inside this fixture's own GUID-named directory, which is what
    /// makes the ownership claim checkable rather than assumed: every database
    /// in that directory was created by this fixture, and no database outside
    /// it is touched.
    /// </para>
    /// </remarks>
    private void ReleaseOwnPools()
    {
        if (!Directory.Exists(_directory))
        {
            return;
        }

        foreach (string file in Directory.GetFiles(_directory, "*.db"))
        {
            // NotoDatabase's shape. Harmless for a file it never opened: the
            // pool for that string is simply empty.
            ClearPool(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            {
                DataSource = file,
                Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadWriteCreate,
                Cache = Microsoft.Data.Sqlite.SqliteCacheMode.Default,
                DefaultTimeout = 5,
            });

            // MigrationSafetyCopy's shape — DataSource and Mode only. A
            // different string, therefore a different pool.
            ClearPool(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            {
                DataSource = file,
                Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadWriteCreate,
            });
        }
    }

    private static void ClearPool(Microsoft.Data.Sqlite.SqliteConnectionStringBuilder builder)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(builder.ToString());
        Microsoft.Data.Sqlite.SqliteConnection.ClearPool(connection);
    }
}
