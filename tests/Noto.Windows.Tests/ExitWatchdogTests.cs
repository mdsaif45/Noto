using System.Diagnostics;
using System.Text.RegularExpressions;
using Noto.Platform.Windows;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// The process ends, however its teardown behaves (ADR-014): a process that will not end is ended by the watchdog
/// within its grace, with exit code 0, and the overrun is recorded. Proved on a real child process that blocks.
/// </summary>
public sealed class ExitWatchdogTests
{
    [Fact]
    public void A_process_that_will_not_end_is_ended_within_the_grace_and_the_overrun_recorded()
    {
        string marker = Path.Join(Path.GetTempPath(), $"noto-watchdog-{Guid.NewGuid():N}.txt");
        string assembly = typeof(ExitWatchdog).Assembly.Location;

        // The child arms the watchdog, then blocks its main (foreground) thread for 30 s: without the watchdog it
        // would still be running long after the test's bound. The overrun callback is compiled code, not a script
        // block: the watchdog's thread has no PowerShell runspace to run one.
        string script = $"Add-Type -Path '{assembly}'; "
            + $"Add-Type -TypeDefinition 'public static class Overran {{ public static void Record() {{ System.IO.File.WriteAllText(@\"{marker}\", \"overran\"); }} }}'; "
            + "[Noto.Platform.Windows.ExitWatchdog]::Arm([TimeSpan]::FromMilliseconds(400), [Overran].GetMethod('Record').CreateDelegate([Action])); "
            + "Start-Sleep -Seconds 30";
        var start = new ProcessStartInfo("pwsh", ["-NoProfile", "-NonInteractive", "-Command", script])
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        try
        {
            using Process child = Process.Start(start)!;
            var clock = Stopwatch.StartNew();
            bool ended = child.WaitForExit(15_000);

            if (!ended)
            {
                child.Kill();
            }

            Assert.True(ended, "the watchdog did not end the process");
            Assert.Equal(0, child.ExitCode);
            Assert.InRange(clock.Elapsed, TimeSpan.FromMilliseconds(400), TimeSpan.FromSeconds(15));
            Assert.Equal("overran", File.ReadAllText(marker));
        }
        finally
        {
            File.Delete(marker);
        }
    }

    [Fact]
    public void The_watchdog_is_armed_when_the_window_closes_after_everything_is_released()
    {
        string app = File.ReadAllText(SourcePath("src", "Noto.Windows", "App.xaml.cs"));

        Assert.Matches(
            @"_window\.Closed \+= \(_, _\) =>\s*\{\s*ReleaseResources\(\);\s*ExitWatchdog\.Arm\(ShutdownGrace, \(\) => _appLog\?\.ShutdownOverran\(\(int\)ShutdownGrace\.TotalMilliseconds\)\);\s*\};",
            Regex.Replace(app, @"^\s*//.*$", string.Empty, RegexOptions.Multiline));
        Assert.Contains("ShutdownGrace = TimeSpan.FromSeconds(5);", app, StringComparison.Ordinal);
        Assert.Equal(1, Regex.Count(app, @"ExitWatchdog\.Arm\("));
    }

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
