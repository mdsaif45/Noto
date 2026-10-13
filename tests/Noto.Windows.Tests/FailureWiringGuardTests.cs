using System.Text.RegularExpressions;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// #10 in the composition root: the last-chance boundaries go to one FailureBoundary, a database that cannot start
/// is refused with a message, and every failure ends through the application's one shutdown transition. Checked in
/// the source, as the other wiring guards are; the runtime check is a real start against isolated data roots.
/// </summary>
public sealed class FailureWiringGuardTests
{
    private static readonly string Source = File.ReadAllText(SourcePath("src", "Noto.Windows", "App.xaml.cs"));
    private static readonly string App = Regex.Replace(Source, @"//.*", string.Empty);

    [Fact]
    public void Every_boundary_hands_its_failure_to_the_one_failure_boundary_once_the_log_exists()
    {
        int log = Index(App, "new RollingFileLog(paths.LogsDirectory)");
        int boundary = Index(App, "_failures = new FailureBoundary(log, SaveOnFailure, ExitAfterFailure);");
        int claim = Index(App, "if (!ClaimDataRoot(paths))");

        Assert.True(log < boundary && boundary < claim, $"log={log} boundary={boundary} claim={claim}");
        Assert.Contains("_failures.Fail(UnhandledSource.UiThread, e.Exception);", App, StringComparison.Ordinal);
        Assert.Contains("_failures.Fail(UnhandledSource.OtherThread, exception);", App, StringComparison.Ordinal);
        Assert.Contains("TaskScheduler.UnobservedTaskException += (_, e) => log.UnobservedTaskException(ExceptionSummary.From(e.Exception));", App, StringComparison.Ordinal);
        Assert.Equal(1, Regex.Count(App, @"new FailureBoundary\("));
    }

    [Fact]
    public void Every_callback_Noto_queues_reaches_the_failure_boundary()
    {
        // WinUI routes a queued callback's exception to no boundary at all (measured: fail-fast, nothing saved), so
        // every enqueue in the application goes through UiWork, which hands it to the same FailureBoundary.
        var raw = Directory.EnumerateFiles(Path.GetDirectoryName(SourcePath("src", "Noto.Windows", "App.xaml.cs"))!, "*.cs")
            .Where(f => Path.GetFileName(f) != "UiWork.cs")
            .Where(f => Regex.IsMatch(Regex.Replace(File.ReadAllText(f), @"//.*", string.Empty), @"\.TryEnqueue\("))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(raw.Count == 0, $"Raw TryEnqueue outside UiWork in: {string.Join(", ", raw)}");
        Assert.Contains("UiWork.OnFailure = exception => _failures.Fail(UnhandledSource.UiThread, exception);", App, StringComparison.Ordinal);
        Assert.Contains("OnFailure(ex);", File.ReadAllText(SourcePath("src", "Noto.Windows", "UiWork.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public void A_database_that_cannot_start_is_refused_with_a_message_and_released_never_a_crash()
    {
        string guarded = App[Index(App, "database.Initialize();")..Index(App, "var folders = new SqliteFolderRepository(database);")];

        Assert.Contains("catch (StorageException ex)", guarded, StringComparison.Ordinal);
        int logged = Index(guarded, "log.StartupFailed(ex.Reason, ExceptionSummary.From(ex));");
        int shown = Index(guarded, "StartupNotice.Show(StartupFailureMessage.For(ex.Reason, paths.DatabaseFile));");
        int released = Index(guarded, "ReleaseResources();");
        int exited = Index(guarded, "Environment.Exit(StartupFailedExitCode);");

        Assert.True(logged < shown && shown < released && released < exited, $"logged={logged} shown={shown} released={released} exited={exited}");
    }

    [Fact]
    public void A_failure_ends_through_the_one_shutdown_transition_and_the_one_release()
    {
        string exit = Body(App, "ExitAfterFailure");

        int transition = Index(exit, "BeginShutdownOnce();");
        int release = Index(exit, "ReleaseResources();");
        int end = Index(exit, "Environment.Exit(FailedExitCode);");

        Assert.True(transition < release && release < end, $"transition={transition} release={release} end={end}");
    }

    [Fact]
    public void The_save_after_a_failure_is_the_editors_own_and_never_waits_unbounded()
    {
        string save = Body(App, "SaveOnFailure");

        Assert.Equal(2, Regex.Count(save, @"main\.SaveBeforeHide\(\)"));
        Assert.Contains("done.Wait(FailureSaveTimeout)", save, StringComparison.Ordinal);
    }

    [Fact]
    public void The_fault_trigger_exists_only_in_debug_builds()
    {
        // Every mention of it sits between #if DEBUG and #endif, so Release has no way to fault on demand.
        string outside = Regex.Replace(Source, @"#if DEBUG[\s\S]*?#endif", string.Empty);

        Assert.Contains("ArmFaultInjection", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("ArmFaultInjection", outside, StringComparison.Ordinal);
        Assert.DoesNotContain("noto-fault", outside, StringComparison.Ordinal);
    }

    private static int Index(string text, string value)
    {
        int index = text.IndexOf(value, StringComparison.Ordinal);
        Assert.True(index >= 0, $"Not found: {value}");
        return index;
    }

    private static string Body(string source, string method)
    {
        var declaration = Regex.Match(source, $@"private \w+ {method}\(");
        Assert.True(declaration.Success, $"Not found: {method}");
        int start = declaration.Index;
        int open = source.IndexOf('{', start);
        int depth = 0;

        for (int i = open; i < source.Length; i++)
        {
            depth += source[i] == '{' ? 1 : source[i] == '}' ? -1 : 0;

            if (depth == 0)
            {
                return source[open..(i + 1)];
            }
        }

        throw new InvalidOperationException($"Unbalanced body for {method}");
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
