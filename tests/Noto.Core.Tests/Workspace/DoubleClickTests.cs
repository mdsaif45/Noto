using Noto.Core.Workspace;
using Xunit;

namespace Noto.Core.Tests.Workspace;

/// <summary>B12 (#190): a double-click on a card is Windows' rule, the second press soon after and close to the first.</summary>
public sealed class DoubleClickTests
{
    [Theory]
    [InlineData(1000, 10, 10, true)]   // the same place, at once
    [InlineData(1500, 10, 10, true)]   // at the double-click time exactly
    [InlineData(1501, 10, 10, false)]  // one millisecond late
    [InlineData(1200, 14, 6, true)]    // at the edge of the square
    [InlineData(1200, 14.5, 10, false)] // just outside it
    [InlineData(1200, 10, 5.5, false)]
    [InlineData(999, 10, 10, false)]   // before the first: a stale or reset clock is no double-click
    public void A_press_is_the_second_click_only_soon_after_and_close_to_the_first(long at, double x, double y, bool expected)
    {
        Assert.Equal(expected, DoubleClick.IsSecond(1000, 10, 10, at, x, y, doubleClickTime: 500));
    }
}
