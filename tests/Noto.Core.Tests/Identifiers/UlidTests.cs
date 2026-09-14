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
