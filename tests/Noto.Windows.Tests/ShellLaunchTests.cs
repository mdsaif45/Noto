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
}
