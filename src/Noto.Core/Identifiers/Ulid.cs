using System.Security.Cryptography;

namespace Noto.Core.Identifiers;

/// <summary>
/// Generates ULIDs — 26-character, lexicographically sortable identifiers
/// (ADR-012).
/// </summary>
/// <remarks>
/// <para>
/// A ULID is a 48-bit millisecond timestamp followed by 80 bits of randomness,
/// encoded as 26 characters of Crockford base32. That makes it globally unique
/// like a GUID, but ordered by creation time, so it indexes well and sorts
/// meaningfully.
/// </para>
/// <para>
/// Hand-written rather than taken as a dependency: the algorithm is small and
/// fully testable, and a package would be one more thing to audit and update
/// forever (principle 9).
/// </para>
/// </remarks>
public static class Ulid
{
    /// <summary>Crockford base32 — excludes I, L, O and U to avoid transcription errors.</summary>
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    private const int TimestampLength = 10;
    private const int RandomnessLength = 16;

    /// <summary>Total length of a canonical ULID.</summary>
    public const int Length = TimestampLength + RandomnessLength;

    private static readonly Lock SyncRoot = new();

    /// <summary>The wall clock's high-water mark, for the regression guard.</summary>
    private static long _lastTimestamp = -1;

    /// <summary>
    /// The last randomness issued for each millisecond still being tracked.
    /// </summary>
    /// <remarks>
    /// Per timestamp rather than a single "most recent" slot: a caller may
    /// legitimately revisit an earlier instant — an import carrying original
    /// creation times does exactly that — and such an id must still continue
    /// that millisecond's run rather than starting a new one beside it.
    /// </remarks>
    private static readonly Dictionary<long, byte[]> LastRandomnessByTimestamp = [];

    /// <summary>
    /// How many distinct milliseconds stay tracked.
    /// </summary>
    /// <remarks>
    /// A bound, because the map would otherwise grow for the life of the
    /// process. Evicting the oldest is safe: a millisecond that has fallen out
    /// is one no caller has touched recently, so the next id at that instant
    /// starts a fresh run — the same thing that happens for any instant seen
    /// for the first time.
    /// </remarks>
    private const int TrackedTimestamps = 64;

    /// <summary>
    /// Creates a new ULID, monotonically increasing even within the same
    /// millisecond.
    /// </summary>
    public static string NewId()
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        lock (SyncRoot)
        {
            // The clock moved backwards (an NTP correction; UTC makes DST
            // irrelevant). Keep issuing from the last known point rather than
            // emitting ids that sort before data already written.
            if (now < _lastTimestamp)
            {
                now = _lastTimestamp;
            }

            // The high-water mark belongs to the wall-clock path alone. The
            // explicit-timestamp overload must not advance it, or supplying an
            // older instant would drag every later wall-clock id forward —
            // which is the import-corruption case the guard was scoped against
            // when it was introduced.
            _lastTimestamp = now;
        }

        return NewId(DateTimeOffset.FromUnixTimeMilliseconds(now));
    }

    /// <summary>
    /// Creates a ULID for an explicit instant, <b>without</b> the monotonic
    /// clock-regression guard.
    /// </summary>
    /// <remarks>
    /// The guard exists to stop a wall-clock correction producing ids that sort
    /// before existing data. Applying it here would silently override the
    /// caller's instant, so this overload honours exactly what it is given.
    /// Randomness is still incremented within a millisecond, so a run of ids at
    /// one instant remains ordered.
    /// </remarks>
    public static string NewId(DateTimeOffset timestamp)
    {
        long ms = timestamp.ToUnixTimeMilliseconds();

        // Monotonicity matters: two notes created in the same millisecond must
        // still have a defined order, or "sort by id" is non-deterministic.
        byte[] randomness = new byte[10];

        lock (SyncRoot)
        {
            // Keyed by millisecond, not by "the most recent one". Tracking only
            // the latest instant silently broke ADR-012's guarantee whenever a
            // caller returned to an earlier timestamp:
            //
            //   NewId(t)   -> randomness R
            //   NewId()    -> "now", a later instant, overwrites the state
            //   NewId(t)   -> t is no longer "the last", so fresh bytes are
            //                 drawn and the second id sits at the SAME
            //                 millisecond with unrelated randomness
            //
            // The two ids then order by chance — measured at ~49% inverted.
            // That is exactly the case an import carrying original creation
            // times hits, interleaved with any ordinary id generation.
            if (LastRandomnessByTimestamp.TryGetValue(ms, out byte[]? previous))
            {
                Array.Copy(previous, randomness, randomness.Length);
                IncrementInPlace(randomness);
            }
            else
            {
                RandomNumberGenerator.Fill(randomness);
            }

            Remember(ms, randomness);
        }

        return Encode(ms, randomness);
    }

    /// <summary>
    /// Reads the creation instant back out of a ULID.
    /// </summary>
    public static DateTimeOffset GetTimestamp(string id)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);

        if (id.Length != Length)
        {
            throw new FormatException($"A ULID is {Length} characters; got {id.Length}.");
        }

        long ms = 0;
        for (int i = 0; i < TimestampLength; i++)
        {
            int value = Alphabet.IndexOf(char.ToUpperInvariant(id[i]), StringComparison.Ordinal);
            if (value < 0)
            {
                throw new FormatException($"'{id[i]}' is not valid Crockford base32.");
            }

            ms = (ms << 5) | (uint)value;
        }

        return DateTimeOffset.FromUnixTimeMilliseconds(ms);
    }

    /// <summary>Whether a string is a syntactically valid ULID.</summary>
    public static bool IsValid(string? id)
    {
        if (id is null || id.Length != Length)
        {
            return false;
        }

        foreach (char c in id)
        {
            if (Alphabet.IndexOf(char.ToUpperInvariant(c), StringComparison.Ordinal) < 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Records the randomness just issued for a millisecond, evicting the
    /// oldest tracked instant when the map is full.
    /// </summary>
    /// <remarks>Callers hold <see cref="SyncRoot"/>.</remarks>
    private static void Remember(long ms, byte[] randomness)
    {
        if (!LastRandomnessByTimestamp.ContainsKey(ms)
            && LastRandomnessByTimestamp.Count >= TrackedTimestamps)
        {
            // Linear scan over a 64-entry map, on a path that already holds a
            // lock and fills 10 cryptographic bytes. A heap would be more
            // machinery than the cost it saves.
            LastRandomnessByTimestamp.Remove(LastRandomnessByTimestamp.Keys.Min());
        }

        // Copied, not aliased: the caller returns this array to Encode and it
        // must not be mutated by a later increment.
        LastRandomnessByTimestamp[ms] = [.. randomness];
    }

    private static void IncrementInPlace(byte[] randomness)
    {
        for (int i = randomness.Length - 1; i >= 0; i--)
        {
            if (randomness[i] != byte.MaxValue)
            {
                randomness[i]++;
                return;
            }

            randomness[i] = 0;
        }

        // Overflowing all 80 bits inside one millisecond is not reachable in
        // practice; if it ever happened, ordering would be the lesser concern.
        throw new OverflowException("ULID randomness exhausted within a single millisecond.");
    }

    private static string Encode(long timestampMs, byte[] randomness)
    {
        Span<char> buffer = stackalloc char[Length];

        // Timestamp: 48 bits over 10 characters, most significant first.
        for (int i = TimestampLength - 1; i >= 0; i--)
        {
            buffer[i] = Alphabet[(int)(timestampMs & 0x1F)];
            timestampMs >>= 5;
        }

        // Randomness: 80 bits over 16 characters.
        int bitBuffer = 0;
        int bitCount = 0;
        int outIndex = TimestampLength;

        foreach (byte b in randomness)
        {
            bitBuffer = (bitBuffer << 8) | b;
            bitCount += 8;

            while (bitCount >= 5)
            {
                bitCount -= 5;
                buffer[outIndex++] = Alphabet[(bitBuffer >> bitCount) & 0x1F];
            }
        }

        return new string(buffer);
    }
}
