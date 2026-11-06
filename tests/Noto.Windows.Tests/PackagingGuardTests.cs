using System.Text.RegularExpressions;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// How Noto is installed, upgraded and uninstalled (ADR-008, Velopack), checked in its source and release script.
/// </summary>
/// <remarks>
/// <c>tools/validation/Invoke-Install.ps1</c> installs, upgrades and uninstalls for real. These pin what it cannot
/// show by itself: no update is ever looked for, the hooks run before anything else, and the install folder can never
/// be the folder that holds the notes.
/// </remarks>
public sealed class PackagingGuardTests
{
    private static readonly string App = File.ReadAllText(SourcePath("src", "Noto.Windows", "App.xaml.cs"));
    private static readonly string Release = File.ReadAllText(SourcePath("tools", "packaging", "New-Release.ps1"));

    [Fact]
    public void Velopacks_hooks_run_first_only_for_a_hook_and_never_apply_an_update()
    {
        string constructor = Code(Constructor());

        // Velopack's start-up code logs every start to its own file: an ordinary start never reaches it.
        Assert.Matches(@"^\s*\{\s*if \(InstalledCopy\.IsHook\(Environment\.GetCommandLineArgs\(\)\)\)\s*\{\s*VelopackApp\.Build\(\)", constructor);
        Assert.True(Index(constructor, ".Run();") < Index(constructor, "InitializeComponent();"));
        Assert.Contains(".SetAutoApplyOnStartup(false)", constructor, StringComparison.Ordinal);
        Assert.Equal(1, Regex.Count(Code(App), @"VelopackApp\.Build\(\)"));
    }

    [Fact]
    public void An_installed_copy_takes_its_shortcuts_identity_before_any_window()
    {
        string constructor = Code(Constructor());

        Assert.True(Index(constructor, ".Run();") < Index(constructor, "_ = InstalledCopy.TakeShortcutIdentity(Environment.ProcessPath);"));
        Assert.True(Index(constructor, "InstalledCopy.TakeShortcutIdentity") < Index(constructor, "InitializeComponent();"));
        Assert.Equal(Noto.Platform.Windows.InstalledCopy.AppUserModelId, "velopack." + Regex.Match(Release, @"'--packId', '([^']+)'").Groups[1].Value);
    }

    [Fact]
    public void An_uninstall_removes_this_copys_start_at_login_and_nothing_else()
    {
        string constructor = Code(Constructor());

        Assert.Matches(@"\.OnBeforeUninstallFastCallback\(version =>\s*\{\s*if \(Environment\.ProcessPath is \{ \} executable\)\s*\{\s*_ = LoginRegistration\.ForCurrentUser\(executable\)\.RemoveIfThis\(\);\s*\}\s*\}\)", constructor);
        Assert.DoesNotContain(".Disable()", constructor, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_looks_for_an_update_or_reaches_the_network()
    {
        // quality-gates.md: no network access in the first release. Velopack's UpdateManager is the only part that would.
        foreach (string file in Sources())
        {
            Assert.DoesNotMatch(@"\bUpdateManager\b|\bGithubSource\b|\bSimpleWebSource\b|\bHttpClient\b|\bWebRequest\b", Code(File.ReadAllText(file)));
        }
    }

    [Fact]
    public void The_install_folder_is_never_the_notes_folder()
    {
        // Velopack installs to %LOCALAPPDATA%\<packId> and an uninstall deletes that folder; the notes are in
        // %LOCALAPPDATA%\<FolderName>.
        string paths = File.ReadAllText(SourcePath("src", "Noto.Infrastructure", "Storage", "NotoStoragePaths.cs"));
        string notes = Regex.Match(paths, @"private const string FolderName = ""([^""]+)"";").Groups[1].Value;
        Match packIds = Regex.Match(Release, @"'--packId', '([^']+)'");

        Assert.Equal("Noto", notes);
        Assert.True(packIds.Success);
        Assert.Equal("Noto.App", packIds.Groups[1].Value);
        Assert.NotEqual(notes, packIds.Groups[1].Value, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(1, Regex.Count(Release, "--packId"));
    }

    [Fact]
    public void The_library_and_the_packing_tool_are_the_same_version()
    {
        string library = Regex.Match(File.ReadAllText(SourcePath("Directory.Packages.props")), @"Include=""Velopack"" Version=""([^""]+)""").Groups[1].Value;
        string tool = Regex.Match(File.ReadAllText(SourcePath(".config", "dotnet-tools.json")), @"""vpk"":\s*\{\s*""version"":\s*""([^""]+)""").Groups[1].Value;

        Assert.NotEmpty(library);
        Assert.Equal(library, tool);
    }

    [Fact]
    public void Only_the_windows_app_sdk_parts_noto_uses_are_shipped()
    {
        // The whole SDK would put its AI, machine-learning, search and widgets libraries (about 50 MB) in the installer.
        string[] projects = [SourcePath("src", "Noto.Windows", "Noto.Windows.csproj"), SourcePath("src", "Noto.UI", "Noto.UI.csproj")];

        foreach (string text in projects.Select(File.ReadAllText))
        {
            Assert.DoesNotContain("Include=\"Microsoft.WindowsAppSDK\"", text, StringComparison.Ordinal);
            Assert.Contains("Include=\"Microsoft.WindowsAppSDK.WinUI\"", text, StringComparison.Ordinal);
            Assert.DoesNotMatch(@"Microsoft\.WindowsAppSDK\.(AI|ML|Search|Widgets|DWrite)\b", text);
        }
    }

    private static int Index(string text, string value)
    {
        int index = text.IndexOf(value, StringComparison.Ordinal);
        Assert.True(index >= 0, $"'{value}' was not found.");
        return index;
    }

    private static string Constructor()
    {
        int start = App.IndexOf("\n    public App()\n", StringComparison.Ordinal);
        Assert.True(start >= 0, "The App constructor was not found.");

        int end = App.IndexOf("\n    }\n", start, StringComparison.Ordinal);
        return App[(App.IndexOf('\n', start + 1) + 1)..end];
    }

    private static IEnumerable<string> Sources()
    {
        string root = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(SourcePath("src", "Noto.Windows", "App.xaml.cs"))))!;

        return Directory.EnumerateFiles(Path.Join(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }

    /// <summary>The source without line comments, so a comment cannot satisfy or fail a guard.</summary>
    private static string Code(string source) => Regex.Replace(source, @"//.*", string.Empty);

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
