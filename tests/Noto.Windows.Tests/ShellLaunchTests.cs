using Noto.Platform.Windows;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// The data folder action (Slice 1, parity J36). Only what can be tested without opening Explorer: a folder that does
/// not exist is refused before anything is handed to the shell, and is never created.
/// </summary>
public sealed class ShellLaunchTests
{
    [Fact]
    public void A_folder_that_does_not_exist_is_refused_and_not_created()
    {
        string missing = Path.Join(Path.GetTempPath(), "noto-shell-" + Guid.NewGuid().ToString("N"));

        Assert.False(ShellLaunch.OpenFolder(missing));
        Assert.False(Directory.Exists(missing));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_folder_is_a_programming_error(string folder)
    {
        Assert.ThrowsAny<ArgumentException>(() => ShellLaunch.OpenFolder(folder));
    }

    [Fact]
    public void The_startup_apps_page_is_windows_own_settings_uri()
    {
        Assert.Equal("ms-settings:startupapps", ShellLaunch.StartupAppsSettings);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file://server/share/x.txt")]
    [InlineData("ms-settings:privacy")]
    [InlineData("relative.txt")]
    public void A_refused_link_is_never_handed_to_the_shell(string destination)
    {
        Assert.Equal(LinkOpening.Refused, ShellLaunch.OpenLink(destination, confirmed: true));
    }

    [Fact]
    public void A_local_target_that_is_gone_is_reported_not_opened()
    {
        string missing = Path.Join(Path.GetTempPath(), "noto-link-" + Guid.NewGuid().ToString("N"), "gone.pdf");

        Assert.Equal(LinkOpening.Missing, ShellLaunch.OpenLink(Noto.Core.Links.LinkTarget.ForPath(missing), confirmed: true));
    }

    [Fact]
    public void A_program_is_not_opened_without_the_users_agreement()
    {
        string folder = Directory.CreateTempSubdirectory("noto-link-").FullName;
        try
        {
            string program = Path.Join(folder, "run.cmd");
            File.WriteAllText(program, "exit /b 0");
            string link = Noto.Core.Links.LinkTarget.ForPath(program);

            LinkCheck check = ShellLaunch.Inspect(link);
            Assert.True(check is { Exists: true, IsFolder: false, AsksFirst: true });
            Assert.Equal(LinkOpening.NotConfirmed, ShellLaunch.OpenLink(link));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_folder_and_a_document_open_without_asking()
    {
        string folder = Directory.CreateTempSubdirectory("noto-link-").FullName;
        try
        {
            string document = Path.Join(folder, "notes.txt");
            File.WriteAllText(document, "x");

            Assert.True(ShellLaunch.Inspect(Noto.Core.Links.LinkTarget.ForPath(folder)) is { Exists: true, IsFolder: true, AsksFirst: false });
            Assert.True(ShellLaunch.Inspect(Noto.Core.Links.LinkTarget.ForPath(document)) is { Exists: true, IsFolder: false, AsksFirst: false });
            Assert.True(ShellLaunch.Inspect("https://example.com") is { Kind: Noto.Core.Links.LinkKind.Web, Exists: true, AsksFirst: false });
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Every_launch_of_a_note_link_goes_through_OpenLink()
    {
        // The one checked entry point (ADR-019 §2): nothing else in the application hands a destination to the shell.
        string windows = Path.GetDirectoryName(typeof(ShellLaunchTests).Assembly.Location)!;
        for (var d = new DirectoryInfo(windows); d is not null; d = d.Parent)
        {
            string src = Path.Join(d.FullName, "src");
            if (Directory.Exists(Path.Join(src, "Noto.Windows")))
            {
                string code = string.Join('\n', Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
                    .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                    .Select(File.ReadAllText));
                Assert.Equal(1, System.Text.RegularExpressions.Regex.Count(code, @"Process\.Start\("));
                Assert.DoesNotMatch(@"Launcher\.Launch(Uri|File|Folder)", code);
                return;
            }
        }

        Assert.Fail("src not found");
    }
}
