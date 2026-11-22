using Noto.Core.Editing;
using Noto.Core.Settings;
using Xunit;

namespace Noto.Core.Tests.Editing;

/// <summary>The Colour copy format (#257; parity J17): what a swatch copies for its code, in each format.</summary>
public sealed class ColorCopyFormatTests
{
    [Fact]
    public void The_format_is_an_editor_setting_that_defaults_to_as_written()
    {
        Assert.Equal("editor.color-copy-format", SettingKeys.EditorColorCopyFormat.Name);
        Assert.Equal(ColorCopyFormat.AsWritten, SettingKeys.EditorColorCopyFormat.Default);
        Assert.Equal(SettingValueKind.Enumeration, SettingKeys.EditorColorCopyFormat.Kind);
        Assert.Contains(SettingKeys.EditorColorCopyFormat, SettingKeys.All);
        Assert.Equal([ColorCopyFormat.AsWritten, ColorCopyFormat.Hex, ColorCopyFormat.Rgb, ColorCopyFormat.Hsl], Enum.GetValues<ColorCopyFormat>());
        Assert.False(SettingKeys.EditorColorCopyFormat.IsValid((ColorCopyFormat)4));
    }

    [Theory]
    [InlineData("#1e90ff", "#1e90ff", "#1E90FF", "rgb(30, 144, 255)", "hsl(210, 100%, 56%)")]
    [InlineData("#1E90FF", "#1E90FF", "#1E90FF", "rgb(30, 144, 255)", "hsl(210, 100%, 56%)")]
    [InlineData("#1E90ff", "#1E90ff", "#1E90FF", "rgb(30, 144, 255)", "hsl(210, 100%, 56%)")]
    [InlineData("#000000", "#000000", "#000000", "rgb(0, 0, 0)", "hsl(0, 0%, 0%)")]
    [InlineData("#ffffff", "#ffffff", "#FFFFFF", "rgb(255, 255, 255)", "hsl(0, 0%, 100%)")]
    [InlineData("#808080", "#808080", "#808080", "rgb(128, 128, 128)", "hsl(0, 0%, 50%)")]
    [InlineData("#ff0000", "#ff0000", "#FF0000", "rgb(255, 0, 0)", "hsl(0, 100%, 50%)")]
    [InlineData("#00ff00", "#00ff00", "#00FF00", "rgb(0, 255, 0)", "hsl(120, 100%, 50%)")]
    [InlineData("#0000ff", "#0000ff", "#0000FF", "rgb(0, 0, 255)", "hsl(240, 100%, 50%)")]
    public void Each_format_writes_the_colour(string code, string asWritten, string hex, string rgb, string hsl)
    {
        string text = $"Brand: {code}.";
        ColorCode colour = Assert.Single(ColorCode.Find(text));

        Assert.Equal(asWritten, colour.Format(ColorCopyFormat.AsWritten, text));
        Assert.Equal(hex, colour.Format(ColorCopyFormat.Hex, text));
        Assert.Equal(rgb, colour.Format(ColorCopyFormat.Rgb, text));
        Assert.Equal(hsl, colour.Format(ColorCopyFormat.Hsl, text));
    }

    [Theory]
    // A saturation of exactly 12.5% rounds up to 13 (banker's rounding would give 12).
    [InlineData("#090807", "hsl(30, 13%, 3%)")]
    // A hue of exactly 0.5 rounds up to 1 (banker's rounding would give 0).
    [InlineData("#c85150", "hsl(1, 52%, 55%)")]
    // A hue just under 360 rounds to 360, which is 0 again.
    [InlineData("#ff0001", "hsl(0, 100%, 50%)")]
    // A hue between 0 and -60 (red towards blue) wraps to the top of the circle.
    [InlineData("#ff0080", "hsl(330, 100%, 50%)")]
    public void Hsl_rounds_a_half_up_and_keeps_the_hue_under_360(string code, string hsl)
    {
        ColorCode colour = Assert.Single(ColorCode.Find(code));

        Assert.Equal(hsl, colour.Format(ColorCopyFormat.Hsl, code));
    }
}
