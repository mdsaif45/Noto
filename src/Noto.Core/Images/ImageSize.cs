namespace Noto.Core.Images;

/// <summary>How large an image is drawn (ADR-019 §4).</summary>
public static class ImageSize
{
    /// <summary>
    /// An image of <paramref name="width"/> × <paramref name="height"/> pixels drawn within <paramref name="maxWidth"/>
    /// and <paramref name="maxHeight"/>: its shape kept, made smaller to fit, never larger than it is. An image with no
    /// size is drawn at none, as is one given no room.
    /// </summary>
    public static (double Width, double Height) Fit(int width, int height, double maxWidth, double maxHeight)
    {
        if (width <= 0 || height <= 0)
        {
            return (0, 0);
        }

        double scale = Math.Min(1, Math.Min(maxWidth / width, maxHeight / height));
        return (width * scale, height * scale);
    }
}
