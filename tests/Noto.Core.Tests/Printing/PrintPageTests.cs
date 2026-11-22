using Noto.Core.Printing;
using Xunit;

namespace Noto.Core.Tests.Printing;

public sealed class PrintPageTests
{
    [Fact]
    public void A_printer_that_marks_the_whole_sheet_gets_the_least_margin_on_every_side()
    {
        // US Letter at 96 per inch: 816 x 1056.
        PrintArea area = PrintPage.TextArea(816, 1056, 0, 0, 816, 1056);

        Assert.Equal(61.2, area.Left, 6);
        Assert.Equal(61.2, area.Right, 6);
        Assert.Equal(79.2, area.Top, 6);
        Assert.Equal(79.2, area.Bottom, 6);
        Assert.Equal(693.6, area.Width, 6);
        Assert.Equal(897.6, area.Height, 6);
    }

    [Fact]
    public void A_wider_unprintable_edge_wins_over_the_least_margin_side_by_side()
    {
        // An edge of 100 on the left and 20 on the right, 90 on top and 0 at the bottom.
        PrintArea area = PrintPage.TextArea(816, 1056, 100, 90, 696, 966);

        Assert.Equal(100, area.Left, 6);
        Assert.Equal(61.2, area.Right, 6);
        Assert.Equal(90, area.Top, 6);
        Assert.Equal(79.2, area.Bottom, 6);
        Assert.Equal(816 - 100 - 61.2, area.Width, 6);
        Assert.Equal(1056 - 90 - 79.2, area.Height, 6);
    }

    [Fact]
    public void Landscape_swaps_the_margins_with_the_sides()
    {
        PrintArea portrait = PrintPage.TextArea(816, 1056, 0, 0, 816, 1056);
        PrintArea landscape = PrintPage.TextArea(1056, 816, 0, 0, 1056, 816);

        Assert.Equal(portrait.Top, landscape.Left, 6);
        Assert.Equal(portrait.Left, landscape.Top, 6);
        Assert.Equal(portrait.Height, landscape.Width, 6);
        Assert.Equal(portrait.Width, landscape.Height, 6);
    }

    [Theory]
    [InlineData(0, 0, 0, 0, 0, 0)] // no sheet
    [InlineData(100, 100, 60, 60, 0, 0)] // edges wider than the sheet
    public void A_sheet_with_no_room_has_a_box_of_no_size(double width, double height, double x, double y, double imageableWidth, double imageableHeight)
    {
        PrintArea area = PrintPage.TextArea(width, height, x, y, imageableWidth, imageableHeight);

        Assert.Equal(0, area.Width);
        Assert.Equal(0, area.Height);
    }

    [Fact]
    public void A_picture_prints_at_most_two_thirds_of_the_text_height()
    {
        Assert.Equal(600, PrintPage.PictureMaxHeight(new PrintArea(0, 0, 0, 0, 500, 900)), 6);
    }
}
