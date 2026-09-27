namespace Noto.Core.Storage;

/// <summary>
/// Persistence diagnostics.
/// </summary>
/// <remarks>
/// A narrow interface rather than a general logger: it names the events worth
/// recording, which makes it obvious that <b>note content is never among
/// them</b> (principle 10). The full logging infrastructure arrives with #7;
/// this is the seam it will plug into.
/// </remarks>
public interface IStorageLog
{
    /// <summary>
    /// A pre-migration safety copy was written. The path is infrastructure, not
    /// user content, so recording it is safe — and it is what a user needs if a
    /// migration goes wrong.
    /// </summary>
    void SafetyCopyCreated(int fromVersion, string path);

    void MigrationStarting(int fromVersion, int toVersion, int pendingCount);

    void MigrationApplied(int version, string description);

    void MigrationCompleted(int version);

    /// <summary>
    /// A migration failed and was rolled back. The exception is a SQLite error
    /// about schema, never about content.
    /// </summary>
    void MigrationFailed(int version, Exception exception);

    /// <summary>
    /// A stored setting could not be used and its default was substituted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The stored value is deliberately not a parameter.</b> A setting is
    /// user-controlled text, and a logger that takes values by habit is how
    /// something sensitive reaches a log file later (principle 10). The key,
    /// the reason and the expected type are enough to diagnose the row, and a
    /// reader who needs the value can open the database.
    /// </para>
    /// <para>
    /// A <i>missing</i> key is not reported here. Absence is the normal way an
    /// untouched setting is stored (#9), so logging it would fill the log on
    /// every clean install.
    /// </para>
    /// </remarks>
    void SettingFellBackToDefault(string key, SettingFallbackReason reason, string expectedType);

    /// <summary>
    /// Settings could not be read at all, so every setting is at its default
    /// for this session.
    /// </summary>
    /// <remarks>
    /// Recorded because it is invisible otherwise: the application starts
    /// normally and behaves as a clean install, which is exactly what #9's
    /// "settings cannot prevent the application from starting" requires and
    /// also exactly what a user would report as "it forgot my settings".
    /// </remarks>
    void SettingsUnreadable(Exception exception);

    /// <summary>
    /// A settings subscriber threw while being told a value changed.
    /// </summary>
    /// <remarks>
    /// The write is already committed by the time subscribers run, so the
    /// exception cannot be rethrown to the writer without failing an
    /// operation that in fact succeeded (ADR-010). It is recorded here
    /// instead, because a subscriber failing silently is how a view stops
    /// reacting to a setting and nobody finds out.
    /// </remarks>
    void SettingSubscriberFailed(string key, Exception exception);
}

/// <summary>
/// Why a stored setting was replaced by its default.
/// </summary>
/// <remarks>
/// <see cref="Corrupt"/> and <see cref="Invalid"/> are separated because they
/// have different causes and different fixes. Corrupt means the text is not of
/// the key's type at all — a hand edit, a partial write, or a future version
/// storing a richer form. Invalid means it parsed cleanly but broke the key's
/// rule, which is most often a range this build narrowed since the value was
/// written. Collapsing them into one reason hides version skew.
/// </remarks>
public enum SettingFallbackReason
{
    /// <summary>The stored text could not be parsed as the key's type.</summary>
    Corrupt,

    /// <summary>It parsed, but failed the key's validity rule.</summary>
    Invalid,
}

/// <summary>Discards everything. The default until #7 lands.</summary>
public sealed class NullStorageLog : IStorageLog
{
    public static NullStorageLog Instance { get; } = new();

    private NullStorageLog()
    {
    }

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

    public void SettingFellBackToDefault(string key, SettingFallbackReason reason, string expectedType)
    {
    }

    public void SettingsUnreadable(Exception exception)
    {
    }

    public void SettingSubscriberFailed(string key, Exception exception)
    {
    }
}
