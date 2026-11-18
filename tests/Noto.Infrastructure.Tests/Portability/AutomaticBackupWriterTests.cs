using Noto.Core.Portability;
using Noto.Infrastructure.Portability;
using Xunit;

namespace Noto.Infrastructure.Tests.Portability;

/// <summary>
/// Automatic backups on a real database and real folders (F15, #15; #233): into the data root's backups or a folder the
/// user chose, the seven newest kept there and nothing else touched, and a chosen folder that is gone or refuses the
/// backup falling back to the data root's backups, never losing it.
/// </summary>
public sealed class AutomaticBackupWriterTests : IDisposable
{
    private readonly Workspace _workspace = new();
    private readonly DateTimeOffset _now = Workspace.Time.GetLocalNow();

    public void Dispose() => _workspace.Dispose();

    private string Default => _workspace.Paths.BackupsDirectory;

    private AutomaticBackupCheck Check(string folder, BackupFrequency frequency = BackupFrequency.Daily) =>
        AutomaticBackupWriter.BackupIfDue(_workspace.Database, _workspace.Paths, folder, frequency, "test", Workspace.Time);

    private static string[] Files(string folder) =>
        [.. Directory.EnumerateFiles(folder).Select(f => Path.GetFileName(f)!).Order(StringComparer.Ordinal)];

    private static void Seed(string folder, params string[] names)
    {
        foreach (string name in names)
        {
            File.WriteAllText(Path.Join(folder, name), "seeded");
        }
    }

    [Fact]
    public void With_no_folder_chosen_it_backs_up_into_the_data_roots_backups_as_it_always_has()
    {
        AutomaticBackupCheck check = Check(string.Empty);

        Assert.Equal(new AutomaticBackupCheck(true, _now, false), check);
        Assert.Equal([AutomaticBackups.FileName(_now)], Files(Default));
    }

    [Fact]
    public void Into_a_chosen_folder_it_writes_and_prunes_there_only_and_touches_nothing_else()
    {
        string chosen = _workspace.Scratch("onedrive");
        string[] older = [.. Enumerable.Range(1, 8).Select(d => AutomaticBackups.FileName(_now.AddDays(-d)))];
        string[] others = ["notes.txt", "Noto backup 2026-11-01 0930.notobackup", "auto-not-a-time.notobackup"];
        Seed(chosen, [.. older, .. others]);
        Directory.CreateDirectory(Path.Join(chosen, "nested"));
        Seed(Path.Join(chosen, "nested"), AutomaticBackups.FileName(_now.AddDays(-30)));
        string inDefault = AutomaticBackups.FileName(_now.AddDays(-40));
        Seed(Default, inDefault);

        AutomaticBackupCheck check = Check(chosen);

        // One new backup there, a real one; the seven newest automatic backups kept, the two oldest removed.
        Assert.Equal(new AutomaticBackupCheck(true, _now, false), check);
        string written = Path.Join(chosen, AutomaticBackups.FileName(_now));
        Assert.True(new FileInfo(written).Length > "seeded".Length);
        Assert.Equal(older.Take(6).Append(AutomaticBackups.FileName(_now)).Concat(others).Order(StringComparer.Ordinal), Files(chosen));

        // Nothing else in the folder, a folder inside it, or the data root's backups was touched.
        Assert.Single(Files(Path.Join(chosen, "nested")));
        Assert.Equal([inDefault], Files(Default));
    }

    [Fact]
    public void A_chosen_folder_with_no_automatic_backups_has_one_due_whatever_the_data_roots_backups_hold()
    {
        // The folder is what is read: a fresh backup in the data root's backups does not hold one off in the new folder.
        string chosen = _workspace.Scratch("new");
        Directory.CreateDirectory(Default);
        Seed(Default, AutomaticBackups.FileName(_now.AddMinutes(-5)));

        Assert.True(Check(chosen).Written);
        Assert.Equal([AutomaticBackups.FileName(_now)], Files(chosen));
    }

    [Fact]
    public void A_chosen_folder_with_a_recent_backup_has_none_due_and_says_when_that_was()
    {
        string chosen = _workspace.Scratch("recent");
        string recent = AutomaticBackups.FileName(_now.AddHours(-2));
        Seed(chosen, recent);

        AutomaticBackupCheck check = Check(chosen);

        Assert.Equal(new AutomaticBackupCheck(false, _now.AddHours(-2), false), check);
        Assert.Equal([recent], Files(chosen));
    }

    [Fact]
    public void A_chosen_folder_that_is_gone_falls_back_to_the_data_roots_backups_and_is_not_made_again()
    {
        string gone = Path.Join(_workspace.Scratch("unplugged"), "Noto backups");

        AutomaticBackupCheck check = Check(gone);

        Assert.Equal(new AutomaticBackupCheck(true, _now, true), check);
        Assert.Equal([AutomaticBackups.FileName(_now)], Files(Default));
        Assert.False(Directory.Exists(gone));
    }

    [Fact]
    public void A_chosen_folder_that_is_gone_is_due_by_the_backups_it_fell_back_to()
    {
        // A day's backup already fell back an hour ago: the next start does not take another.
        string gone = Path.Join(_workspace.Scratch("unplugged"), "Noto backups");
        Directory.CreateDirectory(Default);
        Seed(Default, AutomaticBackups.FileName(_now.AddHours(-1)));

        Assert.Equal(new AutomaticBackupCheck(false, _now.AddHours(-1), false), Check(gone));
    }

    [Fact]
    public void A_chosen_folder_that_refuses_the_backup_falls_back_to_the_data_roots_backups()
    {
        // The backup's own work file cannot be made there (a folder already has its name), as on a folder that is read only.
        string refusing = _workspace.Scratch("refusing");
        Directory.CreateDirectory(Path.Join(refusing, AutomaticBackups.FileName(_now) + ".partial"));
        string older = AutomaticBackups.FileName(_now.AddDays(-2));
        Seed(refusing, older);

        AutomaticBackupCheck check = Check(refusing);

        Assert.Equal(new AutomaticBackupCheck(true, _now, true), check);
        Assert.Equal([AutomaticBackups.FileName(_now)], Files(Default));
        Assert.Equal([older], Files(refusing));
    }
}
