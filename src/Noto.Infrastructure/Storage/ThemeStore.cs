using Noto.Core.Portability;
using Noto.Core.Themes;

namespace Noto.Infrastructure.Storage;

/// <summary>An installed theme file as Settings lists it (#269): <paramref name="Name"/> is <see langword="null"/> when it can't be read.</summary>
public sealed record InstalledTheme(string File, string? Name, string? Author);

/// <summary>
/// The installed theme files (#269, ADR-023 §8): the data root's <c>themes</c> folder, read each time it is listed, and
/// written only by an install. The folder is the list; nothing else records what is installed.
/// </summary>
/// <remarks>
/// A file is named by Noto from the theme's name, by the export's rules (<see cref="PortableNames"/>), never from the
/// file the user picked (ADR-019: a dropped name is untrusted). Its bytes are copied unchanged, comments included,
/// through a temporary file renamed into place: whole or not at all. Removing one is the Recycle Bin's, done by the
/// platform. No path is ever written to a trace or a log.
/// </remarks>
/// <param name="directory">The themes folder; it need not exist yet.</param>
public sealed class ThemeStore(string directory)
{
    /// <summary>The most files listed: a folder of thousands is not a theme collection worth reading at each open.</summary>
    public const int Limit = 100;

    /// <summary>The themes folder.</summary>
    public string Directory { get; } = directory ?? throw new ArgumentNullException(nameof(directory));

    /// <summary>
    /// The installed themes, by file name, at most <see cref="Limit"/>: only bare <c>*.nototheme</c> names, so another
    /// file put there is not listed. One that can't be read is listed with no name.
    /// </summary>
    public IReadOnlyList<InstalledTheme> List() =>
        [.. Files().Order(StringComparer.OrdinalIgnoreCase).Take(Limit).Select(file =>
        {
            ThemeFile? theme = Read(file) is { } bytes ? ThemeFile.Read(bytes, out _) : null;
            return new InstalledTheme(file, theme?.Name, theme?.Author);
        })];

    /// <summary>Where an installed file is: only a name the setting would keep is ever turned into a path.</summary>
    public string PathOf(string file)
    {
        if (file.Length == 0 || !ThemeFile.IsFileName(file))
        {
            throw new ArgumentException("Not an installed theme's file name.", nameof(file));
        }

        return Path.Join(Directory, file);
    }

    /// <summary>An installed file's bytes; <see langword="null"/> when it is gone, too large or can't be read.</summary>
    public byte[]? Read(string file) => ReadCapped(PathOf(file));

    /// <summary>
    /// A file's bytes, read only while it is no larger than <see cref="ThemeFile.MaxBytes"/> (§9: checked before reading,
    /// and never more than that read); <see langword="null"/> when it is larger, gone or can't be read.
    /// </summary>
    public static byte[]? ReadCapped(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > ThemeFile.MaxBytes)
            {
                return null;
            }

            var buffer = new byte[ThemeFile.MaxBytes + 1];
            int read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            return read > ThemeFile.MaxBytes ? null : buffer[..read];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Installs <paramref name="bytes"/>, a theme already checked, as the theme <paramref name="name"/>: over
    /// <paramref name="replacing"/> (the installed file of the same name), or as a new file named from it, with
    /// <c> (2)</c>, <c> (3)</c> when another name already took that file name.
    /// </summary>
    /// <returns>The installed file's name.</returns>
    /// <exception cref="IOException">It could not be written; nothing was installed or replaced.</exception>
    /// <exception cref="UnauthorizedAccessException">It could not be written; nothing was installed or replaced.</exception>
    public string Install(byte[] bytes, string name, string? replacing)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(name);

        System.IO.Directory.CreateDirectory(Directory);

        // A ".." in a name would make a file name the setting refuses; the rest of the export's rules already hold.
        string file = replacing ?? new NameAllocator(Files()).Take(PortableNames.Safe(name).Replace("..", "._", StringComparison.Ordinal), ThemeFile.Extension);
        string target = PathOf(file);
        string partial = Path.Join(Directory, $"{Guid.NewGuid():N}.partial");

        try
        {
            File.WriteAllBytes(partial, bytes);
            File.Move(partial, target, overwrite: true);
        }
        finally
        {
            File.Delete(partial);
        }

        return file;
    }

    private IEnumerable<string> Files() =>
        System.IO.Directory.Exists(Directory)
            ? System.IO.Directory.EnumerateFiles(Directory, "*" + ThemeFile.Extension).Select(Path.GetFileName).OfType<string>().Where(f => ThemeFile.IsFileName(f) && f.Length > 0)
            : [];
}
