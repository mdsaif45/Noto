using System.Globalization;

namespace Noto.Core.Portability;

/// <summary>
/// When Noto backs itself up, what the backups are called and how many it keeps (F15, #15). The backups themselves are
/// ordinary Noto backups (ADR-020), restored like any other.
/// </summary>
/// <remarks>
/// <para>
/// <b>Once a day by default, the last seven kept.</b> A day is the smallest loss a user would notice; a week reaches
/// back past a mistake noticed late. Only Noto's own automatic backups are counted and pruned, by their name: a backup
/// the user saved, or the one taken before a restore, is never touched.
/// </para>
/// <para>
/// <b>Or every 45, 30 or 15 minutes</b> (#199), SideNotes' three intervals, for a user who writes a lot in a day. The
/// day stays the default: it is what Noto has always done, and seven backups a day apart reach back a week, where seven
/// 15 minutes apart reach back less than two hours. The seven kept are the same either way.
/// </para>
/// <para>
/// The time is in the name, so the schedule does not depend on file times, which a copy or a sync tool may change.
/// </para>
/// </remarks>
public static class AutomaticBackups
{
    /// <summary>The longest the timer waits between checks: an hour, as it always has.</summary>
    public static TimeSpan LongestWait { get; } = TimeSpan.FromHours(1);

    /// <summary>The shortest the timer waits between checks, so a check that could not run is not tried again at once.</summary>
    public static TimeSpan ShortestWait { get; } = TimeSpan.FromMinutes(1);

    /// <summary>How far past the moment a backup falls due the timer fires, so a timer a moment early still finds it due.</summary>
    private static readonly TimeSpan Margin = TimeSpan.FromSeconds(1);

    /// <summary>How many automatic backups are kept; older ones are removed once a new one is written.</summary>
    public const int Keep = 7;

    private const string Prefix = "auto-";
    private const string TimeFormat = "yyyy-MM-dd HHmmss";

    /// <summary>The name of an automatic backup taken at <paramref name="now"/>, in local time as a user reads it.</summary>
    public static string FileName(DateTimeOffset now) =>
        Prefix + now.ToLocalTime().ToString(TimeFormat, CultureInfo.InvariantCulture) + BackupManifest.Extension;

    /// <summary>When an automatic backup was taken, from its name; <see langword="null"/> for any other file.</summary>
    public static DateTimeOffset? TakenAt(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        if (!fileName.StartsWith(Prefix, StringComparison.Ordinal) || !fileName.EndsWith(BackupManifest.Extension, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string stamp = fileName[Prefix.Length..^BackupManifest.Extension.Length];
        return DateTime.TryParseExact(stamp, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime local)
            ? new DateTimeOffset(local)
            : null;
    }

    /// <summary>How long after one automatic backup the next is due, at <paramref name="frequency"/>.</summary>
    public static TimeSpan IntervalOf(BackupFrequency frequency) => frequency switch
    {
        BackupFrequency.Every45Minutes => TimeSpan.FromMinutes(45),
        BackupFrequency.Every30Minutes => TimeSpan.FromMinutes(30),
        BackupFrequency.Every15Minutes => TimeSpan.FromMinutes(15),
        _ => TimeSpan.FromDays(1),
    };

    /// <summary>
    /// Whether a backup is due: none yet, the newest as old as the interval or older, or the newest in the future (the
    /// clock moved back), which would otherwise hold off every backup until the clock caught up.
    /// </summary>
    public static bool IsDue(DateTimeOffset? newest, DateTimeOffset now, BackupFrequency frequency) =>
        newest is not { } last || now - last >= IntervalOf(frequency) || last > now;

    /// <summary>
    /// How long the timer waits before the next check (#199): until the next backup is due, a moment past it; at least
    /// <see cref="ShortestWait"/>, so a backup that is due but could not be taken (other work was running) waits a little;
    /// at most <see cref="LongestWait"/>, so a day's backup is still looked for every hour and a changed clock is noticed.
    /// A check with nothing due is then rare, and only a comparison.
    /// </summary>
    public static TimeSpan UntilNextCheck(DateTimeOffset? newest, DateTimeOffset now, BackupFrequency frequency)
    {
        TimeSpan until = IsDue(newest, now, frequency) ? TimeSpan.Zero : newest!.Value + IntervalOf(frequency) - now + Margin;
        return until < ShortestWait ? ShortestWait : until > LongestWait ? LongestWait : until;
    }

    /// <summary>The backups to remove so that only the <see cref="Keep"/> newest remain, oldest first.</summary>
    public static IReadOnlyList<string> Expired(IEnumerable<string> fileNames)
    {
        ArgumentNullException.ThrowIfNull(fileNames);

        return [.. fileNames
            .Select(name => (Name: name, Taken: TakenAt(name)))
            .Where(b => b.Taken is not null)
            .OrderByDescending(b => b.Taken)
            .Skip(Keep)
            .OrderBy(b => b.Taken)
            .Select(b => b.Name)];
    }
}
