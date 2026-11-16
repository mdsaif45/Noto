using static Noto.Platform.Windows.NativeMethods;

namespace Noto.Platform.Windows;

/// <summary>
/// A file or folder shortcut's icon and type name, as Explorer shows them (#196): the shell's own, through
/// <c>SHGetFileInfo</c>. Never a thumbnail: nothing here opens the file or reads what is in it, so a shortcut shows
/// what kind of thing it opens, not its content.
/// </summary>
/// <remarks>
/// Slow on a slow drive, and the shell asks it to be called off the UI thread: the caller runs it on the thread pool
/// and keeps what it gets. A path is never written to a trace or a log.
/// </remarks>
public static class ShellIcons
{
    /// <summary>
    /// The shell's icon for <paramref name="path"/>, large (32 px at 100 %) or small (16 px), as premultiplied BGRA
    /// pixels, and its type name ("PDF Document", "File folder"). <see langword="null"/> when the shell has none: the
    /// target is gone, or the shell refused.
    /// </summary>
    public static unsafe ShellIcon? Of(string path, bool large)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        SHFILEINFOW info;
        uint flags = SHGFI_ICON | SHGFI_TYPENAME | (large ? SHGFI_LARGEICON : SHGFI_SMALLICON);
        if (SHGetFileInfo(path, 0, &info, (uint)sizeof(SHFILEINFOW), flags) == 0 || info.hIcon == 0)
        {
            return null;
        }

        try
        {
            string type = new(info.szTypeName);
            return Pixels(info.hIcon) is ({ } pixels, int width, int height) ? new ShellIcon(pixels, width, height, type) : null;
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }

    /// <summary>
    /// An icon's pixels, top row first, alpha premultiplied as XAML's bitmaps take them. An old icon with no alpha of
    /// its own takes its transparency from its mask.
    /// </summary>
    private static unsafe (byte[]? Pixels, int Width, int Height) Pixels(nint icon)
    {
        ICONINFO parts;
        if (!GetIconInfo(icon, &parts))
        {
            return (null, 0, 0);
        }

        try
        {
            BITMAP bitmap;
            if (parts.hbmColor == 0 || GetObject(parts.hbmColor, sizeof(BITMAP), &bitmap) == 0 || bitmap.bmWidth <= 0 || bitmap.bmHeight <= 0)
            {
                return (null, 0, 0);
            }

            int width = bitmap.bmWidth;
            int height = bitmap.bmHeight;
            byte[]? colour = Bits(parts.hbmColor, width, height);
            byte[]? mask = colour is null ? null : Bits(parts.hbmMask, width, height);
            if (colour is null)
            {
                return (null, 0, 0);
            }

            bool hasAlpha = false;
            for (int i = 3; i < colour.Length && !hasAlpha; i += 4)
            {
                hasAlpha = colour[i] != 0;
            }

            for (int i = 0; i < colour.Length; i += 4)
            {
                // The mask is black where the icon is drawn.
                byte alpha = hasAlpha ? colour[i + 3] : (byte)(mask is not null && mask[i] != 0 ? 0 : 255);
                colour[i] = (byte)(colour[i] * alpha / 255);
                colour[i + 1] = (byte)(colour[i + 1] * alpha / 255);
                colour[i + 2] = (byte)(colour[i + 2] * alpha / 255);
                colour[i + 3] = alpha;
            }

            return (colour, width, height);
        }
        finally
        {
            DeleteObject(parts.hbmColor);
            DeleteObject(parts.hbmMask);
        }
    }

    /// <summary>A bitmap's pixels as 32-bit BGRA, top row first; <see langword="null"/> when GDI refuses.</summary>
    private static unsafe byte[]? Bits(nint handle, int width, int height)
    {
        if (handle == 0)
        {
            return null;
        }

        var header = new BITMAPINFO
        {
            biSize = 40,
            biWidth = width,
            biHeight = -height,
            biPlanes = 1,
            biBitCount = 32,
        };
        var pixels = new byte[width * height * 4];
        nint screen = GetDC(0);
        try
        {
            fixed (byte* bits = pixels)
            {
                return GetDIBits(screen, handle, 0, (uint)height, bits, &header, DIB_RGB_COLORS) == height ? pixels : null;
            }
        }
        finally
        {
            _ = ReleaseDC(0, screen);
        }
    }
}

/// <summary>A shell icon (<see cref="ShellIcons.Of"/>): premultiplied BGRA pixels, top row first, and the type's name.</summary>
public sealed record ShellIcon(byte[] Pixels, int Width, int Height, string TypeName);
