using Noto.Core.Portability;
using Xunit;

namespace Noto.Core.Tests.Portability;

/// <summary>F15 (#15): when an automatic backup is due, what it is called, and which ones are kept.</summary>
public sealed class AutomaticBackupsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 9, 30, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 11, 11, 9, 30, 0)));

    [Fact]
    public void A_backup_is_due_with_none_yet_a_day_after_the_last_or_when_the_last_is_in_the_future()
    {
        Assert.True(AutomaticBackups.IsDue(null, Now));
        Assert.False(AutomaticBackups.IsDue(Now.AddHours(-23), Now));
        Assert.True(AutomaticBackups.IsDue(Now.AddDays(-1), Now));
        Assert.True(AutomaticBackups.IsDue(Now.AddDays(-3), Now));
        Assert.True(AutomaticBackups.IsDue(Now.AddHours(5), Now));   // the clock moved back
    }

    [Fact]
    public void Its_name_carries_its_time_and_reads_back()
    {
        string name = AutomaticBackups.FileName(Now);

        Assert.Equal("auto-2026-11-11 093000.notobackup", name);
        Assert.Equal(Now, AutomaticBackups.TakenAt(name));
    }

    [Theory]
    [InlineData("before-restore-2026-11-11 093000.notobackup")]   // the backup a restore takes: never Noto's to prune
    [InlineData("Noto backup 2026-11-11 0930.notobackup")]        // one the user saved
    [InlineData("auto-yesterday.notobackup")]
    [InlineData("auto-2026-11-11 093000.zip")]
    public void Only_its_own_names_are_automatic_backups(string name)
    {
        Assert.Null(AutomaticBackups.TakenAt(name));
    }

    [Fact]
    public void The_seven_newest_are_kept_and_nothing_else_is_ever_removed()
    {
        var names = Enumerable.Range(0, 10).Select(d => AutomaticBackups.FileName(Now.AddDays(-d))).ToList();
        names.Add("before-restore-2026-01-01 000000.notobackup");
        names.Add("notes.txt");

        IReadOnlyList<string> expired = AutomaticBackups.Expired(names);

        Assert.Equal([AutomaticBackups.FileName(Now.AddDays(-9)), AutomaticBackups.FileName(Now.AddDays(-8)), AutomaticBackups.FileName(Now.AddDays(-7))], expired);
        Assert.Empty(AutomaticBackups.Expired(names.Take(AutomaticBackups.Keep)));
    }
}
