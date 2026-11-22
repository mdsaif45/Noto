namespace Noto.Core.Printing;

/// <summary>
/// Where a printed note's text goes on a sheet (#248): the arithmetic of a page, apart from WinUI, so its margins are
/// tested without a printer. Lengths are in the print dialog's units (1/96 inch), whatever they are, as long as they agree.
/// </summary>
public static class PrintPage
{
    /// <summary>The least margin on each side, as a share of the paper: 7.5%, the WindowsAppSDK Printing sample's (16 mm on A4).</summary>
    public const double MarginShare = 0.075;

    /// <summary>The most pages one note prints: a layout that never stops overflowing ends here, not in a dialog that waits forever.</summary>
    public const int MaxPages = 200;

    /// <summary>
    /// The text's box on a sheet of <paramref name="pageWidth"/> by <paramref name="pageHeight"/> whose printer can mark
    /// only its imageable rectangle: each margin is the wider of the printer's unprintable edge and
    /// <see cref="MarginShare"/> of the paper, so nothing is cut off and the text never runs to the paper's edge. A sheet
    /// with no room left has a box of no size, never a negative one.
    /// </summary>
    public static PrintArea TextArea(double pageWidth, double pageHeight, double imageableLeft, double imageableTop, double imageableWidth, double imageableHeight)
    {
        double left = Math.Max(imageableLeft, pageWidth * MarginShare);
        double top = Math.Max(imageableTop, pageHeight * MarginShare);
        double right = Math.Max(pageWidth - imageableLeft - imageableWidth, pageWidth * MarginShare);
        double bottom = Math.Max(pageHeight - imageableTop - imageableHeight, pageHeight * MarginShare);
        return new PrintArea(left, top, right, bottom, Math.Max(0, pageWidth - left - right), Math.Max(0, pageHeight - top - bottom));
    }

    /// <summary>
    /// The tallest a picture prints: two thirds of the text's height, so a picture never outgrows a page and its
    /// caption fits above it on the same one.
    /// </summary>
    public static double PictureMaxHeight(PrintArea area) => area.Height * 2 / 3;
}

/// <summary>A sheet's margins and the text's width and height between them (#248).</summary>
public readonly record struct PrintArea(double Left, double Top, double Right, double Bottom, double Width, double Height);
