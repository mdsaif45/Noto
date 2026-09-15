using Noto.Core.Identifiers;
using Xunit;

namespace Noto.Core.Tests.Identifiers;

/// <summary>
/// ULIDs are the primary key of every user-facing entity (ADR-012), so these
/// properties are load-bearing: uniqueness protects against data collisions on
/// import, and ordering is what makes them preferable to a GUID.
/// </summary>
public sealed class UlidTests
{
    [Fact]
    public void Is_twenty_six_characters_of_crockford_base32()
    {
        string id = Ulid.NewId();

        Assert.Equal(26, id.Length);
        Assert.Equal(Ulid.Length, id.Length);

        // Crockford excludes I, L, O and U to avoid transcription errors.
        Assert.All(id, c => Assert.Contains(c, "0123456789ABCDEFGHJKMNPQRSTVWXYZ"));
    }

    [Fact]
    public void Is_monotonic_within_the_same_millisecond()
    {
        // T1/T2. ADR-012: ids sharing a timestamp increase in the generator's
        // allocation order. Guaranteed for NewId() -- the wall-clock path --
        // which is where design section 7 rule 2 gets its "deterministic
        // tiebreak for two notes created in the same instant".
        //
        // 2000 ids issue far faster than a millisecond ticks, so the run is
        // dominated by same-millisecond allocations.
        string[] ids = [.. Enumerable.Range(0, 2_000).Select(_ => Ulid.NewId())];

        Assert.Equal(ids, ids.Order(StringComparer.Ordinal));
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Issues_thousands_at_one_millisecond_without_collision()
    {
        // T3. The randomness increments rather than re-drawing, so a long run
        // inside one millisecond must neither wrap nor repeat.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string previous = string.Empty;

        for (int i = 0; i < 5_000; i++)
        {
            string id = Ulid.NewId();
            Assert.True(seen.Add(id), $"duplicate at {i}");

            if (previous.Length > 0)
            {
                Assert.True(string.CompareOrdinal(previous, id) < 0, $"not increasing at {i}");
            }

            previous = id;
        }
    }

    [Fact]
    public void Sorts_lexicographically_in_creation_order()
    {
        string earlier = Ulid.NewId(DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000));
        string later = Ulid.NewId(DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_001_000));

        Assert.True(string.CompareOrdinal(earlier, later) < 0);
    }

    [Fact]
    public void Produces_no_duplicates_under_rapid_generation()
    {
        string[] ids = [.. Enumerable.Range(0, 10_000).Select(_ => Ulid.NewId())];

        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Round_trips_its_timestamp()
    {
        var instant = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_123_456);

        Assert.Equal(instant, Ulid.GetTimestamp(Ulid.NewId(instant)));
    }

    [Fact]
    public void The_explicit_overload_honours_the_instant_it_is_given()
    {
        // It must NOT apply the wall-clock regression guard: silently clamping
        // a caller's timestamp forward would corrupt an import that supplies
        // original creation times.
        var past = DateTimeOffset.FromUnixTimeMilliseconds(1_600_000_000_000);

        _ = Ulid.NewId();   // advances the shared monotonic state to "now"

        Assert.Equal(past, Ulid.GetTimestamp(Ulid.NewId(past)));
    }

    [Fact]
    public void The_wall_clock_path_never_goes_backwards()
    {
        // An NTP correction must not produce ids that sort before existing
        // data. Only NewId() carries this guarantee.
        string[] ids = [.. Enumerable.Range(0, 200).Select(_ => Ulid.NewId())];

        Assert.Equal(ids, ids.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void An_explicit_instant_yields_unique_ids_without_retained_state()
    {
        // T6/T8. The explicit overload draws fresh randomness every call and
        // keeps no per-instant sequence. Two ids at one supplied instant are
        // unique and stably comparable; their order does NOT reflect issuance
        // order, and none is promised.
        var instant = DateTimeOffset.FromUnixTimeMilliseconds(1_500_000_000_000);

        string[] ids = [.. Enumerable.Range(0, 1_000).Select(_ => Ulid.NewId(instant))];

        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ids, id => Assert.Equal(instant, Ulid.GetTimestamp(id)));
    }

    [Fact]
    public void An_explicit_instant_does_not_depend_on_previous_calls()
    {
        // T8. Revisiting an instant after arbitrary unrelated generation must
        // work, and must not require the generator to have remembered it.
        var instant = DateTimeOffset.FromUnixTimeMilliseconds(1_500_000_000_000);

        string first = Ulid.NewId(instant);

        for (int i = 1; i <= 5_000; i++)
        {
            Ulid.NewId(DateTimeOffset.FromUnixTimeMilliseconds(1_500_000_000_000 + i));
        }

        string second = Ulid.NewId(instant);

        Assert.NotEqual(first, second);
        Assert.Equal(instant, Ulid.GetTimestamp(second));
    }

    [Fact]
    public void Explicit_instants_order_by_their_timestamp_not_by_issuance()
    {
        // T5. The central distinction: lexical order follows the TIMESTAMP
        // component, so an id minted later for an earlier instant sorts first.
        // That is expected, and is what makes import possible.
        var earlier = DateTimeOffset.FromUnixTimeMilliseconds(1_500_000_000_000);
        var later = DateTimeOffset.FromUnixTimeMilliseconds(1_600_000_000_000);

        string issuedFirst = Ulid.NewId(later);
        string issuedSecond = Ulid.NewId(earlier);

        Assert.True(
            string.CompareOrdinal(issuedSecond, issuedFirst) < 0,
            "an id for an earlier instant must sort first, whenever it was issued");
    }

    [Fact]
    public void Explicit_generation_does_not_accumulate_state()
    {
        // T7. The explicit path holds nothing, so supplying a great many
        // distinct instants must not grow the generator. Asserted through
        // behaviour rather than by inspecting privates: a wall-clock id issued
        // afterwards still continues its own sequence correctly.
        for (int i = 0; i < 20_000; i++)
        {
            Ulid.NewId(DateTimeOffset.FromUnixTimeMilliseconds(1_400_000_000_000 + i));
        }

        string[] wallClock = [.. Enumerable.Range(0, 500).Select(_ => Ulid.NewId())];

        Assert.Equal(wallClock, wallClock.Order(StringComparer.Ordinal));
        Assert.Equal(wallClock.Length, wallClock.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Concurrent_wall_clock_callers_receive_unique_increasing_ids()
    {
        // T4. "Issue order" means the generator's SERIALIZED ALLOCATION order,
        // not thread scheduling order: the counter and the id are captured
        // together, so the sequence checked is the one actually produced.
        const int threads = 8;
        const int perThread = 500;

        var issued = new System.Collections.Concurrent.ConcurrentBag<(long Order, string Id)>();
        long sequence = 0;
        using var start = new Barrier(threads);
        object gate = new();

        Parallel.For(0, threads, _ =>
        {
            start.SignalAndWait();

            for (int i = 0; i < perThread; i++)
            {
                lock (gate)
                {
                    issued.Add((Interlocked.Increment(ref sequence), Ulid.NewId()));
                }
            }
        });

        var inAllocationOrder = issued.OrderBy(x => x.Order).Select(x => x.Id).ToList();

        Assert.Equal(threads * perThread, inAllocationOrder.Count);
        Assert.Equal(inAllocationOrder, inAllocationOrder.Order(StringComparer.Ordinal));
        Assert.Equal(inAllocationOrder.Count, inAllocationOrder.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Concurrent_explicit_callers_receive_unique_ids()
    {
        // T6 under concurrency. Uniqueness only: no issuance ordering is
        // promised for the explicit path, so asserting one would encode a
        // guarantee the contract withholds.
        var instant = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
        const int threads = 8;
        const int perThread = 500;

        var ids = new System.Collections.Concurrent.ConcurrentBag<string>();
        using var start = new Barrier(threads);

        Parallel.For(0, threads, _ =>
        {
            start.SignalAndWait();
            for (int i = 0; i < perThread; i++)
            {
                ids.Add(Ulid.NewId(instant));
            }
        });

        Assert.Equal(threads * perThread, ids.Count);
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("too-short")]
    [InlineData("IIIIIIIIIIIIIIIIIIIIIIIIII")]   // I is not in the alphabet
    public void Rejects_invalid_identifiers(string? candidate)
    {
        Assert.False(Ulid.IsValid(candidate));
    }

    [Fact]
    public void Accepts_what_it_generates()
    {
        Assert.True(Ulid.IsValid(Ulid.NewId()));
    }
}
