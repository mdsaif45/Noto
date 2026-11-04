using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// Data portability's wiring (ADR-020 §3, §6), held by the build: a restore is applied before anything opens the
/// database and only after the user said so, Cancel being the default; the restart passes on only the data root; the
/// work runs one at a time, off the UI thread, with its result said politely and its failure on the notice line.
/// What it does on a running build is Invoke-Portability.ps1's.
/// </summary>
public sealed class PortabilityGuardTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly string Windows = Path.GetDirectoryName(SourcePath("src", "Noto.Windows", "App.xaml.cs"))!;
    private static readonly string App = Code(Path.Join(Windows, "App.xaml.cs"));
    private static readonly string Data = Code(Path.Join(Windows, "SettingsPage.Data.cs"));
    private static readonly string Portability = Code(Path.Join(Windows, "DataPortability.cs"));

    [Fact]
    public void A_restore_is_applied_after_the_claim_and_before_the_database_opens()
    {
        string launch = Body(App, "OnLaunched");
        int claim = Index(launch, "if (!(login ? ClaimForLogin(paths) : ClaimDataRoot(paths)))");
        int apply = Index(launch, "RestoreOutcome restored = PendingRestore.ApplyIfPending(paths, TimeProvider.System);");
        int open = Index(launch, "var database = new NotoDatabase(paths.DatabaseFile, log);");

        Assert.True(claim < apply && apply < open, $"claim {claim}, apply {apply}, open {open}");
        Assert.Equal(1, Regex.Count(App, @"PendingRestore\.ApplyIfPending\("));
        Assert.Contains("main.ShowRestoreOutcome(restored);", launch, StringComparison.Ordinal);

        // A restore that rolled back is said, and said again after the first folder load, which clears the notice line
        // (measured: without it, the notice was gone before the window was seen).
        string main = Code(Path.Join(Windows, "MainWindow.xaml.cs"));
        Assert.Matches(@"HideNotice\(\);\s*if \(_restoreNotice is \{ \} restored\)\s*\{\s*_restoreNotice = null;\s*ShowNotice\(restored\);", Body(main, "Refresh"));
        Assert.Contains("_restoreNotice = \"The backup couldn't be restored, so your notes are as they were.\";", Code(Path.Join(Windows, "MainWindow.Portability.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public void A_restart_quits_through_the_one_end_and_passes_on_only_the_data_root()
    {
        Assert.Matches(@"private void Restart\(\)\s*\{\s*_restartAfterRelease = true;\s*Quit\(\);\s*\}", App);

        // At the end of ReleaseResources: after the database and the data root's claim are let go.
        string release = Body(App, "ReleaseResources");
        Assert.True(Index(release, "_ownership?.Dispose();") < Index(release, "if (_restartAfterRelease)"));
        Assert.Contains("_ = Relaunch.Start(Environment.GetCommandLineArgs().Where(a => a.StartsWith(DataRootSwitch, StringComparison.Ordinal)));", release, StringComparison.Ordinal);
    }

    [Fact]
    public void A_restore_is_checked_then_confirmed_with_Cancel_the_default_and_the_present_backed_up_first()
    {
        string restore = Data[Index(Data, "private async void OnRestoreClick")..Index(Data, "private async void OnExportClick")];

        Assert.True(Index(restore, "await data.Check(file, progress, cancellation);") < Index(restore, "var confirm = new ContentDialog"));
        Assert.Matches(@"if \(manifest is null\)\s*\{\s*ShowNotice\(DataPortability\.Explain\(problem!\.Value\)\);", restore);
        Assert.Contains("DefaultButton = ContentDialogButton.Close,", restore, StringComparison.Ordinal);
        Assert.Matches(@"if \(await confirm\.ShowAsync\(\) != ContentDialogResult\.Primary\)\s*\{\s*data\.Discard\(\);", restore);

        string restart = Body(Portability, "RestoreAndRestart");
        Assert.True(Index(restart, "BackupWriter.Create(database, paths.AttachmentsDirectory, safety,") < Index(restart, "PendingRestore.Commit(paths, manifest);"));
        Assert.True(Index(restart, "PendingRestore.Commit(paths, manifest);") < Index(restart, "restart();"));
    }

    [Fact]
    public void Work_runs_one_at_a_time_off_the_UI_thread_and_its_failure_changes_nothing_it_says()
    {
        string run = Body(Portability, "Run");
        Assert.Contains("if (Interlocked.Exchange(ref _busy, 1) != 0)", run, StringComparison.Ordinal);
        Assert.Contains("return await Task.Run(work);", run, StringComparison.Ordinal);

        string work = Body(Data, "Work");
        Assert.Contains("SetRowsEnabled(false);", work, StringComparison.Ordinal);
        Assert.Matches(@"catch \(OperationCanceledException\)\s*\{\s*result = ""Cancelled\. Nothing was changed\."";", work);
        Assert.Matches(@"catch \(PortabilityException\)\s*\{\s*ShowNotice\(", work);
        Assert.Matches(@"finally\s*\{\s*_work = null;\s*SetRowsEnabled\(true\);", work);

        // A backup and an export save the note being edited first, so they hold what is on screen.
        Assert.Matches(@"public async Task<BackupCounts> Backup\([^)]*\)\s*\{\s*saveOpenNote\(\);", Portability);

        // An unfinished backup leaves no empty file where the save dialog made one, and never deletes a file with content.
        Assert.Matches(@"catch \(Exception ex\) when \(ex is OperationCanceledException or PortabilityException\)\s*\{\s*RemoveEmpty\(file\);\s*throw;", Body(Portability, "Backup"));
        Assert.Contains("if (new FileInfo(file) is { Exists: true, Length: 0 })", Body(Portability, "RemoveEmpty"), StringComparison.Ordinal);
        Assert.Matches(@"public Task<ExportSummary\?> Export\([^)]*\)\s*\{\s*saveOpenNote\(\);", Portability);
    }

    [Fact]
    public void Results_are_said_politely_and_failures_assertively()
    {
        XDocument settings = XDocument.Load(Path.Join(Windows, "SettingsPage.xaml"));
        XDocument window = XDocument.Load(Path.Join(Windows, "MainWindow.xaml"));

        Assert.Equal("Polite", (string?)Named(settings, "DataStatusText").Attribute("AutomationProperties.LiveSetting"));
        Assert.Equal("Assertive", (string?)Named(settings, "SettingsNoticeText").Attribute("AutomationProperties.LiveSetting"));
        Assert.Equal("Polite", (string?)Named(window, "StatusText").Attribute("AutomationProperties.LiveSetting"));
        Assert.Equal("Polite", (string?)Named(window, "NoteStatusText").Attribute("AutomationProperties.LiveSetting"));
        Assert.Equal("Progress", (string?)Named(settings, "DataProgressBar").Attribute("AutomationProperties.Name"));

        // A result is never in the error colour.
        Assert.All(new[] { Named(settings, "DataStatusText"), Named(window, "StatusText"), Named(window, "NoteStatusText") },
            e => Assert.DoesNotContain("Notice", (string?)e.Attribute("Style") ?? string.Empty, StringComparison.Ordinal));
    }

    [Fact]
    public void A_folder_and_a_card_export_from_their_menus()
    {
        string main = Code(Path.Join(Windows, "MainWindow.xaml.cs"));
        Assert.Contains("ExportFromMenu(new Infrastructure.Portability.ExportScope.Folder(folder.Id), fromStack: false)", main, StringComparison.Ordinal);
        Assert.Contains("ExportFromMenu(new Infrastructure.Portability.ExportScope.Note(card.Id), fromStack: true)", main, StringComparison.Ordinal);
    }

    private static XElement Named(XDocument document, string name) =>
        document.Descendants().Single(e => (string?)e.Attribute(X + "Name") == name);

    private static int Index(string source, string needle)
    {
        int at = source.IndexOf(needle, StringComparison.Ordinal);
        Assert.True(at >= 0, $"Not found: {needle}");
        return at;
    }

    private static string Code(string path) => Regex.Replace(File.ReadAllText(path), @"^\s*//.*$", string.Empty, RegexOptions.Multiline);

    private static string Body(string source, string method)
    {
        var declaration = Regex.Match(source, $@"(private|internal|public|protected)(?: override)?(?: static)?(?: async)? [\w<>?,\[\]() ]+? {method}(?:<\w+>)?\(");
        Assert.True(declaration.Success, $"Not found: {method}");
        int open = source.IndexOf('{', declaration.Index);
        int depth = 0;

        for (int i = open; i < source.Length; i++)
        {
            depth += source[i] switch { '{' => 1, '}' => -1, _ => 0 };

            if (depth == 0)
            {
                return source[open..(i + 1)];
            }
        }

        throw new InvalidOperationException($"Unbalanced body for {method}");
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
