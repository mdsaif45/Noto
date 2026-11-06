using Noto.Platform.Windows;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// An installed copy (ADR-008): when Velopack's hooks run, and which copy takes the Start menu shortcut's identity.
/// </summary>
public sealed class InstalledCopyTests : IDisposable
{
    private readonly string _folder = Path.Join(Path.GetTempPath(), $"noto-installed-{Guid.NewGuid():N}");

    public InstalledCopyTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Theory]
    [InlineData(new[] { "Noto.exe", "--veloapp-install", "0.1.0" }, true)]
    [InlineData(new[] { "Noto.exe", "--veloapp-uninstall", "0.1.0" }, true)]
    [InlineData(new[] { "Noto.exe", "--VELOAPP-updated", "0.1.0" }, true)]
    [InlineData(new[] { "Noto.exe" }, false)]
    [InlineData(new[] { "Noto.exe", "--login" }, false)]
    [InlineData(new[] { "Noto.exe", "--data-root=C:\\x", "--veloapp-install" }, false)]
    [InlineData(new[] { "Noto.exe", "-veloapp-install", "0.1.0" }, false)]
    [InlineData(new[] { "Noto.exe", "--veloappinstall", "0.1.0" }, false)]
    public void Only_a_start_for_a_velopack_hook_is_a_hook(string[] commandLine, bool expected)
    {
        Assert.Equal(expected, InstalledCopy.IsHook(commandLine));
    }

    [Fact]
    public void A_copy_with_velopacks_manifest_beside_it_is_installed()
    {
        string executable = Path.Join(_folder, "Noto.exe");
        Assert.False(InstalledCopy.IsInstalled(executable));

        File.WriteAllText(Path.Join(_folder, "sq.version"), "<package />");

        Assert.True(InstalledCopy.IsInstalled(executable));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Noto.exe")]
    public void No_path_or_folder_is_not_installed(string? executable)
    {
        Assert.False(InstalledCopy.IsInstalled(executable));
    }

    [Fact]
    public void A_build_never_takes_the_installed_identity()
    {
        Assert.False(InstalledCopy.TakeShortcutIdentity(Path.Join(_folder, "Noto.exe")));
    }

    [Fact]
    public void An_installed_copy_takes_the_shortcuts_identity()
    {
        // Sets this test process's own identity: it has no window for the taskbar to group.
        File.WriteAllText(Path.Join(_folder, "sq.version"), "<package />");

        Assert.True(InstalledCopy.TakeShortcutIdentity(Path.Join(_folder, "Noto.exe")));
    }
}
