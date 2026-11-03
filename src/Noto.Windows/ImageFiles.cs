using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Noto;

/// <summary>Reading a kept image for the editor and the cards (ADR-019 §4).</summary>
internal static class ImageFiles
{
    /// <summary>
    /// The image's size in pixels, from its header, without decoding it; <see langword="null"/> when the file cannot be
    /// read or is not an image the system decodes.
    /// </summary>
    internal static async Task<(int Width, int Height)?> SizeOf(string path)
    {
        try
        {
            using IRandomAccessStream stream = await FileRandomAccessStream.OpenAsync(path, FileAccessMode.Read);
            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
            return ((int)decoder.OrientedPixelWidth, (int)decoder.OrientedPixelHeight);
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The image decoded at the width it is drawn, never at full resolution.</summary>
    internal static BitmapImage Bitmap(string path, double width) => new()
    {
        DecodePixelType = DecodePixelType.Logical,
        DecodePixelWidth = Math.Max(1, (int)Math.Ceiling(width)),
        UriSource = new Uri(path),
    };
}
