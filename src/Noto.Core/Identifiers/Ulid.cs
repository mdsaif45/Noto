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

    /// <summary>
    /// The wall clock's high-water mark, and the randomness last issued at it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One slot, not a map. The wall clock only ever advances — the regression
    /// guard below enforces it — so once a millisecond has passed, no
    /// <see cref="NewId()"/> call can produce it again and its randomness is
    /// dead. Keeping more than the current instant would retain state nothing
    /// can ever consult.
    /// </para>
    /// <para>
    /// This is why the generator's state is O(1) regardless of how long the
    /// process runs or how many ids it mints.
    /// </para>
    /// </remarks>
    private static long _lastTimestamp = -1;

    private static readonly byte[] LastRandomness = new byte[RandomnessBytes];

    /// <summary>The random component's width in bytes — 80 bits.</summary>
    private const int RandomnessBytes = 10;

    /// <summary>
    /// Creates a new ULID for the current instant, strictly increasing even
    /// within one millisecond.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ids from this overload are unique and strictly increasing in the
    /// generator's serialized allocation order — the guarantee design §7 rule 2
    /// leans on when equal <c>SortOrder</c> falls back to the id "rather than
    /// to chance".
    /// </para>
    /// <para>
    /// It does <b>not</b> delegate to the explicit overload: that path draws
    /// fresh randomness every call by design, which would discard the sequence
    /// this one maintains.
    /// </para>
    /// </remarks>
    public static string NewId()
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        byte[] randomness = new byte[RandomnessBytes];

        lock (SyncRoot)
        {
            // The clock moved backwards (an NTP correction; UTC makes DST
            // irrelevant). Keep issuing from the last known point rather than
            // emitting ids that sort before data already written.
            if (now < _lastTimestamp)
            {
                now = _lastTimestamp;
            }

            if (now == _lastTimestamp)
            {
                // Same millisecond: continue the run rather than drawing fresh
                // bytes, which would order the two ids by chance.
                Array.Copy(LastRandomness, randomness, randomness.Length);
                IncrementInPlace(randomness);
            }
            else
            {
                RandomNumberGenerator.Fill(randomness);
            }

            // One slot, holding only the instant still being issued at. The
            // clock never returns to an earlier millisecond, so nothing older
            // is ever consulted again.
            _lastTimestamp = now;
            Array.Copy(randomness, LastRandomness, randomness.Length);
        }

        return Encode(now, randomness);
    }

    /// <summary>
    /// Creates a ULID for an explicit instant, honouring exactly the timestamp
    /// it is given.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The caller's instant is used verbatim: the wall-clock regression guard
    /// is deliberately <b>not</b> applied, because silently clamping a supplied
    /// timestamp forward would corrupt an import carrying original creation
    /// times (ADR-012 — "export and import are a contract").
    /// </para>
    /// <para>
    /// The random component is drawn fresh from the CSPRNG on every call, and
    /// <b>no per-instant sequence is retained</b>. Two ids sharing an
    /// explicitly supplied instant are unique and stably comparable, but their
    /// order does <b>not</b> reflect the order in which they were issued.
    /// Same-instant issuance ordering is guaranteed for <see cref="NewId()"/>
    /// alone.
    /// </para>
    /// <para>
    /// That is a deliberate contract boundary, not an oversight. Retaining a
    /// sequence per supplied instant would mean unbounded state: the overload
    /// accepts any instant and may revisit any of them, so nothing would ever
    /// license discarding an entry. The alternative — a bounded cache — silently
    /// loses the sequence on eviction, which is the defect this replaced.
    /// </para>
    /// </remarks>
    public static string NewId(DateTimeOffset timestamp)
    {
        long ms = timestamp.ToUnixTimeMilliseconds();

        // No lock and no shared state: this path reads and writes nothing that
        // another caller can observe, which is what makes it O(1) and free of
        // the eviction problem entirely.
        byte[] randomness = new byte[RandomnessBytes];
        RandomNumberGenerator.Fill(randomness);

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
