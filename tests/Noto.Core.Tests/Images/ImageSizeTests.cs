using Noto.Core.Images;
using Xunit;

namespace Noto.Core.Tests.Images;

public sealed class ImageSizeTests
{
    [Theory]
    [InlineData(200, 100, 400, 320, 200, 100)] // smaller than the room: never enlarged
    [InlineData(800, 400, 400, 320, 400, 200)] // wider: fits the width
    [InlineData(400, 800, 400, 320, 160, 320)] // taller: fits the height
    [InlineData(1000, 1000, 400, 320, 320, 320)] // both: the tighter one wins
    [InlineData(400, 320, 400, 320, 400, 320)] // exactly the room
    [InlineData(3000, 100, double.PositiveInfinity, 160, 3000, 100)] // a card with no known width
    public void An_image_keeps_its_shape_and_fits_without_growing(int width, int height, double maxWidth, double maxHeight, double expectedWidth, double expectedHeight)
    {
        (double drawnWidth, double drawnHeight) = ImageSize.Fit(width, height, maxWidth, maxHeight);

        Assert.Equal(expectedWidth, drawnWidth, 6);
        Assert.Equal(expectedHeight, drawnHeight, 6);
    }

    [Theory]
    [InlineData(0, 100, 400, 320)]
    [InlineData(100, 0, 400, 320)]
    [InlineData(-1, 100, 400, 320)]
    [InlineData(100, 100, 0, 320)]
    [InlineData(100, 100, 400, 0)]
    public void No_size_or_no_room_draws_nothing(int width, int height, double maxWidth, double maxHeight)
    {
        Assert.Equal((0d, 0d), ImageSize.Fit(width, height, maxWidth, maxHeight));
    }
}
