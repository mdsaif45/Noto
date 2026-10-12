using System.Text.RegularExpressions;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// #7 in the composition root: the log exists before the database and settings so their failures reach it, both are
/// given it, and its level follows the setting. Checked in the source, as the other wiring guards are, because this
/// project cannot reference the application; the runtime check is a real start against an isolated data root.
/// </summary>
public sealed class LoggingWiringGuardTests
{
    private static readonly string App = Regex.Replace(File.ReadAllText(SourcePath("src", "Noto.Windows", "App.xaml.cs")), @"//.*", string.Empty);

    [Fact]
    public void The_log_is_created_before_the_database_and_given_to_it_and_to_settings()
    {
        int log = App.IndexOf("new RollingFileLog(paths.LogsDirectory)", StringComparison.Ordinal);
        int database = App.IndexOf("new NotoDatabase(paths.DatabaseFile, log)", StringComparison.Ordinal);
        int settings = App.IndexOf("new SqliteSettingsStore(database, log)", StringComparison.Ordinal);

        Assert.True(log >= 0 && database > log && settings > database, $"log={log} database={database} settings={settings}");
    }

    [Fact]
    public void The_level_follows_the_setting_once_settings_are_read()
    {
        // Once, as soon as settings are read — not only inside the change handler, which never runs at startup.
        int load = App.IndexOf("settings.Load();", StringComparison.Ordinal);
        int subscribe = App.IndexOf("settings.SettingChanged +=", load, StringComparison.Ordinal);
        Assert.True(load >= 0 && subscribe > load, $"load={load} subscribe={subscribe}");
        Assert.Contains("log.Level = settings.Read(SettingKeys.DiagnosticsLogLevel);", App[load..subscribe], StringComparison.Ordinal);

        // And again whenever the setting changes.
        string handler = App[subscribe..App.IndexOf("};", subscribe, StringComparison.Ordinal)];
        Assert.Contains("if (e.Key == SettingKeys.DiagnosticsLogLevel)", handler, StringComparison.Ordinal);
        Assert.Contains("log.Level = settings.Read(SettingKeys.DiagnosticsLogLevel);", handler, StringComparison.Ordinal);
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
