using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Noto;

/// <summary>
/// The files a drop carries, as paths (#31). A file from Explorer has its own path. A picture dragged from a web page,
/// or an attachment from a mail app, is a virtual file with no path (measured from Edge: <c>FileGroupDescriptorW</c>
/// and <c>FileContents</c>, no <c>FileDrop</c>): it is copied first, into a folder of its own under the temporary
/// folder, so the same import and picture paths take it. The copies are removed once used.
/// </summary>
internal static class DroppedItems
{
    /// <summary>Each dropped item's path, a virtual file's copy's for one with none; and the folder of copies, if any.</summary>
    /// <remarks>Read while the drop lasts: a virtual file's source may be gone once it ends.</remarks>
    public static async Task<(IReadOnlyList<string> Paths, string? Copies)> PathsOf(DataPackageView view)
    {
        var paths = new List<string>();
        string? copies = null;

        foreach (IStorageItem item in await view.GetStorageItemsAsync())
        {
            if (Path.IsPathFullyQualified(item.Path))
            {
                paths.Add(item.Path);
            }
            else if (item is StorageFile file)
            {
                // Read as a stream and written with .NET's own file I/O: StorageFile.CopyAsync and StorageFolder need a
                // package identity, which unpackaged Noto has not (measured: 0x80073D54, and Noto ended).
                copies ??= Directory.CreateDirectory(Path.Join(Path.GetTempPath(), "Noto", "drops", Guid.NewGuid().ToString("N"))).FullName;
                // Each in a folder of its own, under its own name: a picture note is titled by its file's name.
                string copy = Path.Join(Directory.CreateDirectory(Path.Join(copies, paths.Count.ToString(System.Globalization.CultureInfo.InvariantCulture))).FullName, SafeName(file.Name));
                try
                {
                    using Stream source = await file.OpenStreamForReadAsync();
                    using FileStream target = File.Create(copy);
                    await source.CopyToAsync(target);
                    paths.Add(copy);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or COMException)
                {
                    // One item that cannot be read is left out; the others are still taken.
                }
            }
        }

        return (paths, copies);
    }

    /// <summary>A virtual file's name, as a file name: never a path, never empty.</summary>
    private static string SafeName(string name)
    {
        string file = Path.GetFileName(name);
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            file = file.Replace(c, '_');
        }

        return file.Length > 0 ? file : "dropped";
    }

    /// <summary>Removes a drop's copies once Noto has taken what it keeps; best effort.</summary>
    public static void Release(string? copies)
    {
        if (copies is null)
        {
            return;
        }

        try
        {
            Directory.Delete(copies, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left in the temporary folder, which Windows clears.
        }
    }
}
