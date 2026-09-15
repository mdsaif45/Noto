namespace Noto.Core.Storage;

/// <summary>
/// A persistence failure the application can reason about.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately a single type with a <see cref="Reason"/> rather than a
/// hierarchy: callers branch on a handful of outcomes, and a class per failure
/// mode would be machinery without a second implementation (principle 9).
/// </para>
/// <para>
/// The originating exception is always preserved as the inner exception — the
/// infrastructure layer must not swallow diagnostic detail. It must equally
/// never put note content into the message (principle 10).
/// </para>
/// </remarks>
public sealed class StorageException : Exception
{
    public StorageException(StorageFailure reason, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Reason = reason;
    }

    public StorageException()
        : this(StorageFailure.Unknown, "A storage operation failed.")
    {
    }

    public StorageException(string message)
        : this(StorageFailure.Unknown, message)
    {
    }

    public StorageException(string message, Exception innerException)
        : this(StorageFailure.Unknown, message, innerException)
    {
    }

    public StorageFailure Reason { get; }
}

/// <summary>Why a storage operation failed.</summary>
public enum StorageFailure
{
    Unknown = 0,

    /// <summary>The database file could not be created or opened.</summary>
    CannotOpen,

    /// <summary>The database is not a valid Noto database, or is damaged.</summary>
    Corrupt,

    /// <summary>A migration failed. The database was rolled back and is unchanged.</summary>
    MigrationFailed,

    /// <summary>
    /// The database schema is newer than this build understands. Almost always
    /// means an older Noto opening a database a newer one wrote — continuing
    /// would risk corrupting it.
    /// </summary>
    SchemaTooNew,

    /// <summary>A write failed and was rolled back.</summary>
    WriteFailed,
}
