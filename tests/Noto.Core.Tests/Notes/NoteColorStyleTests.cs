using Noto.Core.Notes;
using Noto.Core.Settings;
using Xunit;

namespace Noto.Core.Tests.Notes;

/// <summary>The note colour style setting (#191; parity F10, J23): Background or Bar, Background the default.</summary>
public sealed class NoteColorStyleTests
{
    [Fact]
    public void The_style_is_an_appearance_setting_that_defaults_to_background()
    {
        // Background is #150's look exactly, so nobody's cards change until they choose Bar.
        Assert.Equal("appearance.note-color-style", SettingKeys.AppearanceNoteColorStyle.Name);
        Assert.Equal(NoteColorStyle.Background, SettingKeys.AppearanceNoteColorStyle.Default);
        Assert.Equal(SettingValueKind.Enumeration, SettingKeys.AppearanceNoteColorStyle.Kind);
        Assert.Contains(SettingKeys.AppearanceNoteColorStyle, SettingKeys.All);
    }

    [Fact]
    public void Only_the_two_styles_are_valid()
    {
        Assert.Equal([NoteColorStyle.Background, NoteColorStyle.Bar], Enum.GetValues<NoteColorStyle>());
        Assert.True(SettingKeys.AppearanceNoteColorStyle.IsValid(NoteColorStyle.Background));
        Assert.True(SettingKeys.AppearanceNoteColorStyle.IsValid(NoteColorStyle.Bar));
        Assert.False(SettingKeys.AppearanceNoteColorStyle.IsValid((NoteColorStyle)2));
    }
}
