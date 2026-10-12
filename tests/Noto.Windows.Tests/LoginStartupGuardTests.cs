using System.Text.RegularExpressions;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// How the application wires run at login (#94, ADR-015), checked in its source.
/// </summary>
/// <remarks>
/// Like <see cref="TrayLifetimeGuardTests"/>: the composition root is read,
/// not loaded. These pin what no runtime run can show by itself: a login
/// start never hands off and never activates the window; Windows' Startup
/// apps setting is only ever read; nothing registers at startup; and the
/// registry belongs to the platform layer.
/// </remarks>
public sealed class LoginStartupGuardTests
{
    private static readonly string App = File.ReadAllText(SourcePath("src", "Noto.Windows", "App.xaml.cs"));
    private static readonly string Login = File.ReadAllText(SourcePath("src", "Noto.Platform.Windows", "LoginStartup.cs"));

    [Fact]
    public void The_login_switch_is_read_once_as_an_exact_token()
    {
        string launch = Body(App, "OnLaunched");

        Assert.Contains("bool login = commandLine.Contains(LoginRegistration.LoginSwitch);", launch, StringComparison.Ordinal);
        Assert.Equal(1, Regex.Count(Code(App), @"LoginRegistration\.LoginSwitch"));
        Assert.Contains("public const string LoginSwitch = \"--login\";", Login, StringComparison.Ordinal);
    }

    [Fact]
    public void A_login_start_claims_without_a_handoff_or_a_message_box()
    {
        Assert.Contains("if (!(login ? ClaimForLogin(paths) : ClaimDataRoot(paths)))", Body(App, "OnLaunched"), StringComparison.Ordinal);

        string claim = Code(Body(App, "ClaimForLogin"));
        Assert.Contains("InstanceOwnership.TryAcquire(_instanceKey)", claim, StringComparison.Ordinal);
        Assert.DoesNotContain("InstanceOwnership.Claim(", claim, StringComparison.Ordinal);
        Assert.DoesNotContain("StartupNotice", claim, StringComparison.Ordinal);
        Assert.DoesNotContain("Window", claim, StringComparison.Ordinal);
    }

    [Fact]
    public void A_login_start_never_activates_the_window()
    {
        string launch = Code(Body(App, "OnLaunched"));

        Assert.Matches(@"if \(!login\)\s*\{\s*_window\.Activate\(\);\s*\}", launch);
        Assert.Equal(1, Regex.Count(Code(App), @"\.Activate\(\)"));
    }

    [Fact]
    public void Windows_startup_apps_setting_is_only_ever_opened_read_only()
    {
        string code = Code(Login);

        foreach (Match use in Regex.Matches(code, @"[^\n]*_approvedKey[^\n]*"))
        {
            if (use.Value.Contains("private readonly", StringComparison.Ordinal) || use.Value.Contains("_approvedKey = approvedKey", StringComparison.Ordinal))
            {
                continue;
            }

            Assert.Contains("OpenSubKey(_approvedKey, writable: false)", use.Value, StringComparison.Ordinal);
        }

        Assert.DoesNotMatch(@"CreateSubKey\(_approvedKey|DeleteValue\([^)]*approved|approved\.SetValue|approved\.DeleteValue", code);
    }

    [Fact]
    public void The_registered_command_carries_no_data_root()
    {
        Assert.DoesNotContain("data-root", Body(Login, "CommandFor"), StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_registers_at_startup()
    {
        string launch = Code(Body(App, "OnLaunched")) + Code(Body(App, "StartTray"));

        Assert.DoesNotContain(".Enable()", launch, StringComparison.Ordinal);
        Assert.DoesNotContain(".Disable()", launch, StringComparison.Ordinal);
    }

    [Fact]
    public void The_tray_choice_flips_from_the_state_windows_has_now()
    {
        string toggle = Code(Body(App, "ToggleLoginStartup"));

        Assert.True(Index(toggle, "_login.State()") < Index(toggle, "_login.Enable()"));
        Assert.Matches(@"LoginStartupState\.Off => _login\.Enable\(\)", toggle);
        Assert.Matches(@"LoginStartupState\.On => _login\.Disable\(\)", toggle);
        Assert.Matches(@"_ => before", toggle);
        Assert.Contains("LoginStartupRow.For(_login?.State()", Body(App, "OnTrayMenuRequested"), StringComparison.Ordinal);
    }

    [Fact]
    public void Run_at_login_adds_no_setting_and_the_registry_stays_in_the_platform()
    {
        string settings = File.ReadAllText(SourcePath("src", "Noto.Core", "Settings", "SettingKeys.cs"));
        Assert.DoesNotMatch(@"(?i)login|startup\.|run-at", settings);

        string windowsSource = Path.GetDirectoryName(SourcePath("src", "Noto.Windows", "App.xaml.cs"))!;
        foreach (string file in Directory.EnumerateFiles(windowsSource, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            Assert.DoesNotContain("Microsoft.Win32", File.ReadAllText(file), StringComparison.Ordinal);
        }
    }

    /// <summary>The source without line comments, so a comment cannot satisfy or fail a guard.</summary>
    private static string Code(string source) => Regex.Replace(source, @"//.*", string.Empty);

    /// <summary>A member's text, from its declaration to the next member at the same depth.</summary>
    private static string Body(string source, string member)
    {
        Match declaration = Regex.Match(source, $@"\n    (?:protected |private |public |internal )[^\n(]*\b{member}\(");
        Assert.True(declaration.Success, $"{member} was not found.");

        int start = declaration.Index;
        int end = source.IndexOf("\n    }\n", start, StringComparison.Ordinal);
        int expressionEnd = source.IndexOf(";\n\n", start, StringComparison.Ordinal);

        if (expressionEnd > start && (end < 0 || expressionEnd < end) && !source[start..expressionEnd].Contains('{', StringComparison.Ordinal))
        {
            return source[start..expressionEnd];
        }

        Assert.True(end > start, $"The end of {member} was not found.");
        return source[start..end];
    }

    private static int Index(string text, string value)
    {
        int index = text.IndexOf(value, StringComparison.Ordinal);
        Assert.True(index >= 0, $"'{value}' was not found.");
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
