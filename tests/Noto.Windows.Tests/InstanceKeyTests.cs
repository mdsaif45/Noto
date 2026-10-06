using System.Diagnostics;
using System.Globalization;
using Noto.Platform.Windows;
using Xunit;
using Xunit.Abstractions;

namespace Noto.Windows.Tests;

/// <summary>
/// Which data root a key names (A17, ADR-013): every spelling of one folder
/// gives one key, and different folders or users never share one.
/// </summary>
public sealed partial class InstanceKeyTests(ITestOutputHelper output)
{
    private const string Alice = "S-1-5-21-1000-2000-3000-1001";

    // ------------------------------------------------------- normalisation

    [Theory]
    [InlineData(@"C:\Users\Alice\AppData\Local\Noto")]
    [InlineData(@"c:\users\alice\appdata\local\noto")]
    [InlineData(@"C:\USERS\ALICE\APPDATA\LOCAL\NOTO\")]
    [InlineData(@"C:\Users\Alice\AppData\Local\Noto\\")]
    [InlineData(@"C:/Users/Alice/AppData/Local/Noto/")]
    [InlineData(@"\\?\C:\Users\Alice\AppData\Local\Noto")]
    [InlineData(@"\\?\c:\users\alice\appdata\local\noto\")]
    public void Every_spelling_of_one_path_normalises_the_same(string path)
    {
        Assert.Equal(@"C:\USERS\ALICE\APPDATA\LOCAL\NOTO", InstanceKey.Normalize(path));
    }

    [Theory]
    [InlineData(@"\\?\UNC\server\share\Noto", @"\\SERVER\SHARE\NOTO")]
    [InlineData(@"\\server\share\Noto\", @"\\SERVER\SHARE\NOTO")]
    [InlineData(@"\\?\C:\", @"C:\")]
    [InlineData(@"C:\", @"C:\")]
    public void Network_paths_and_drive_roots_keep_their_shape(string path, string expected)
    {
        Assert.Equal(expected, InstanceKey.Normalize(path));
    }

    [Fact]
    public void Case_folding_is_invariant_not_the_current_culture()
    {
        // Under tr-TR, "i".ToUpper() is "İ". The key must not depend on the
        // culture Noto happens to start under.
        CultureInfo before = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            Assert.Equal(@"C:\NOTI", InstanceKey.Normalize(@"c:\noti"));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    // ---------------------------------------------------------------- hash

    [Fact]
    public void The_hash_is_sha256_of_the_normalised_path_cut_to_32_hex_characters()
    {
        // Computed independently: SHA-256 over the UTF-8 of the normalised
        // path, upper-case hex, first 32 characters. A change here renames
        // every running Noto's mutex and pipe.
        Assert.Equal("945217C9DB2C828660412C7870D3E4DE", InstanceKey.HashOf(@"C:\USERS\ALICE\APPDATA\LOCAL\NOTO"));
    }

    [Fact]
    public void The_key_is_stable_across_spellings_and_distinct_across_roots_and_users()
    {
        InstanceKey a = InstanceKey.From(Alice, @"c:\users\alice\appdata\local\noto\");
        InstanceKey b = InstanceKey.From(Alice, @"\\?\C:\Users\Alice\AppData\Local\Noto");
        InstanceKey otherRoot = InstanceKey.From(Alice, @"C:\Users\Alice\AppData\Local\NotoDev");
        InstanceKey otherUser = InstanceKey.From("S-1-5-21-1000-2000-3000-1002", @"C:\Users\Alice\AppData\Local\Noto");

        Assert.Equal(a.MutexName, b.MutexName);
        Assert.Equal(a.PipeName, b.PipeName);
        Assert.NotEqual(a.PathHash, otherRoot.PathHash);
        Assert.NotEqual(a.MutexName, otherUser.MutexName);
        Assert.NotEqual(a.PipeName, otherUser.PipeName);
    }

    [Fact]
    public void The_names_follow_the_documented_shape()
    {
        InstanceKey key = InstanceKey.From(Alice, @"C:\Users\Alice\AppData\Local\Noto");

        Assert.Equal($@"Global\Noto.Instance.{Alice}.945217C9DB2C828660412C7870D3E4DE", key.MutexName);
        Assert.Equal($"Noto.Instance.{Alice}.945217C9DB2C828660412C7870D3E4DE", key.PipeName);
        Assert.Matches("^[0-9A-F]{32}$", key.PathHash);
    }

    [Fact]
    public void The_names_never_contain_the_folder_path()
    {
        InstanceKey key = InstanceKey.From(Alice, @"C:\Users\Alice\Secret Project\Noto");

        Assert.DoesNotContain("SECRET", key.MutexName, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SECRET", key.PipeName, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------- physical resolution

    [Fact]
    public void A_real_folder_resolves_to_itself_whatever_the_case_or_trailing_separator()
    {
        using var root = new TempFolder();

        string upper = InstanceKey.ForDataRoot(root.Path.ToUpperInvariant()).PipeName;
        string lower = InstanceKey.ForDataRoot(root.Path.ToLowerInvariant() + @"\").PipeName;

        Assert.Equal(InstanceKey.ForDataRoot(root.Path).PipeName, upper);
        Assert.Equal(upper, lower);
    }

    [Fact]
    public void The_key_belongs_to_the_current_user()
    {
        using var root = new TempFolder();

        Assert.Equal(ProcessIdentity.CurrentUserSid(), InstanceKey.ForDataRoot(root.Path).UserSid);
    }

    [Fact]
    public void A_junction_resolves_to_the_folder_it_points_at()
    {
        using var root = new TempFolder();
        string target = Directory.CreateDirectory(Path.Join(root.Path, "data")).FullName;
        string junction = Path.Join(root.Path, "via-junction");

        Assert.Equal(0, Run("cmd.exe", $"/c mklink /J \"{junction}\" \"{target}\""));

        Assert.Equal(InstanceKey.ForDataRoot(target).PipeName, InstanceKey.ForDataRoot(junction).PipeName);
        Assert.NotEqual(InstanceKey.Normalize(junction), InstanceKey.Normalize(InstanceKey.PhysicalPathOf(junction)));
    }

    [Fact]
    public void A_symbolic_link_resolves_to_the_folder_it_points_at()
    {
        using var root = new TempFolder();
        string target = Directory.CreateDirectory(Path.Join(root.Path, "data")).FullName;
        string link = Path.Join(root.Path, "via-symlink");

        try
        {
            _ = Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Creating a symbolic link needs Developer Mode or the privilege.
            // The junction test covers reparse resolution without it.
            output.WriteLine($"Symbolic links are not permitted here ({ex.Message}). Not run — not passed.");
            return;
        }

        Assert.Equal(InstanceKey.ForDataRoot(target).PipeName, InstanceKey.ForDataRoot(link).PipeName);
    }

    [Fact]
    public void A_subst_drive_resolves_to_the_folder_behind_it()
    {
        using var root = new TempFolder();
        char? letter = FreeDriveLetter();

        if (letter is null)
        {
            output.WriteLine("No free drive letter for subst. Not run — not passed.");
            return;
        }

        string drive = $"{letter}:";
        Assert.Equal(0, Run("subst.exe", $"{drive} \"{root.Path}\""));

        try
        {
            Assert.Equal(InstanceKey.ForDataRoot(root.Path).PipeName, InstanceKey.ForDataRoot(drive + @"\").PipeName);
        }
        finally
        {
            _ = Run("subst.exe", $"{drive} /d");
        }
    }

    [Fact]
    public void A_short_name_resolves_to_the_long_one()
    {
        using var root = new TempFolder();
        string longName = Directory.CreateDirectory(Path.Join(root.Path, "a folder with a long name")).FullName;
        string shortName = ShortPathOf(longName);

        if (string.Equals(shortName, longName, StringComparison.OrdinalIgnoreCase))
        {
            output.WriteLine("8.3 names are disabled on this volume. Not run — not passed.");
            return;
        }

        Assert.Equal(InstanceKey.ForDataRoot(longName).PipeName, InstanceKey.ForDataRoot(shortName).PipeName);
    }

    [Fact]
    public void Two_different_folders_never_share_a_key()
    {
        using var a = new TempFolder();
        using var b = new TempFolder();

        Assert.NotEqual(InstanceKey.ForDataRoot(a.Path).PipeName, InstanceKey.ForDataRoot(b.Path).PipeName);
    }

    [Fact]
    public void A_missing_folder_is_an_error_not_a_guess()
    {
        Assert.Throws<System.ComponentModel.Win32Exception>(
            () => InstanceKey.ForDataRoot(Path.Join(Path.GetTempPath(), $"noto-missing-{Guid.NewGuid():N}")));
    }

    private static int Run(string file, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(file, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;

        process.WaitForExit();
        return process.ExitCode;
    }

    private static char? FreeDriveLetter()
    {
        var used = DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])).ToHashSet();

        for (char letter = 'Z'; letter >= 'M'; letter--)
        {
            if (!used.Contains(letter))
            {
                return letter;
            }
        }

        return null;
    }

    private static string ShortPathOf(string path)
    {
        char[] buffer = new char[1024];
        uint length = Native.GetShortPathName(path, buffer, (uint)buffer.Length);

        return length == 0 ? path : new string(buffer, 0, (int)length);
    }

    /// <summary>A folder under the temp directory, deleted afterwards. Never a Noto data root.</summary>
    private sealed class TempFolder : IDisposable
    {
        public TempFolder() =>
            Path = Directory.CreateDirectory(System.IO.Path.Join(System.IO.Path.GetTempPath(), "noto-instance-tests", Guid.NewGuid().ToString("N"))).FullName;

        public string Path { get; }

        public void Dispose()
        {
            // Junctions and links first, so the delete never follows one.
            foreach (string link in Directory.EnumerateDirectories(Path).Where(e => new DirectoryInfo(e).LinkTarget is not null))
            {
                Directory.Delete(link);
            }

            Directory.Delete(Path, recursive: true);
        }
    }

    private static partial class Native
    {
        [System.Runtime.InteropServices.LibraryImport("kernel32.dll", EntryPoint = "GetShortPathNameW", SetLastError = true, StringMarshalling = System.Runtime.InteropServices.StringMarshalling.Utf16)]
        public static partial uint GetShortPathName(string longPath, [System.Runtime.InteropServices.Out] char[] shortPath, uint length);
    }
}
