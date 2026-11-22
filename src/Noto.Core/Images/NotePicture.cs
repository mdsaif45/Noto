namespace Noto.Core.Images;

/// <summary>What is behind a note saved or copied as a picture (#254; parity I4, J31). Stored by member name.</summary>
public enum PictureBackground
{
    /// <summary>The note's colour on the card's fill, as the stack draws it: the default.</summary>
    NoteColor,

    /// <summary>Nothing: the text on a transparent picture.</summary>
    Transparent,
}

/// <summary>The space around the note in its picture (#254; parity J32). Stored by member name.</summary>
public enum PictureMargins
{
    /// <summary>The note's text to the picture's edge.</summary>
    None,

    /// <summary>A little room: the default.</summary>
    Small,

    /// <summary>Room enough to frame the note.</summary>
    Large,
}

/// <summary>The picture's shape (#254; parity J33): the note's own, or a ratio reached by padding. Stored by member name.</summary>
public enum PictureShape
{
    /// <summary>As tall and wide as the note and its margins: the default.</summary>
    Fit,

    /// <summary>1:1.</summary>
    Square,

    /// <summary>4:3, landscape.</summary>
    FourByThree,

    /// <summary>16:9, landscape.</summary>
    SixteenByNine,
}

/// <summary>How a note is saved or copied as a picture (#254): Settings › Export pictures, read together.</summary>
/// <param name="Background">What is behind the note.</param>
/// <param name="Margins">The space around it.</param>
/// <param name="Shape">The picture's shape.</param>
/// <param name="Footer">Whether the note's modified date and "Noto" end it.</param>
public sealed record PictureExport(PictureBackground Background, PictureMargins Margins, PictureShape Shape, bool Footer);

/// <summary>A note picture's canvas and where the note sits on it, in DIPs (#254).</summary>
/// <param name="Width">The canvas's width.</param>
/// <param name="Height">The canvas's height.</param>
/// <param name="Left">The note's distance from the canvas's left edge.</param>
/// <param name="Top">The note's distance from the canvas's top edge.</param>
public readonly record struct PictureLayout(double Width, double Height, double Left, double Top);

/// <summary>
/// The geometry of a note saved or copied as a picture (#254): its margins, then padding to the chosen shape, the note
/// centred in it. Padding is only ever added, so a note is never cropped, however long.
/// </summary>
public static class NotePicture
{
    /// <summary>
    /// The longest side a picture is rendered at, in pixels: Direct3D's texture limit, past which rendering fails. A
    /// note taller than this at the screen's scale is rendered at a smaller one, whole.
    /// </summary>
    public const int MaxSide = 16_384;

    /// <summary>The margin on every side, in DIPs: the design system's large space (16), and twice its extra-large (48).</summary>
    public static double MarginOf(PictureMargins margins) => margins switch
    {
        PictureMargins.Small => 16,
        PictureMargins.Large => 48,
        _ => 0,
    };

    /// <summary>
    /// The canvas for a note of <paramref name="contentWidth"/> by <paramref name="contentHeight"/> DIPs: the note and
    /// its margins, padded wider or taller (never both) to reach <paramref name="shape"/>, the note centred. Sizes are
    /// whole DIPs, so the note's edges fall on pixels.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A size is not finite and positive.</exception>
    public static PictureLayout Layout(double contentWidth, double contentHeight, PictureMargins margins, PictureShape shape)
    {
        Check(contentWidth, nameof(contentWidth));
        Check(contentHeight, nameof(contentHeight));

        double content = Math.Ceiling(contentWidth);
        double tall = Math.Ceiling(contentHeight);
        double margin = MarginOf(margins);
        double width = content + (2 * margin);
        double height = tall + (2 * margin);

        // The ratio as whole numbers, compared by cross-multiplying: a canvas already at 16:9 gains nothing from rounding.
        (int across, int down) = shape switch
        {
            PictureShape.Square => (1, 1),
            PictureShape.FourByThree => (4, 3),
            PictureShape.SixteenByNine => (16, 9),
            _ => (0, 0),
        };

        if (across > 0)
        {
            if (width * down < height * across)
            {
                width = Math.Ceiling(height * across / down);
            }
            else
            {
                height = Math.Ceiling(width * down / across);
            }
        }

        return new PictureLayout(width, height, Math.Floor((width - content) / 2), Math.Floor((height - tall) / 2));
    }

    /// <summary>
    /// The scale to render a <paramref name="width"/> by <paramref name="height"/> DIP picture at: the screen's
    /// <paramref name="scale"/>, so its text is as crisp as on screen, unless that passes <see cref="MaxSide"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A size or the scale is not finite and positive.</exception>
    public static double ScaleWithin(double width, double height, double scale)
    {
        Check(width, nameof(width));
        Check(height, nameof(height));
        Check(scale, nameof(scale));
        return Math.Min(scale, MaxSide / Math.Max(width, height));
    }

    private static void Check(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(name, value, "A size is finite and positive.");
        }
    }
}
