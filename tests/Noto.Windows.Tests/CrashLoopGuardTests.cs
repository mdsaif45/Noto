using System.Text.RegularExpressions;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// F16 (#10) in the source: a failure is recorded as Noto ends; a start that finds Noto failing at every start stops
/// before anything opens, quietly at sign-in and otherwise only on the user's word, No the default. What a real start
/// does is Invoke-CrashLoop.ps1's to show.
/// </summary>
public sealed class CrashLoopGuardTests
{
    private static readonly string App = File.ReadAllText(SourcePath("src", "Noto.Windows", "App.xaml.cs"));

    [Fact]
    public void A_start_checks_for_a_loop_once_it_owns_the_data_root_and_before_anything_opens()
    {
        int claim = Index(App, "if (!(login ? ClaimForLogin(paths) : ClaimDataRoot(paths)))");
        int check = Index(App, "if (CrashLoop.IsLoop(FailureHistory.Read(paths.FailuresFile), TimeProvider.System.GetUtcNow()))");
        int pipe = Index(App, "StartActivationPipe();");
        int open = Index(App, "created = database.Initialize();");

        Assert.True(claim < check && check < pipe && pipe < open, $"{claim} {check} {pipe} {open}");
    }

    [Fact]
    public void At_sign_in_it_ends_quietly_and_otherwise_only_the_user_starts_it_again()
    {
        // Never a message at sign-in: a login start does not ask, so it does not start.
        Assert.Contains("bool startAnyway = !login && StartupNotice.Ask(CrashLoopMessage.For(paths.Root));", App, StringComparison.Ordinal);
        Assert.Matches(@"if \(!startAnyway\)\s*\{\s*ReleaseResources\(\);\s*Environment\.Exit\(StartupFailedExitCode\);\s*return;\s*\}\s*FailureHistory\.Clear\(paths\.FailuresFile\);", App);
        Assert.Contains("log.CrashLoopStopped(login, startAnyway);", App, StringComparison.Ordinal);

        // No is the default button: Enter never starts a Noto that may fail again.
        string notice = File.ReadAllText(SourcePath("src", "Noto.Platform.Windows", "StartupNotice.cs"));
        Assert.Contains("MB_YESNO | NativeMethods.MB_DEFBUTTON2", notice, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failure_is_recorded_with_how_soon_after_its_start_before_the_process_ends()
    {
        string exit = Regex.Match(App, @"private void ExitAfterFailure\(\)[\s\S]*?Environment\.Exit\(FailedExitCode\);").Value;

        Assert.True(Index(exit, "FailureHistory.Append(failures, new Failure(TimeProvider.System.GetUtcNow(), _sinceStart.Elapsed));") < Index(exit, "BeginShutdownOnce();"));
    }

    private static int Index(string text, string value)
    {
        int index = text.IndexOf(value, StringComparison.Ordinal);
        Assert.True(index >= 0, $"Not found: {value}");
        return index;
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
