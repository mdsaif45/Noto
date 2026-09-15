using System.Globalization;

namespace Noto.Infrastructure.Storage;

/// <summary>
/// The one timestamp format every persisted column uses.
/// </summary>
/// <remarks>
/// ISO-8601 round-trip ("O"), invariant culture, UTC. Shared rather than
/// duplicated per repository: a second copy that drifted would write timestamps
/// one reader could not parse, and the ordering engine needs the same format
/// when it renormalises rows in any table.
/// </remarks>
internal static class Timestamps
{
    private const string RoundTrip = "O";

    public static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString(RoundTrip, CultureInfo.InvariantCulture);

    public static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
