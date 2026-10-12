using System.Buffers.Binary;
using System.Text.RegularExpressions;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// Noto's icon (#95): the asset's structure, and the three places it must
/// reach — the executable, the window and the tray — checked in the source.
/// </summary>
/// <remarks>
/// What the icon looks like on the real taskbar, tray and Alt+Tab is the
/// runtime check; these pin what makes it reach them at all.
/// </remarks>
public sealed class NotoIconTests
{
    private static readonly int[] Sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];

    [Fact]
    public void The_icon_holds_every_shell_size_as_32_bit_with_alpha()
    {
        byte[] ico = File.ReadAllBytes(SourcePath("src", "Noto.Windows", "Assets", "Noto.ico"));

        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(0)));   // reserved
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(2)));   // an icon, not a cursor
        int count = BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4));
        Assert.Equal(Sizes.Length, count);

        var found = new List<int>();

        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> entry = ico.AsSpan(6 + (16 * i), 16);
            int size = entry[0] == 0 ? 256 : entry[0];
            Assert.Equal(size, entry[1] == 0 ? 256 : entry[1]);
            Assert.Equal(32, BinaryPrimitives.ReadUInt16LittleEndian(entry[6..]));

            int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
            int offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);
            ReadOnlySpan<byte> png = ico.AsSpan(offset, length);

            // A PNG whose header says this size, 8 bits per channel, RGBA: alpha is real.
            Assert.True(png[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }), $"{size} px is not a PNG");
            Assert.Equal(size, BinaryPrimitives.ReadInt32BigEndian(png[16..]));
            Assert.Equal(size, BinaryPrimitives.ReadInt32BigEndian(png[20..]));
            Assert.Equal(8, png[24]);
            Assert.Equal(6, png[25]);
            found.Add(size);
        }

        Assert.Equal(Sizes, found);
    }

    [Fact]
    public void The_executable_embeds_the_icon_and_ships_it_for_the_window()
    {
        string project = File.ReadAllText(SourcePath("src", "Noto.Windows", "Noto.Windows.csproj"));

        Assert.Contains(@"<ApplicationIcon>Assets\Noto.ico</ApplicationIcon>", project, StringComparison.Ordinal);
        Assert.Contains(@"<Content Include=""Assets\Noto.ico"" CopyToOutputDirectory=""PreserveNewest"" />", project, StringComparison.Ordinal);
    }

    [Fact]
    public void The_window_takes_the_same_icon()
    {
        string app = Regex.Replace(File.ReadAllText(SourcePath("src", "Noto.Windows", "App.xaml.cs")), @"//.*", string.Empty);

        Assert.Contains(@"Path.Combine(AppContext.BaseDirectory, ""Assets"", ""Noto.ico"")", app, StringComparison.Ordinal);
        Assert.Contains("_window.AppWindow.SetIcon(icon);", app, StringComparison.Ordinal);
    }

    [Fact]
    public void The_tray_loads_the_executables_own_icon_before_the_generic_one()
    {
        string tray = Regex.Replace(File.ReadAllText(SourcePath("src", "Noto.Platform.Windows", "TrayIcon.cs")), @"//.*", string.Empty);
        int own = tray.IndexOf("NativeMethods.AppIconResource", StringComparison.Ordinal);
        int generic = tray.IndexOf("NativeMethods.LoadIcon(0, NativeMethods.IDI_APPLICATION)", StringComparison.Ordinal);

        Assert.True(own >= 0 && generic > own, "The executable's own icon comes first; the generic one is the fallback.");
        Assert.Contains("_ = NativeMethods.DestroyIcon(_icon);", tray, StringComparison.Ordinal);
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
