using Noto.Core.Images;
using Noto.Core.Settings;
using Xunit;

namespace Noto.Core.Tests.Images;

public sealed class NotePictureTests
{
    [Theory]
    [InlineData(PictureMargins.None, 300, 200, 0, 0)]
    [InlineData(PictureMargins.Small, 332, 232, 16, 16)]
    [InlineData(PictureMargins.Large, 396, 296, 48, 48)]
    public void Fit_is_the_note_and_its_margins(PictureMargins margins, double width, double height, double left, double top)
    {
        Assert.Equal(new PictureLayout(width, height, left, top), NotePicture.Layout(300, 200, margins, PictureShape.Fit));
    }

    [Theory]
    [InlineData(PictureShape.Square, 300, 100, 300, 300, 0, 100)]          // wide: padded taller
    [InlineData(PictureShape.Square, 100, 300, 300, 300, 100, 0)]          // tall: padded wider
    [InlineData(PictureShape.FourByThree, 300, 1000, 1334, 1000, 517, 0)]   // a long note: padded wider, whole
    [InlineData(PictureShape.FourByThree, 400, 100, 400, 300, 0, 100)]
    [InlineData(PictureShape.SixteenByNine, 320, 90, 320, 180, 0, 45)]
    [InlineData(PictureShape.SixteenByNine, 160, 90, 160, 90, 0, 0)]       // already the ratio: nothing added
    public void A_shape_is_reached_by_padding_one_side_the_note_centred_and_never_cropped(PictureShape shape, double width, double height, double canvasWidth, double canvasHeight, double left, double top)
    {
        PictureLayout layout = NotePicture.Layout(width, height, PictureMargins.None, shape);

        Assert.Equal(new PictureLayout(canvasWidth, canvasHeight, left, top), layout);
        Assert.True(layout.Width >= width && layout.Height >= height);
        Assert.True(layout.Left + width <= layout.Width && layout.Top + height <= layout.Height);
    }

    [Fact]
    public void Margins_come_before_the_ratio()
    {
        // 300x100 with 48 on each side is 396x196; square pads it to 396x396, the note 148 from the top.
        Assert.Equal(new PictureLayout(396, 396, 48, 148), NotePicture.Layout(300, 100, PictureMargins.Large, PictureShape.Square));
    }

    [Fact]
    public void Sizes_are_whole_dips()
    {
        PictureLayout layout = NotePicture.Layout(300.4, 99.2, PictureMargins.Small, PictureShape.SixteenByNine);

        Assert.Equal(Math.Floor(layout.Width), layout.Width);
        Assert.Equal(Math.Floor(layout.Height), layout.Height);
        Assert.Equal(Math.Floor(layout.Left), layout.Left);
        Assert.Equal(Math.Floor(layout.Top), layout.Top);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, -1)]
    [InlineData(double.NaN, 100)]
    [InlineData(100, double.PositiveInfinity)]
    public void A_size_that_is_not_finite_and_positive_is_refused(double width, double height)
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => NotePicture.Layout(width, height, PictureMargins.Small, PictureShape.Fit));
    }

    [Fact]
    public void A_picture_renders_at_the_screen_scale_unless_that_passes_the_texture_limit()
    {
        Assert.Equal(1.5, NotePicture.ScaleWithin(400, 3000, 1.5));
        Assert.Equal(NotePicture.MaxSide / 20_000.0, NotePicture.ScaleWithin(400, 20_000, 1.5));
        Assert.True(20_000 * NotePicture.ScaleWithin(400, 20_000, 2) <= NotePicture.MaxSide);
    }

    [Fact]
    public void The_settings_default_to_the_note_colour_small_margins_its_own_shape_and_no_footer()
    {
        Assert.Equal(PictureBackground.NoteColor, SettingKeys.ExportingPictureBackground.Default);
        Assert.Equal(PictureMargins.Small, SettingKeys.ExportingPictureMargins.Default);
        Assert.Equal(PictureShape.Fit, SettingKeys.ExportingPictureShape.Default);
        Assert.False(SettingKeys.ExportingPictureFooter.Default);
        Assert.Contains(SettingKeys.ExportingPictureBackground, SettingKeys.All);
        Assert.Contains(SettingKeys.ExportingPictureMargins, SettingKeys.All);
        Assert.Contains(SettingKeys.ExportingPictureShape, SettingKeys.All);
        Assert.Contains(SettingKeys.ExportingPictureFooter, SettingKeys.All);
    }
}
