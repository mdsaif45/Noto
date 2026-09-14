namespace Noto.Core;

/// <summary>
/// The current instant, in UTC.
/// </summary>
/// <remarks>
/// An interface rather than <see cref="DateTimeOffset.UtcNow"/> at the call
/// site, because the <c>UpdatedAt</c> contract is testable only if time can be
/// controlled: "all rows in one transaction share a single timestamp" cannot be
/// asserted against a clock that advances between two reads
/// (core-note-engine-contract.md §4).
/// </remarks>
public interface IClock
{
    /// <summary>The current UTC instant.</summary>
    DateTimeOffset UtcNow { get; }
}

/// <summary>The real clock.</summary>
public sealed class SystemClock : IClock
{
    public static SystemClock Instance { get; } = new();

    private SystemClock()
    {
    }

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
