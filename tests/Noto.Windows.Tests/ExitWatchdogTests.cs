using System.Diagnostics;
using System.Text.RegularExpressions;
using Noto.Platform.Windows;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// The bounded-shutdown safeguard (ADR-017 §10): after its grace the watchdog records the overrun, then ends the
/// process; the end runs even if recording fails. The clock runs here on its own thread, as in Noto, with the end
/// observed instead of performed; that the end Noto uses kills its own process is guarded below.
/// </summary>
public sealed class ExitWatchdogTests
{
    [Fact]
    public void After_the_grace_the_overrun_is_recorded_then_the_process_ended()
    {
        var steps = new System.Collections.Concurrent.ConcurrentQueue<string>();
        using var ended = new ManualResetEventSlim();
        var clock = Stopwatch.StartNew();

        ExitWatchdog.Arm(TimeSpan.FromMilliseconds(300), () => steps.Enqueue("overran"), () =>
        {
            steps.Enqueue("ended");
            ended.Set();
        });

        Assert.True(ended.Wait(TimeSpan.FromSeconds(10)), "the watchdog never ended the process");
        Assert.True(clock.Elapsed >= TimeSpan.FromMilliseconds(300), $"ended after {clock.Elapsed.TotalMilliseconds} ms, before the grace");
        Assert.Equal(["overran", "ended"], steps);
    }

    [Fact]
    public void The_process_is_ended_even_when_recording_the_overrun_fails()
    {
        using var ended = new ManualResetEventSlim();

        // In Noto the end ends the process before the failed record could be rethrown. Here the end parks the clock's
        // background thread instead, so the rethrow never reaches the test host.
        ExitWatchdog.Arm(TimeSpan.FromMilliseconds(50), () => throw new IOException("log unavailable"), () =>
        {
            ended.Set();
            Thread.Sleep(Timeout.Infinite);
        });

        Assert.True(ended.Wait(TimeSpan.FromSeconds(10)), "a failed record stopped the end");
    }

    [Fact]
    public void Noto_ends_by_killing_its_own_process()
    {
        string watchdog = File.ReadAllText(SourcePath("src", "Noto.Platform.Windows", "ExitWatchdog.cs"));

        Assert.Matches(
            @"public static void Arm\(TimeSpan grace, Action\? onOverrun\) =>\s*Arm\(grace, onOverrun, \(\) => Process\.GetCurrentProcess\(\)\.Kill\(\)\);",
            watchdog);
        Assert.Matches(@"try\s*\{\s*onOverrun\?\.Invoke\(\);\s*\}\s*finally\s*\{\s*end\(\);\s*\}", watchdog);
        Assert.Matches(@"IsBackground = true,", watchdog);
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
