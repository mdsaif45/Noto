using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Noto.Core.Diagnostics;
using Noto.Core.Settings;
using Noto.Core.Storage;
using Noto.Infrastructure.Diagnostics;
using Noto.Infrastructure.Storage;
using Xunit;

namespace Noto.Infrastructure.Tests.Diagnostics;

/// <summary>
/// Noto's log (#7): JSON lines in the logs folder, bounded in size and number, never the reason Noto fails, and
/// unable to record note content, setting values, exception messages, stack traces or absolute paths.
/// </summary>
public sealed partial class RollingFileLogTests : IDisposable
{
    private const string Secret = "SECRET-NOTE-TEXT-4711";

    private readonly string _root = Path.Join(Path.GetTempPath(), "noto-log-tests-" + Guid.NewGuid().ToString("N")[..8]);

    private string Logs => Path.Join(_root, "logs");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Each_event_is_one_json_line_in_the_logs_folder()
    {
        var log = new RollingFileLog(Logs);

        log.MigrationStarting(1, 2, 1);
        log.MigrationApplied(2);
        log.MigrationCompleted(2);

        var lines = AllLines();
        Assert.Equal(3, lines.Count);
        Assert.All(lines, line => Assert.True(JsonDocument.Parse(line).RootElement.TryGetProperty("time", out _)));

        using var applied = JsonDocument.Parse(lines[1]);
        Assert.Equal("MigrationApplied", applied.RootElement.GetProperty("event").GetString());
        Assert.Equal("Information", applied.RootElement.GetProperty("level").GetString());
        Assert.Equal(2, applied.RootElement.GetProperty("version").GetInt32());
        Assert.Equal(["event", "level", "time", "version"], applied.RootElement.EnumerateObject().Select(p => p.Name).Order());
    }

    [Fact]
    public void An_exceptions_message_and_stack_trace_never_reach_the_file()
    {
        var log = new RollingFileLog(Logs);
        Exception thrown;

        try
        {
            ThrowFromAFrameNamedLeakyFrame4711();
            return;
        }
        catch (InvalidOperationException ex)
        {
            thrown = ex;
        }

        log.MigrationFailed(3, ExceptionSummary.From(thrown));
        log.SettingsUnreadable(ExceptionSummary.From(new IOException(@"C:\Users\SecretUser\noto.db is locked", thrown)));
        log.SettingSubscriberFailed(SettingKeys.WorkspaceEdge, ExceptionSummary.From(thrown));

        string text = AllText();
        Assert.Contains("System.InvalidOperationException", text, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, text, StringComparison.Ordinal);
        Assert.DoesNotContain("LeakyFrame4711", text, StringComparison.Ordinal);
        Assert.DoesNotContain("SecretUser", text, StringComparison.Ordinal);
        Assert.DoesNotMatch(AbsolutePath(), text);
    }

    [Fact]
    public void A_location_is_recorded_relative_to_the_data_root_or_as_outside_never_absolute()
    {
        var log = new RollingFileLog(Logs);

        log.SafetyCopyCreated(1, DataRootPath.Of(_root, Path.Join(_root, "noto-premigration-v1-20261013T101500.db")));
        log.SafetyCopyCreated(1, DataRootPath.Of(_root, @"C:\Users\SecretUser\Documents\notes.db"));
        log.SafetyCopyCreated(1, DataRootPath.Of(_root, Path.Join(_root, @"..\SecretUser.db")));

        var copies = AllLines().Select(l => JsonDocument.Parse(l).RootElement.GetProperty("copy").GetString()).ToList();
        Assert.Equal(["noto-premigration-v1-20261013T101500.db", "<outside>", "<outside>"], copies);

        string text = AllText();
        Assert.DoesNotContain("SecretUser", text, StringComparison.Ordinal);
        Assert.DoesNotContain(_root, text.Replace(@"\\", @"\", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(AbsolutePath(), text);
    }

    [Fact]
    public void Failure_events_record_the_reason_and_the_exception_types_never_the_message()
    {
        var log = new RollingFileLog(Logs);
        var thrown = new InvalidOperationException(Secret, new IOException(@"C:\Users\SecretUser\noto.db"));

        log.StartupFailed(StorageFailure.Corrupt, ExceptionSummary.From(new StorageException(StorageFailure.Corrupt, Secret, thrown)));
        log.UnhandledException(UnhandledSource.UiThread, ExceptionSummary.From(thrown));
        log.EmergencySave(saved: false);
        log.EmergencySaveFailed(ExceptionSummary.From(thrown));
        log.UnobservedTaskException(ExceptionSummary.From(new AggregateException(thrown)));

        var lines = AllLines().Select(l => JsonDocument.Parse(l).RootElement).ToList();
        Assert.Equal(
            ["StartupFailed", "UnhandledException", "EmergencySave", "EmergencySaveFailed", "UnobservedTaskException"],
            lines.Select(l => l.GetProperty("event").GetString()));
        Assert.Equal(["Error", "Error", "Warning", "Error", "Warning"], lines.Select(l => l.GetProperty("level").GetString()));
        Assert.Equal("Corrupt", lines[0].GetProperty("reason").GetString());
        Assert.Equal("UiThread", lines[1].GetProperty("source").GetString());
        Assert.False(lines[2].GetProperty("saved").GetBoolean());

        string text = AllText();
        Assert.DoesNotContain(Secret, text, StringComparison.Ordinal);
        Assert.DoesNotContain("SecretUser", text, StringComparison.Ordinal);
        Assert.DoesNotMatch(AbsolutePath(), text);
    }

    [Fact]
    public void A_setting_is_recorded_by_its_key_reason_and_kind()
    {
        var log = new RollingFileLog(Logs);

        log.SettingFellBackToDefault(SettingKeys.WorkspaceEdge, SettingFallbackReason.Corrupt);

        using var line = JsonDocument.Parse(Assert.Single(AllLines()));
        Assert.Equal("workspace.edge", line.RootElement.GetProperty("key").GetString());
        Assert.Equal("Corrupt", line.RootElement.GetProperty("reason").GetString());
        Assert.Equal("Warning", line.RootElement.GetProperty("level").GetString());
        Assert.False(line.RootElement.TryGetProperty("scoped", out _));
    }

    [Fact]
    public void A_family_members_scope_never_reaches_the_file_only_its_declared_name()
    {
        // A scope is opaque: a display identity, or the name of a row someone edited by hand.
        const string Scope = @"C:\Users\SecretUser\SECRET-NOTE-TEXT-4711";
        var log = new RollingFileLog(Logs);

        log.SettingFellBackToDefault(SettingKeys.WorkspaceWidthByScope.For(Scope), SettingFallbackReason.Invalid);
        log.SettingSubscriberFailed(SettingKeys.WorkspaceWidthByScope.For(Scope), ExceptionSummary.From(new InvalidOperationException()));

        var lines = AllLines();
        Assert.Equal(2, lines.Count);
        Assert.All(lines, l =>
        {
            using var line = JsonDocument.Parse(l);
            Assert.Equal("workspace.width", line.RootElement.GetProperty("key").GetString());
            Assert.True(line.RootElement.GetProperty("scoped").GetBoolean());
        });
        string text = string.Join('\n', lines);
        Assert.DoesNotContain("SECRET", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Users", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_is_closed_before_it_passes_its_size_and_a_new_one_started()
    {
        var log = new RollingFileLog(Logs, maxFileBytes: 400, maxFiles: 100);

        for (int i = 0; i < 30; i++)
        {
            log.MigrationApplied(i);
        }

        var files = Directory.GetFiles(Logs, "noto-*.log");
        Assert.True(files.Length > 1, $"expected several files, found {files.Length}");
        Assert.All(files, f => Assert.True(new FileInfo(f).Length <= 400, $"{f} is {new FileInfo(f).Length} bytes"));
        Assert.Equal(30, AllLines().Count);
    }

    [Fact]
    public void Only_the_newest_files_are_kept_and_the_newest_events_survive()
    {
        var log = new RollingFileLog(Logs, maxFileBytes: 250, maxFiles: 3);

        for (int i = 0; i < 60; i++)
        {
            log.MigrationApplied(i);
        }

        Assert.Equal(3, Directory.GetFiles(Logs, "noto-*.log").Length);

        var versions = AllLines().Select(l => JsonDocument.Parse(l).RootElement.GetProperty("version").GetInt32()).ToList();
        Assert.Contains(59, versions);
        Assert.DoesNotContain(0, versions);
        Assert.Equal(versions.Order(), versions);
    }

    [Fact]
    public void Events_below_the_level_are_not_written_and_the_level_can_change_at_runtime()
    {
        var log = new RollingFileLog(Logs);
        Assert.Equal(LogLevel.Information, log.Level);

        log.Level = LogLevel.Warning;
        log.MigrationApplied(1);
        log.SettingFellBackToDefault(SettingKeys.WorkspaceEdge, SettingFallbackReason.Invalid);

        log.Level = LogLevel.Error;
        log.SettingFellBackToDefault(SettingKeys.WorkspaceEdge, SettingFallbackReason.Corrupt);
        log.MigrationFailed(2, ExceptionSummary.From(new InvalidOperationException()));

        log.Level = LogLevel.Debug;
        log.MigrationCompleted(3);

        var events = AllLines().Select(l => JsonDocument.Parse(l).RootElement.GetProperty("event").GetString()).ToList();
        Assert.Equal(["SettingFellBackToDefault", "MigrationFailed", "MigrationCompleted"], events);
    }

    [Fact]
    public void A_folder_that_cannot_be_created_drops_the_event_without_throwing()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Logs, "a file where the folder should be");
        var log = new RollingFileLog(Logs);

        var error = Record.Exception(() =>
        {
            log.MigrationCompleted(1);
            log.MigrationFailed(1, ExceptionSummary.From(new InvalidOperationException(Secret)));
        });

        Assert.Null(error);
        Assert.Equal("a file where the folder should be", File.ReadAllText(Logs));
    }

    [Fact]
    public void Preparing_the_data_root_does_not_depend_on_the_logs_folder()
    {
        // A logs folder that cannot be created costs the log, never Noto's start (measured: before this, a file
        // named "logs" in the data root made EnsureCreated throw and the app exit at startup).
        Directory.CreateDirectory(_root);
        File.WriteAllText(Logs, "a file where the folder should be");

        Assert.Null(Record.Exception(() => NotoStoragePaths.At(_root).EnsureCreated()));
        Assert.True(Directory.Exists(NotoStoragePaths.At(_root).BackupsDirectory));
    }

    [Fact]
    public void A_file_locked_by_another_process_drops_the_event_without_throwing()
    {
        var log = new RollingFileLog(Logs);
        log.MigrationCompleted(1);
        string file = Assert.Single(Directory.GetFiles(Logs));

        using (new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Null(Record.Exception(() => log.MigrationCompleted(2)));
        }

        log.MigrationCompleted(3);
        var versions = AllLines().Select(l => JsonDocument.Parse(l).RootElement.GetProperty("version").GetInt32()).ToList();
        Assert.Equal([1, 3], versions);
    }

    [Fact]
    public void Concurrent_writers_never_interleave_a_line()
    {
        var log = new RollingFileLog(Logs, maxFileBytes: 64 * 1024, maxFiles: 100);

        Parallel.For(0, 8, writer =>
        {
            for (int i = 0; i < 200; i++)
            {
                log.MigrationApplied((writer * 1000) + i);
            }
        });

        var lines = AllLines();
        Assert.Equal(1600, lines.Count);
        Assert.Equal(1600, lines.Select(l => JsonDocument.Parse(l).RootElement.GetProperty("version").GetInt32()).Distinct().Count());
    }

    [Fact]
    public void A_new_log_continues_the_newest_file_while_it_has_room()
    {
        new RollingFileLog(Logs).MigrationCompleted(1);
        new RollingFileLog(Logs).MigrationCompleted(2);

        Assert.Single(Directory.GetFiles(Logs, "noto-*.log"));
        Assert.Equal(2, AllLines().Count);
    }

    /// <summary>A drive-letter path as JSON writes it (the backslash escaped).</summary>
    [GeneratedRegex(@"[A-Za-z]:\\\\")]
    private static partial Regex AbsolutePath();

    private List<string> AllLines() =>
        Directory.GetFiles(Logs, "noto-*.log").Order(StringComparer.Ordinal)
            .SelectMany(File.ReadAllLines).Where(l => l.Length > 0).ToList();

    private string AllText() => string.Join('\n', AllLines());

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowFromAFrameNamedLeakyFrame4711() => throw new InvalidOperationException(Secret);
}
