using Noto.Core.Diagnostics;
using Noto.Core.Settings;

namespace Noto.Core.Storage;

/// <summary>
/// Persistence diagnostics.
/// </summary>
/// <remarks>
/// A narrow interface rather than a general logger: it names the events worth
/// recording, which makes it obvious that <b>note content is never among
/// them</b> (principle 10).
/// <para>
/// The parameters make the rule hold by type, not by care (#7): a location is a
/// <see cref="DataRootPath"/>, which has no absolute form; a failure is an
/// <see cref="ExceptionSummary"/>, which has no message or stack trace; a
/// setting is its <see cref="SettingKey"/>, never its value. No parameter takes
/// free text a caller could fill with content.
/// </para>
/// </remarks>
public interface IStorageLog
{
    /// <summary>
    /// A pre-migration safety copy was written: where it is, relative to the
    /// data root, is what a user needs if a migration goes wrong.
    /// </summary>
    void SafetyCopyCreated(int fromVersion, DataRootPath copy);

    void MigrationStarting(int fromVersion, int toVersion, int pendingCount);

    void MigrationApplied(int version, string description);

    void MigrationCompleted(int version);

    /// <summary>
    /// A migration failed and was rolled back.
    /// </summary>
    void MigrationFailed(int version, ExceptionSummary failure);

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
    void SettingFellBackToDefault(SettingKey key, SettingFallbackReason reason);

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
    void SettingsUnreadable(ExceptionSummary failure);

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
    void SettingSubscriberFailed(SettingKey key, ExceptionSummary failure);
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

/// <summary>Discards everything: for components and tests that are given no log.</summary>
public sealed class NullStorageLog : IStorageLog
{
    public static NullStorageLog Instance { get; } = new();

    private NullStorageLog()
    {
    }

    public void SafetyCopyCreated(int fromVersion, DataRootPath copy)
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

    public void MigrationFailed(int version, ExceptionSummary failure)
    {
    }

    public void SettingFellBackToDefault(SettingKey key, SettingFallbackReason reason)
    {
    }

    public void SettingsUnreadable(ExceptionSummary failure)
    {
    }

    public void SettingSubscriberFailed(SettingKey key, ExceptionSummary failure)
    {
    }
}
