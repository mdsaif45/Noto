using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// The welcome's wiring (ADR-021), held by the build: decided once at startup from the database's creation and a
/// restore, before the window; opened after the first folder load; ended only by Get started, Skip or Escape, which the
/// composition root records and nothing else does; one surface like Settings, named for a screen reader, with one
/// filled action. What it does on a running build is Invoke-Welcome.ps1's.
/// </summary>
public sealed class WelcomeGuardTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly string Windows = Path.GetDirectoryName(SourcePath("src", "Noto.Windows", "App.xaml.cs"))!;
    private static readonly string App = Code(Path.Join(Windows, "App.xaml.cs"));
    private static readonly string Main = Code(Path.Join(Windows, "MainWindow.xaml.cs"));
    private static readonly string Welcome = Code(Path.Join(Windows, "MainWindow.Welcome.cs"));
    private static readonly string Page = Code(Path.Join(Windows, "WelcomePage.xaml.cs"));
    private static readonly XDocument PageXaml = XDocument.Load(Path.Join(Windows, "WelcomePage.xaml"));

    [Fact]
    public void The_welcome_is_decided_once_after_settings_load_and_before_the_window()
    {
        string launch = Body(App, "OnLaunched");
        int restored = Index(launch, "RestoreOutcome restored = PendingRestore.ApplyIfPending(");
        int created = Index(launch, "created = database.Initialize();");
        int load = Index(launch, "settings.Load();");
        int decide = Index(launch, "bool welcome = onboarding.ShowAtStart(");
        int window = Index(launch, "var main = new MainWindow(");
        int handed = Index(launch, "main.WelcomeAtStart = welcome;");

        Assert.True(restored < created && created < load && load < decide && decide < window && window < handed, $"{restored} {created} {load} {decide} {window} {handed}");
        Assert.Equal(1, Regex.Count(App, @"\.ShowAtStart\("));
        Assert.Matches(@"onboarding\.ShowAtStart\(\s*created,\s*restored == RestoreOutcome\.Applied,\s*\(\) => Onboarding\.HoldsAnything\(folders, notes\)\);", launch);
    }

    [Fact]
    public void Only_the_end_of_the_welcome_is_recorded_and_only_by_the_composition_root()
    {
        string launch = Body(App, "OnLaunched");

        Assert.Contains("main.WelcomeEnded = outcome => _ = outcome == WelcomeState.Completed ? onboarding.Complete() : onboarding.Skip();", launch, StringComparison.Ordinal);
        Assert.Contains("main.WelcomeTour = () => WelcomeTour.For(preferences.HotkeyEnabled ? preferences.HotkeyBinding : null);", launch, StringComparison.Ordinal);
        Assert.Equal(1, Regex.Count(App, @"onboarding\.Complete\(\)"));
        Assert.Equal(1, Regex.Count(App, @"onboarding\.Skip\(\)"));
        Assert.DoesNotContain("Onboarding", Main + Welcome + Page, StringComparison.Ordinal);

        // Get started on the last step completes; Skip and Escape skip; nothing else ends it.
        Assert.Matches(@"if \(tour\.IsLast\)\s*\{\s*End\(WelcomeState\.Completed\);\s*return;\s*\}\s*_ = tour\.Next\(\);", Body(Page, "Primary"));
        Assert.Contains("=> End(WelcomeState.Skipped);", Page, StringComparison.Ordinal);
        Assert.Matches(@"if \(e\.Key == VirtualKey\.Escape && WelcomeSurface is \{ \} page\)\s*\{\s*e\.Handled = true;\s*page\.End\(WelcomeState\.Skipped\);", Body(Welcome, "OnWelcomeKeyDown"));
        Assert.Equal(1, Regex.Count(Page, @"End\(WelcomeState\.Completed\)"));
        Assert.Equal(2, Regex.Count(Page + Welcome, @"End\(WelcomeState\.Skipped\)"));
        Assert.Matches(@"CloseWelcome\(\);\s*WelcomeEnded\?\.Invoke\(outcome\);", Body(Welcome, "OnWelcomeEnded"));
    }

    [Fact]
    public void It_opens_after_the_first_folder_load_and_from_Settings_Help()
    {
        Assert.Matches(@"Refresh\(\);\s*OpenWelcomeAtStart\(\);", Body(Main, "OnPaneFirstLoaded"));
        Assert.Matches(@"if \(WelcomeAtStart\)\s*\{\s*WelcomeAtStart = false;\s*OpenWelcome\(\);", Body(Welcome, "OpenWelcomeAtStart"));
        Assert.Contains("page.WelcomeRequested += OnSettingsWelcomeRequested;", Body(Main, "OpenSettings"), StringComparison.Ordinal);
        Assert.Contains("page.WelcomeRequested -= OnSettingsWelcomeRequested;", Body(Main, "CloseSettings"), StringComparison.Ordinal);
        Assert.Contains("=> OpenWelcome();", Welcome, StringComparison.Ordinal);

        // Help's Show tour asks for the welcome, and nothing else does.
        string settings = Code(Path.Join(Windows, "SettingsPage.xaml.cs"));
        Assert.Contains("private void OnShowWelcomeClick(object sender, RoutedEventArgs e) => WelcomeRequested?.Invoke(this, EventArgs.Empty);", settings, StringComparison.Ordinal);
        Assert.Equal(1, Regex.Count(settings, @"WelcomeRequested\?\.Invoke"));
        XElement show = XDocument.Load(Path.Join(Windows, "SettingsPage.xaml")).Descendants().Single(e => (string?)e.Attribute(X + "Name") == "ShowWelcomeButton");
        Assert.Equal("OnShowWelcomeClick", (string?)show.Attribute("Click"));

        // Focus comes back where it belongs when what it was on is gone: the welcome's primary action under Settings,
        // Help's Show tour under the welcome.
        Assert.Matches(@": back == NoteRoot \? NoteList\s*: back is WelcomePage welcome \? welcome\.PrimaryButton\s*: FolderList;", Body(Main, "CloseSettings"));
        Assert.Matches(@": back == NoteRoot \? NoteList\s*: back is SettingsPage settings \? settings\.HelpButton\s*: Folders\.Count > 0 \? FolderList\s*: FolderNameInput;", Body(Welcome, "CloseWelcome"));
        Assert.Contains("internal Control HelpButton => ShowWelcomeButton;", settings, StringComparison.Ordinal);
        Assert.Contains("internal Control PrimaryButton => WelcomeNextButton;", Page, StringComparison.Ordinal);

        // One surface at a time, like Settings: created on open, removed on close; Search never covers it.
        string open = Body(Welcome, "OpenWelcome");
        Assert.Matches(@"if \(WelcomeSurface is \{ \} open\)\s*\{\s*CloseSettings\(\);\s*open\.FocusPrimary\(\);\s*return;", open);
        Assert.Matches(@"UIElement from = SettingsSurface is \{ \} settings \? settings\s*: RecycleBinSurface", open);
        Assert.Contains("((Panel)page.Parent).Children.Remove(page);", Body(Welcome, "CloseWelcome"), StringComparison.Ordinal);
        Assert.Contains("WelcomeSurface is not null)", Body(Code(Path.Join(Windows, "MainWindow.Search.cs")), "OpenSearch"), StringComparison.Ordinal);
    }

    [Fact]
    public void Enter_is_the_primary_action_and_focus_never_rests_on_a_hidden_Back()
    {
        Assert.Matches(@"if \(e\.Key == VirtualKey\.Enter && !e\.Handled\)\s*\{\s*e\.Handled = true;\s*Primary\(\);", Body(Page, "OnKeyDown"));
        Assert.Matches(@"WelcomeBackButton\.Visibility = tour\.IsFirst \? Visibility\.Collapsed : Visibility\.Visible;", Body(Page, "ShowStep"));
        Assert.Matches(@"if \(tour\.IsFirst\)\s*\{\s*FocusPrimary\(\);", Body(Page, "OnBackClick"));
        Assert.Contains("page.FocusPrimary", Body(Welcome, "OpenWelcome"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_surface_is_named_for_a_screen_reader_with_its_step_in_words_and_its_decoration_raw()
    {
        XElement root = PageXaml.Root!;
        Assert.Equal("WelcomeRoot", (string?)root.Attribute("AutomationProperties.AutomationId"));
        Assert.Equal("Welcome to Noto", (string?)root.Attribute("AutomationProperties.Name"));
        Assert.Contains("new FrameworkElementAutomationPeer(this)", Page, StringComparison.Ordinal);

        Assert.Equal("Level1", (string?)Named("WelcomeTitle").Attribute("AutomationProperties.HeadingLevel"));
        Assert.Equal("Raw", (string?)Named("WelcomeProgressBar").Attribute("AutomationProperties.AccessibilityView"));
        Assert.Contains("WelcomeProgressText.Text = tour.Progress;", Page, StringComparison.Ordinal);
        Assert.Contains("RaiseNotificationEvent(", Body(Page, "ShowStep"), StringComparison.Ordinal);

        // A step reached by Next or Back is announced; the first, shown with the page, is read as the page is.
        Assert.Matches(@"if \(announce\)\s*\{\s*FrameworkElementAutomationPeer\.FromElement\(this\)\?\.RaiseNotificationEvent\(", Body(Page, "ShowStep"));
        Assert.Contains("ShowStep(announce: false);", Body(Page, "Show"), StringComparison.Ordinal);
        Assert.Contains("ShowStep(announce: true);", Body(Page, "Primary"), StringComparison.Ordinal);
        Assert.Contains("ShowStep(announce: true);", Body(Page, "OnBackClick"), StringComparison.Ordinal);

        // The primary action says what it does, on screen and to a screen reader alike.
        Assert.Matches(@"WelcomeNextButton\.Content = tour\.PrimaryLabel;\s*Microsoft\.UI\.Xaml\.Automation\.AutomationProperties\.SetName\(WelcomeNextButton, tour\.PrimaryLabel\);", Body(Page, "ShowStep"));
        Assert.Contains("$\"{tour.Progress}. {step.Title}. {step.Summary}\"", Body(Page, "ShowStep"), StringComparison.Ordinal);

        foreach (string id in new[] { "WelcomeProgressText", "WelcomeTitle", "WelcomeSummary", "WelcomeLines", "WelcomeSkipButton", "WelcomeBackButton", "WelcomeNextButton" })
        {
            Assert.Equal(id, (string?)Named(id).Attribute("AutomationProperties.AutomationId"));
        }

        Assert.Equal("False", (string?)Named("WelcomeLines").Attribute("IsTabStop"));
    }

    [Fact]
    public void One_filled_action_and_the_buttons_in_reading_order()
    {
        var buttons = PageXaml.Descendants().Where(e => e.Name.LocalName == "Button").ToList();

        Assert.Equal(["WelcomeSkipButton", "WelcomeBackButton", "WelcomeNextButton"], buttons.Select(b => (string?)b.Attribute(X + "Name")));
        Assert.Equal(["0", "2", "3"], buttons.Select(b => (string?)b.Attribute("Grid.Column")));
        Assert.Equal(
            ["{StaticResource SubtleButtonStyle}", "{StaticResource SubtleButtonStyle}", "{StaticResource FolderPaneButtonStyle}"],
            buttons.Select(b => (string?)b.Attribute("Style")));
        Assert.Equal("{StaticResource ReadingWidth}", (string?)Named("WelcomeLayout").Attribute("MaxWidth"));
        Assert.Equal("Center", (string?)Named("WelcomeLayout").Attribute("VerticalAlignment"));
    }

    private static XElement Named(string name) =>
        PageXaml.Descendants().Single(e => (string?)e.Attribute(X + "Name") == name);

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
