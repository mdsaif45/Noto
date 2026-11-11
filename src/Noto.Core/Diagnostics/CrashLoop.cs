using System.Globalization;

namespace Noto.Core.Diagnostics;

/// <summary>
/// One failure Noto recorded as it ended (F16, #10): when, and how long after its start. Times only, never what Noto
/// was doing (principle 10).
/// </summary>
public readonly record struct Failure(DateTimeOffset At, TimeSpan AfterStart);

/// <summary>
/// Whether Noto is failing at every start (F16, #10): then it stops instead of failing again.
/// </summary>
/// <remarks>
/// <para>
/// <b>Three failures, each soon after a start, within half an hour.</b> A failure a minute or more into a session is a
/// failure, not a loop: the start itself worked. Half an hour holds a run of restarts a user or Windows' sign-in would
/// make, and lets yesterday's failures go.
/// </para>
/// <para>
/// Recorded one line per failure, <c>&lt;round-trip time&gt; &lt;seconds after start&gt;</c>; a line that is not one is
/// skipped, so a damaged record never stops Noto from starting.
/// </para>
/// </remarks>
public static class CrashLoop
{
    /// <summary>A failure this soon after its start is a failure to start.</summary>
    public static TimeSpan Early { get; } = TimeSpan.FromSeconds(60);

    /// <summary>How far back failures count.</summary>
    public static TimeSpan Within { get; } = TimeSpan.FromMinutes(30);

    /// <summary>How many early failures within <see cref="Within"/> make a loop.</summary>
    public const int Threshold = 3;

    /// <summary>How many failures the record keeps: enough for <see cref="Threshold"/>, never growing.</summary>
    public const int Kept = 10;

    /// <summary>Whether <paramref name="failures"/> are a loop at <paramref name="now"/>.</summary>
    public static bool IsLoop(IEnumerable<Failure> failures, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(failures);

        return failures.Count(f => f.AfterStart <= Early && f.At <= now && now - f.At <= Within) >= Threshold;
    }

    /// <summary>A failure as its line in the record.</summary>
    public static string Line(Failure failure) =>
        failure.At.ToString("O", CultureInfo.InvariantCulture) + " " + failure.AfterStart.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>The failures in a record's lines; anything that is not one is skipped.</summary>
    public static IReadOnlyList<Failure> Parse(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var failures = new List<Failure>();

        foreach (string line in lines)
        {
            string[] parts = line.Trim().Split(' ');

            if (parts.Length == 2
                && DateTimeOffset.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset at)
                && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)
                && double.IsFinite(seconds) && seconds >= 0)
            {
                failures.Add(new Failure(at, TimeSpan.FromSeconds(seconds)));
            }
        }

        return failures;
    }
}
