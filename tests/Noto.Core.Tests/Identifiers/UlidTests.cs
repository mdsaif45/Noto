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
        // Two notes created in the same millisecond must still have a defined
        // order, or "sort by id" is non-deterministic.
        var fixedInstant = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);

        string[] ids = [.. Enumerable.Range(0, 500).Select(_ => Ulid.NewId(fixedInstant))];

        Assert.Equal(ids, ids.Order(StringComparer.Ordinal));
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
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
    public void An_instant_revisited_after_a_later_one_continues_its_run()
    {
        // REGRESSION. ADR-012 promises ids are "monotonic within the same
        // millisecond, so ordering is total". An earlier implementation
        // tracked only the MOST RECENT millisecond, so returning to an earlier
        // instant drew fresh randomness and produced two ids at the same
        // millisecond with unrelated random components — ordering by chance,
        // measured at ~49% inverted.
        //
        // Single-threaded on purpose: the defect needs no concurrency, only an
        // interleaved wall-clock id, which is what any import carrying
        // original creation times runs into.
        var past = DateTimeOffset.FromUnixTimeMilliseconds(1_500_000_000_000);

        var ids = new List<string>();
        for (int i = 0; i < 200; i++)
        {
            ids.Add(Ulid.NewId(past));
            _ = Ulid.NewId();          // a later instant, in between
        }

        Assert.Equal(ids, ids.Order(StringComparer.Ordinal));
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Two_instants_interleaved_each_keep_their_own_run()
    {
        // Two callers importing from different eras, alternating. Each
        // millisecond's sequence must remain ascending independently.
        var first = DateTimeOffset.FromUnixTimeMilliseconds(1_500_000_000_000);
        var second = DateTimeOffset.FromUnixTimeMilliseconds(1_600_000_000_000);

        var a = new List<string>();
        var b = new List<string>();

        for (int i = 0; i < 100; i++)
        {
            a.Add(Ulid.NewId(first));
            b.Add(Ulid.NewId(second));
        }

        Assert.Equal(a, a.Order(StringComparer.Ordinal));
        Assert.Equal(b, b.Order(StringComparer.Ordinal));
        Assert.Empty(a.Intersect(b, StringComparer.Ordinal));
    }

    [Fact]
    public void Concurrent_callers_at_one_instant_produce_a_totally_ordered_sequence()
    {
        // ADR-012's guarantee is over the GENERATED SEQUENCE, not per caller:
        // "ORDER BY Id" is a query over every row whichever thread minted it.
        // Interleaving A1 B1 A2 is correct; a descent in issue order is not.
        //
        // A Barrier rather than sleeps, so the threads genuinely contend.
        var instant = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
        const int threads = 8;
        const int perThread = 500;

        var issued = new System.Collections.Concurrent.ConcurrentBag<(long Order, string Id)>();
        long sequence = 0;
        using var start = new Barrier(threads);

        Parallel.For(0, threads, _ =>
        {
            start.SignalAndWait();

            for (int i = 0; i < perThread; i++)
            {
                // The counter and the id are taken together so issue order is
                // observable; without that, "ascending" is untestable.
                lock (issued)
                {
                    issued.Add((Interlocked.Increment(ref sequence), Ulid.NewId(instant)));
                }
            }
        });

        var inIssueOrder = issued.OrderBy(x => x.Order).Select(x => x.Id).ToList();

        Assert.Equal(threads * perThread, inIssueOrder.Count);
        Assert.Equal(inIssueOrder, inIssueOrder.Order(StringComparer.Ordinal));
        Assert.Equal(inIssueOrder.Count, inIssueOrder.Distinct(StringComparer.Ordinal).Count());
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
