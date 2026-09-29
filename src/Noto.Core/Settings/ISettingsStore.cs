namespace Noto.Core.Settings;

/// <summary>
/// Which setting changed.
/// </summary>
/// <remarks>
/// Carries the <b>key only</b>, never the value. A subscriber that needs the
/// value reads it back through the store, so there is one source of truth for
/// what a setting currently is. Shipping the value here would create a second
/// one, and the two would differ the moment two writes raced.
/// </remarks>
public sealed class SettingChangedEventArgs(SettingKey key) : EventArgs
{
    /// <summary>The key whose value was just persisted.</summary>
    public SettingKey Key { get; } = key ?? throw new ArgumentNullException(nameof(key));
}

/// <summary>
/// Reads and writes Noto's settings.
/// </summary>
/// <remarks>
/// <para>
/// The port; the SQLite implementation lives in <c>Noto.Infrastructure</c>
/// (ADR-009). Core states what it needs and knows nothing about storage —
/// Core has no package references, so a <c>Sqlite*</c> type here would be a
/// build error rather than a review comment.
/// </para>
/// <para>
/// <b>Not a repository, and deliberately not shaped like one.</b> Settings are
/// not an aggregate: there is no id, no lifecycle, no soft delete, and no
/// failure model — <see cref="Read{T}"/> cannot fail, because every key has a
/// default. Naming it <c>ISettingsRepository</c> would promise the
/// three-repository symmetry of notes, folders and tags and then not deliver
/// any of it.
/// </para>
/// <para>
/// <b>There is no <c>Reset</c>.</b> Issue #9 does not ask for one, and
/// defaults are already recoverable without it. When a caller genuinely needs
/// to clear a key it is a small addition to the implementation, not a change
/// to this contract.
/// </para>
/// <para>
/// <b>Threading: the UI thread, and only the UI thread.</b> The application is
/// single user, single process (contract §10), and the cache and the event are
/// unsynchronised on that basis. A caller on another thread marshals first.
/// Making this type internally thread-safe would advertise a guarantee the
/// rest of the system does not make.
/// </para>
/// </remarks>
public interface ISettingsStore
{
    /// <summary>
    /// Raised after a write has been persisted and the cache updated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A plain .NET event, not an aggregator. #9 requires "change notification,
    /// so UI reacts without polling" and nothing more; the domain-event
    /// infrastructure of ADR-010 (#21) is for facts about entities —
    /// <c>NoteUpdated</c>, <c>FolderRenamed</c> — and a setting is not an
    /// entity. This neither anticipates #21 nor competes with it.
    /// </para>
    /// <para>
    /// <b>Raised once for every successful write</b>, including a write of
    /// the value already stored — the event means "this is now persisted",
    /// not "this is different". A rejected or failed write raises nothing, so
    /// a subscriber never sees an attempt that did not happen. A subscriber
    /// that only cares about differences compares for itself.
    /// </para>
    /// <para>
    /// <b>Lifetime is the subscriber's.</b> The store lives for the process, so
    /// it outlives every view that subscribes to it; a handler that is never
    /// removed keeps its target alive for the life of the application. Views
    /// unsubscribe when they close.
    /// </para>
    /// <para>
    /// <b>A throwing subscriber does not fail the write</b> (ADR-010). By the
    /// time this is raised the value is committed, so there is nothing left to
    /// roll back and no honest way to report failure to the writer.
    /// </para>
    /// </remarks>
    event EventHandler<SettingChangedEventArgs>? SettingChanged;

    /// <summary>
    /// The current value of a setting.
    /// </summary>
    /// <remarks>
    /// <b>Cannot fail and never throws.</b> A key that is missing, unparseable
    /// or invalid yields <see cref="SettingKey{T}.Default"/>. Callers do not
    /// branch on storage state to read a preference.
    /// </remarks>
    T Read<T>(SettingKey<T> key);

    /// <summary>
    /// The stored value of a setting, if there is a usable one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see langword="true"/> only when a row for the key was loaded, parsed
    /// as the key's type and passed its validity rule; <paramref name="value"/>
    /// is then that value. For a missing, corrupt or invalid row it is
    /// <see langword="false"/> and <paramref name="value"/> is
    /// <see cref="SettingKey{T}.Default"/> — the same value
    /// <see cref="Read{T}"/> returns, so the two never disagree.
    /// </para>
    /// <para>
    /// Exists for fallback chains, which need to know that nothing usable is
    /// stored rather than receive a default indistinguishable from a stored
    /// value. It changes nothing else: corrupt and invalid rows are reported
    /// and left untouched exactly as for <see cref="Read{T}"/>, and it never
    /// throws.
    /// </para>
    /// </remarks>
    bool TryRead<T>(SettingKey<T> key, out T value);

    /// <summary>
    /// Persists a setting.
    /// </summary>
    /// <remarks>
    /// Validates first: a value failing <see cref="SettingKey{T}.IsValid"/> is
    /// rejected and nothing is written. On success the row is upserted, the
    /// cache updated, and <see cref="SettingChanged"/> raised — in that order,
    /// so a subscriber that reads back always sees the new value.
    /// </remarks>
    /// <returns>
    /// <see langword="true"/> when the value was valid and persisted;
    /// <see langword="false"/> when it was rejected as invalid.
    /// </returns>
    /// <exception cref="Storage.StorageException">The write failed.</exception>
    bool Write<T>(SettingKey<T> key, T value);
}
