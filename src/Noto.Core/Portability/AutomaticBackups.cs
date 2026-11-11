using System.Globalization;

namespace Noto.Core.Portability;

/// <summary>
/// When Noto backs itself up, what the backups are called and how many it keeps (F15, #15). The backups themselves are
/// ordinary Noto backups (ADR-020), restored like any other.
/// </summary>
/// <remarks>
/// <para>
/// <b>Once a day, the last seven kept.</b> A day is the smallest loss a user would notice; a week reaches back past a
/// mistake noticed late. Only Noto's own automatic backups are counted and pruned, by their name: a backup the user
/// saved, or the one taken before a restore, is never touched.
/// </para>
/// <para>
/// The time is in the name, so the schedule does not depend on file times, which a copy or a sync tool may change.
/// </para>
/// </remarks>
public static class AutomaticBackups
{
    /// <summary>How long after one automatic backup the next is due.</summary>
    public static TimeSpan Interval { get; } = TimeSpan.FromDays(1);

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

    /// <summary>
    /// Whether a backup is due: none yet, the newest a day old or more, or the newest in the future (the clock moved
    /// back), which would otherwise hold off every backup until the clock caught up.
    /// </summary>
    public static bool IsDue(DateTimeOffset? newest, DateTimeOffset now) =>
        newest is not { } last || now - last >= Interval || last > now;

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
