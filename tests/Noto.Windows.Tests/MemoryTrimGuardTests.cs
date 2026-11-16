using System.Text.RegularExpressions;
using Noto.Platform.Windows;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// F22 (#160): a hidden Noto gives back the working set it is not using, only while hidden, and the idle measurement
/// starts after the start-up's backup check. What it measures is Invoke-Performance.ps1's idle.
/// </summary>
public sealed class MemoryTrimGuardTests
{
    private static readonly string App = File.ReadAllText(SourcePath("src", "Noto.Windows", "App.xaml.cs"));

    [Fact]
    public void Windows_accepts_the_trim_and_the_process_keeps_running()
    {
        Assert.True(ProcessMemory.Trim());
    }

    [Fact]
    public void Noto_trims_only_once_it_has_been_hidden_and_from_one_place()
    {
        string all = string.Join('\n', Directory.EnumerateFiles(Path.GetDirectoryName(SourcePath("src", "Noto.Windows", "App.xaml.cs"))!, "*.cs").Select(File.ReadAllText));
        Assert.Equal(1, Regex.Count(all, @"ProcessMemory\.Trim\(\)"));

        string trim = Regex.Match(App, @"private void TrimWhenHidden\(\)[\s\S]*?\n    }\n").Value;
        Assert.Contains("WindowActivation.PresenceOf(_windowHandle) == WorkspacePresence.Hidden", trim, StringComparison.Ordinal);
        Assert.True(trim.IndexOf("PresenceOf", StringComparison.Ordinal) < trim.IndexOf("ProcessMemory.Trim()", StringComparison.Ordinal));
        Assert.Contains("private static readonly TimeSpan TrimDelay = TimeSpan.FromSeconds(5);", App, StringComparison.Ordinal);

        // After a hide, a hidden start, and an automatic backup check.
        Assert.Contains("_coordinator.Hidden += (_, _) => TrimWhenHidden();", App, StringComparison.Ordinal);
        Assert.Matches(@"timer\.Start\(\);\s*// A backup reads the whole database[^\n]*\n\s*TrimWhenHidden\(\);", App);
    }

    [Fact]
    public void The_backup_check_restarts_its_timer_so_the_wait_counts_from_the_check()
    {
        Assert.Matches(@"timer\.Stop\(\);\s*timer\.Interval = await check\(\);\s*timer\.Start\(\);", App);
    }

    [Fact]
    public void The_backup_timer_waits_until_the_next_is_due_and_a_change_of_frequency_applies_at_once()
    {
        // #199: the timer and a changed setting check through one path; the check passes the chosen frequency, and the
        // wait it returns is Core's (AutomaticBackups.UntilNextCheck, unit tested): until the next is due, an hour at most.
        Assert.Contains("_backupTimer.Tick += async (_, _) => await CheckAutomaticBackup();", App, StringComparison.Ordinal);
        Assert.Matches(@"key == SettingKeys\.BackupAutomatic \|\| key == SettingKeys\.BackupFrequency\)\s*\{\s*_ = CheckAutomaticBackup\(\);", App);

        string check = Regex.Match(App, @"private static async Task<TimeSpan> BackupIfDue\([\s\S]*?\n    }\n").Value;
        Assert.Contains("return AutomaticBackups.LongestWait;", check, StringComparison.Ordinal);
        Assert.Contains("await portability.BackupIfDue(frequency)", check, StringComparison.Ordinal);
        Assert.Contains("return AutomaticBackups.UntilNextCheck(newest, TimeProvider.System.GetLocalNow(), frequency);", check, StringComparison.Ordinal);

        // Nothing due is a comparison with the clock, before any work is started.
        string portability = File.ReadAllText(SourcePath("src", "Noto.Windows", "DataPortability.cs"));
        string due = Regex.Match(portability, @"public async Task<bool> BackupIfDue\(BackupFrequency frequency\)[\s\S]*?\n    }\n").Value;
        int fast = due.IndexOf("NewestAutomaticBackup is { } known && !AutomaticBackups.IsDue(", StringComparison.Ordinal);
        Assert.True(fast > 0 && fast < due.IndexOf("await Run(", StringComparison.Ordinal), "A check with nothing due starts work.");
    }

    /// <summary>Walks up to the repository root; a guard that silently skips is worse than none.</summary>
    private static string SourcePath(params string[] parts)
    {
        string relative = Path.Join(parts);

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Join(directory.FullName, relative);

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Could not locate {relative} by walking up from {AppContext.BaseDirectory}");
    }
}
