using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Noto.Core.Settings;
using Noto.Core.Storage;

namespace Noto.Infrastructure.Storage;

/// <summary>
/// The SQLite implementation of <see cref="ISettingsStore"/>.
/// </summary>
/// <remarks>
/// <para>
/// Hand-written SQL over <c>Microsoft.Data.Sqlite</c>, no ORM, matching the
/// three repositories (ADR-003). Every SQLite concern stops here: failures
/// become <see cref="StorageException"/> and no <c>Sqlite*</c> type crosses
/// back out.
/// </para>
/// <para>
/// <b>Reads come from memory, not from SQLite.</b> <see cref="Load"/> runs one
/// statement at startup and everything after that is served from the cache.
/// #9 requires that "loading does not measurably affect startup" and
/// architecture-overview.md puts settings in phase 1, ahead of the first
/// window — a per-read query would put file I/O on the path that decides
/// whether the hotkey feels instant.
/// </para>
/// <para>
/// The cache is owned here rather than exposed: it is how this store is fast,
/// not part of what a settings store is. Nothing else writes the table, so
/// write-through cannot go stale.
/// </para>
/// </remarks>
public sealed class SqliteSettingsStore : ISettingsStore
{
    private readonly NotoDatabase _database;
    private readonly IStorageLog _log;

    /// <summary>
    /// Parsed values for declared keys, by key name.
    /// </summary>
    /// <remarks>
    /// Holds only what was usable. A key that is absent, unparseable or
    /// invalid is simply not here, and <see cref="Read{T}"/> falls back — so
    /// there is no sentinel and no "is this a real value" flag to get wrong.
    /// </remarks>
    private readonly Dictionary<string, object> _cache = [];

    public SqliteSettingsStore(NotoDatabase database, IStorageLog? log = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _log = log ?? NullStorageLog.Instance;
    }

    public event EventHandler<SettingChangedEventArgs>? SettingChanged;

    /// <summary>
    /// Reads every declared setting into the cache. Call once, at startup.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Never throws.</b> If the table cannot be read the store runs
    /// entirely on defaults and says so through the log — #9's definition of
    /// done is that "settings cannot prevent the application from starting".
    /// </para>
    /// <para>
    /// That is not in tension with the startup policy for an unopenable
    /// database, which is still to refuse to start
    /// (architecture-overview.md §error handling). That decision is made
    /// earlier and elsewhere, by <see cref="NotoDatabase.Initialize"/>: by the
    /// time this runs the database is open and migrated. The two rules govern
    /// different failures — losing the user's notes, versus losing their
    /// preferences — and only the first is worth refusing to start over.
    /// </para>
    /// <para>
    /// One statement reads the whole table rather than one per key: there are
    /// a handful of rows, and it also lets unknown keys be seen and left
    /// alone rather than never being looked at.
    /// </para>
    /// </remarks>
    /// <param name="keys">
    /// Which keys to materialise. Defaults to <see cref="SettingKeys.All"/>,
    /// which is what the application passes; a caller supplies its own only
    /// to exercise a key that is not a production setting.
    /// </param>
    /// <param name="families">
    /// Which key families to materialise, by the same rule. Defaults to
    /// <see cref="SettingKeys.Families"/>. A row is materialised as a family
    /// member only when its name is one of these prefixes, the separator and
    /// a non-blank scope; every other row stays unknown.
    /// </param>
    public void Load(IReadOnlyList<SettingKey>? keys = null, IReadOnlyList<SettingKeyFamily>? families = null)
    {
        Dictionary<string, string> rows;

        try
        {
            rows = ReadAllRows();
        }
        catch (StorageException ex)
        {
            // Defaults for this session. Deliberately not rethrown, and
            // deliberately not written back: see WriteBack note below.
            _cache.Clear();
            _log.SettingsUnreadable(ex);
            return;
        }

        _cache.Clear();

        foreach (SettingKey key in keys ?? SettingKeys.All)
        {
            // Absent is the normal state of a setting nobody has changed
            // (#9), so it is not logged and not an error.
            if (!rows.TryGetValue(key.Name, out string? stored))
            {
                continue;
            }

            if (TryMaterialise(key, stored, out object? value))
            {
                _cache[key.Name] = value;
            }
        }

        // Family members: only rows whose name a declared family claims. A
        // row that no family claims — including "prefix::" with a blank
        // scope — is an unknown key and is never looked at.
        IReadOnlyList<SettingKeyFamily> declared = families ?? SettingKeys.Families;

        foreach ((string name, string stored) in rows)
        {
            foreach (SettingKeyFamily family in declared)
            {
                if (family.MemberNamed(name) is not SettingKey member)
                {
                    continue;
                }

                if (TryMaterialise(member, stored, out object? value))
                {
                    _cache[member.Name] = value;
                }

                break;
            }
        }

        // Rows whose key is in no SettingKey and no family are simply never
        // looked at. They stay in the table untouched, so settings a newer
        // Noto wrote survive an older Noto opening the same database.
    }

    public T Read<T>(SettingKey<T> key) => TryRead(key, out T value) ? value : key.Default;

    public bool TryRead<T>(SettingKey<T> key, out T value)
    {
        ArgumentNullException.ThrowIfNull(key);

        // The cache holds only values that parsed and passed validation, so a
        // hit is exactly "a usable value is stored".
        if (_cache.TryGetValue(key.Name, out object? cached) && cached is T typed)
        {
            value = typed;
            return true;
        }

        value = key.Default;
        return false;
    }

    public bool Write<T>(SettingKey<T> key, T value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);

        // Validate before persisting, so an illegal value never reaches the
        // table and cannot later come back as a fallback-with-a-log.
        if (!key.IsValid(value))
        {
            return false;
        }

        Persist(key.Name, Serialise(key.Kind, value));

        _cache[key.Name] = value;

        // After the write and the cache, so a subscriber that reads back sees
        // the new value. A subscriber that throws does not fail a write that
        // has already committed (ADR-010).
        RaiseChanged(key);

        return true;
    }

    private Dictionary<string, string> ReadAllRows()
    {
        try
        {
            using var connection = _database.OpenConnection();
            using var command = connection.CreateCommand();

            command.CommandText = "SELECT Key, Value FROM Settings;";

            using var reader = command.ExecuteReader();

            var rows = new Dictionary<string, string>(StringComparer.Ordinal);

            while (reader.Read())
            {
                rows[reader.GetString(0)] = reader.GetString(1);
            }

            return rows;
        }
        catch (SqliteException ex)
        {
            throw new StorageException(
                StorageFailure.Unknown,
                "Could not read the settings.",
                ex);
        }
    }

    private void Persist(string key, string value)
    {
        try
        {
            using var connection = _database.OpenConnection();
            using var command = connection.CreateCommand();

            // One statement, so insert and update are the same operation and
            // cannot disagree. No transaction: a single-row upsert is already
            // atomic, and wrapping it would only add a second thing to fail.
            command.CommandText =
                """
                INSERT INTO Settings (Key, Value)
                VALUES ($key, $value)
                ON CONFLICT (Key) DO UPDATE SET Value = excluded.Value;
                """;

            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$value", value);

            command.ExecuteNonQuery();
        }
        catch (SqliteException ex)
        {
            // The key names a setting, never its value — the value is what
            // could carry something the user would not want in a message
            // (principle 10).
            throw new StorageException(
                StorageFailure.WriteFailed,
                $"Could not save the setting '{key}'.",
                ex);
        }
    }

    private void RaiseChanged(SettingKey key)
    {
        EventHandler<SettingChangedEventArgs>? handlers = SettingChanged;

        if (handlers is null)
        {
            return;
        }

        var args = new SettingChangedEventArgs(key);

        // Invoked one at a time rather than through the multicast delegate, so
        // one subscriber throwing does not stop the rest being told. The write
        // is already committed either way.
        foreach (EventHandler<SettingChangedEventArgs> handler
            in handlers.GetInvocationList().Cast<EventHandler<SettingChangedEventArgs>>())
        {
            try
            {
                handler(this, args);
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
            {
                // Wide, because a subscriber is arbitrary caller code and
                // there is no set of exception types it is known to throw —
                // but not unconditional. A process running out of memory or
                // stack is not a subscriber bug, and continuing to notify the
                // remaining subscribers after one would be pretending the
                // process is still healthy.
                //
                // It cannot be rethrown: the value is already committed by the
                // time subscribers run, so failing the write would report a
                // successful operation as a failed one (ADR-010). Recorded
                // instead — a subscriber that quietly stops reacting to a
                // setting is invisible until someone reports the UI "not
                // updating", and #7 gives this somewhere to go.
                _log.SettingSubscriberFailed(key.Name, ex);
            }
        }
    }

    /// <summary>
    /// Turns a stored string into the key's value type, or reports why not.
    /// </summary>
    private bool TryMaterialise(SettingKey key, string stored, out object value)
    {
        value = default!;

        if (!TryParse(key, stored, out object? parsed))
        {
            _log.SettingFellBackToDefault(key.Name, SettingFallbackReason.Corrupt, key.Kind.ToString());
            return false;
        }

        if (!key.IsValidValue(parsed))
        {
            _log.SettingFellBackToDefault(key.Name, SettingFallbackReason.Invalid, key.Kind.ToString());
            return false;
        }

        value = parsed;
        return true;
    }

    /// <summary>
    /// Parses the documented text form of each supported type.
    /// </summary>
    /// <remarks>
    /// <b>Invariant culture throughout.</b> A <c>double</c> written on a
    /// machine whose locale uses a decimal comma and read on one that uses a
    /// point is a real defect and a silent one; pinning the culture on both
    /// sides is what makes the round trip exact regardless of where the
    /// database was written.
    /// </remarks>
    private static bool TryParse(
        SettingKey key,
        string stored,
        [NotNullWhen(true)] out object? value)
    {
        switch (key.Kind)
        {
            case SettingValueKind.Boolean:
                if (bool.TryParse(stored, out bool b))
                {
                    value = b;
                    return true;
                }

                break;

            case SettingValueKind.Whole:
                if (int.TryParse(stored, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i))
                {
                    value = i;
                    return true;
                }

                break;

            case SettingValueKind.Real:
                if (double.TryParse(stored, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
                    && !double.IsNaN(d)
                    && !double.IsInfinity(d))
                {
                    value = d;
                    return true;
                }

                break;

            case SettingValueKind.Text:
                // Any text is a legal string. Whether it is a legal *value* is
                // the key's validity rule, checked separately, which is what
                // keeps "unparseable" and "not allowed" distinguishable.
                value = stored;
                return true;

            case SettingValueKind.Enumeration:
                Type enumType = key.ValueType;

                // Case-sensitive, and Enum.IsDefined on top. Measured:
                // Enum.TryParse(typeof(E), "7", false, out v) returns true
                // with v == 7 for a two-member enum, so without IsDefined an
                // undefined number reaches the cache and no switch handles
                // it. A defined number ("1") does round-trip, which is
                // harmless — it names a real member.
                if (Enum.TryParse(enumType, stored, ignoreCase: false, out object? parsed)
                    && parsed is not null
                    && Enum.IsDefined(enumType, parsed))
                {
                    value = parsed;
                    return true;
                }

                break;

            default:
                break;
        }

        value = null;
        return false;
    }

    private static string Serialise<T>(SettingValueKind kind, T value) => kind switch
    {
        // "True"/"False" — what bool.TryParse reads back, culture-independent.
        SettingValueKind.Boolean => value!.ToString()!,
        SettingValueKind.Whole => ((int)(object)value!).ToString(CultureInfo.InvariantCulture),

        // "R" round-trips every finite double exactly; the default format does
        // not, so a written value could read back subtly different.
        SettingValueKind.Real => ((double)(object)value!).ToString("R", CultureInfo.InvariantCulture),
        SettingValueKind.Text => (string)(object)value!,

        // The member name, not its number: names survive a member being
        // inserted in the middle of the enum, numbers do not.
        SettingValueKind.Enumeration => value!.ToString()!,
        _ => throw new NotSupportedException($"No text form for '{kind}'."),
    };

}
