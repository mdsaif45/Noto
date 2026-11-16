using Noto.Core.Portability;
using Xunit;

namespace Noto.Core.Tests.Portability;

/// <summary>F15 (#15), #199: when an automatic backup is due at each frequency, how long the timer waits for it, what it is called, and which ones are kept.</summary>
public sealed class AutomaticBackupsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 9, 30, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 11, 11, 9, 30, 0)));

    [Fact]
    public void A_backup_is_due_with_none_yet_a_day_after_the_last_or_when_the_last_is_in_the_future()
    {
        Assert.True(AutomaticBackups.IsDue(null, Now, BackupFrequency.Daily));
        Assert.False(AutomaticBackups.IsDue(Now.AddHours(-23), Now, BackupFrequency.Daily));
        Assert.True(AutomaticBackups.IsDue(Now.AddDays(-1), Now, BackupFrequency.Daily));
        Assert.True(AutomaticBackups.IsDue(Now.AddDays(-3), Now, BackupFrequency.Daily));
        Assert.True(AutomaticBackups.IsDue(Now.AddHours(5), Now, BackupFrequency.Daily));   // the clock moved back
    }

    [Theory]
    [InlineData(BackupFrequency.Every45Minutes, 45)]
    [InlineData(BackupFrequency.Every30Minutes, 30)]
    [InlineData(BackupFrequency.Every15Minutes, 15)]
    public void At_a_frequency_in_minutes_a_backup_is_due_once_the_newest_is_that_old(BackupFrequency frequency, int minutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(minutes), AutomaticBackups.IntervalOf(frequency));
        Assert.False(AutomaticBackups.IsDue(Now.AddMinutes(-minutes).AddSeconds(1), Now, frequency));
        Assert.True(AutomaticBackups.IsDue(Now.AddMinutes(-minutes), Now, frequency));
        Assert.True(AutomaticBackups.IsDue(null, Now, frequency));
        Assert.True(AutomaticBackups.IsDue(Now.AddMinutes(1), Now, frequency));   // the clock moved back
    }

    [Fact]
    public void A_day_is_the_default_and_is_unchanged()
    {
        Assert.Equal(default(BackupFrequency), BackupFrequency.Daily);
        Assert.Equal(BackupFrequency.Daily, Noto.Core.Settings.SettingKeys.BackupFrequency.Default);
        Assert.Equal(TimeSpan.FromDays(1), AutomaticBackups.IntervalOf(BackupFrequency.Daily));
    }

    [Fact]
    public void The_timer_waits_until_the_next_is_due_and_a_moment_more()
    {
        // A fake clock: the newest backup was taken at Now, and time moves on from there.
        DateTimeOffset taken = Now;

        Assert.Equal(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(1), AutomaticBackups.UntilNextCheck(taken, taken, BackupFrequency.Every15Minutes));
        Assert.Equal(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1), AutomaticBackups.UntilNextCheck(taken, taken.AddMinutes(10), BackupFrequency.Every15Minutes));
        Assert.Equal(TimeSpan.FromMinutes(45) + TimeSpan.FromSeconds(1), AutomaticBackups.UntilNextCheck(taken, taken, BackupFrequency.Every45Minutes));

        // Fired a moment early, it is not due yet, and the timer fires again a minute on (it then is).
        DateTimeOffset early = taken.AddMinutes(15).AddMilliseconds(-20);
        Assert.False(AutomaticBackups.IsDue(taken, early, BackupFrequency.Every15Minutes));
        Assert.True(AutomaticBackups.IsDue(taken, early + AutomaticBackups.UntilNextCheck(taken, early, BackupFrequency.Every15Minutes), BackupFrequency.Every15Minutes));
    }

    [Fact]
    public void The_timer_waits_an_hour_at_most_and_a_minute_at_least()
    {
        // A day's backup is still looked for every hour, as before (#160).
        Assert.Equal(TimeSpan.FromHours(1), AutomaticBackups.UntilNextCheck(Now, Now, BackupFrequency.Daily));
        Assert.Equal(TimeSpan.FromHours(1), AutomaticBackups.UntilNextCheck(Now.AddHours(-20), Now, BackupFrequency.Daily));

        // Due but not taken (other work was running), none yet, or the clock moved back: a minute.
        Assert.Equal(TimeSpan.FromMinutes(1), AutomaticBackups.UntilNextCheck(Now.AddDays(-2), Now, BackupFrequency.Daily));
        Assert.Equal(TimeSpan.FromMinutes(1), AutomaticBackups.UntilNextCheck(null, Now, BackupFrequency.Every15Minutes));
        Assert.Equal(TimeSpan.FromMinutes(1), AutomaticBackups.UntilNextCheck(Now.AddHours(1), Now, BackupFrequency.Every15Minutes));
        Assert.Equal(TimeSpan.FromMinutes(1), AutomaticBackups.UntilNextCheck(Now.AddMinutes(-14).AddSeconds(-30), Now, BackupFrequency.Every15Minutes));
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
