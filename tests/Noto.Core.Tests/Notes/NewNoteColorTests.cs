using Noto.Core.Notes;
using Noto.Core.Settings;
using Xunit;

namespace Noto.Core.Tests.Notes;

/// <summary>
/// #245's two appearance settings: the colour a new note starts with (J24), none by default, and whether a
/// <c>#rrggbb</c> code shows its chip (F14, J18), on by default.
/// </summary>
public sealed class NewNoteColorTests
{
    [Fact]
    public void New_note_colour_is_an_appearance_setting_that_defaults_to_none()
    {
        // None is every note before the setting existed, so nobody's new notes change until they choose a colour.
        Assert.Equal("appearance.new-note-color", SettingKeys.AppearanceNewNoteColor.Name);
        Assert.Equal(string.Empty, SettingKeys.AppearanceNewNoteColor.Default);
        Assert.Equal(SettingValueKind.Text, SettingKeys.AppearanceNewNoteColor.Kind);
        Assert.Contains(SettingKeys.AppearanceNewNoteColor, SettingKeys.All);
    }

    [Fact]
    public void Only_none_and_the_six_palette_keys_are_valid()
    {
        Assert.True(SettingKeys.AppearanceNewNoteColor.IsValid(string.Empty));
        Assert.All(NoteColor.Keys, key => Assert.True(SettingKeys.AppearanceNewNoteColor.IsValid(key)));
        Assert.False(SettingKeys.AppearanceNewNoteColor.IsValid("note7"));
        Assert.False(SettingKeys.AppearanceNewNoteColor.IsValid("NOTE1"));
        Assert.False(SettingKeys.AppearanceNewNoteColor.IsValid("Blue"));
        Assert.False(SettingKeys.AppearanceNewNoteColor.IsValid(" "));
    }

    [Fact]
    public void Colour_chips_are_an_appearance_setting_on_by_default()
    {
        // On is #155's look exactly.
        Assert.Equal("appearance.color-chips", SettingKeys.AppearanceColorChips.Name);
        Assert.True(SettingKeys.AppearanceColorChips.Default);
        Assert.Equal(SettingValueKind.Boolean, SettingKeys.AppearanceColorChips.Kind);
        Assert.Contains(SettingKeys.AppearanceColorChips, SettingKeys.All);
    }
}
