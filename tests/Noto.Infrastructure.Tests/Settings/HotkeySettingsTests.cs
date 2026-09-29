using Noto.Core.Activation;
using Noto.Core.Settings;
using Noto.Core.Storage;
using Xunit;

namespace Noto.Infrastructure.Tests.Settings;

/// <summary>
/// The hotkey settings against real SQLite (#16 slice 4): the binding's
/// validity is the chord grammar, and #9's fallback rules apply unchanged.
/// </summary>
public sealed class HotkeySettingsTests : IDisposable
{
    private readonly SettingsTestContext _context = new();

    public void Dispose() => _context.Dispose();

    [Fact]
    public void A_missing_binding_is_the_default_chord()
    {
        var store = _context.LoadedStore();

        Assert.Equal("Ctrl+Alt+Win+Space", store.Read(SettingKeys.HotkeyBinding));
        Assert.True(HotkeyChord.TryParse(store.Read(SettingKeys.HotkeyBinding), out _));
    }

    [Theory]
    [InlineData("Ctrl+Shift+F5")]
    [InlineData(" win + alt + ctrl + space ")]
    public void A_valid_stored_binding_is_read_exactly_as_stored(string stored)
    {
        _context.SeedRaw("activation.hotkey.binding", stored);

        var store = _context.LoadedStore();

        Assert.True(store.TryRead(SettingKeys.HotkeyBinding, out string binding));
        Assert.Equal(stored, binding);
        Assert.Empty(_context.Log.Fallbacks);
    }

    [Theory]
    [InlineData("Ctrl+Hyper+Q")]
    [InlineData("Q")]
    [InlineData("Shift+A")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Enter")]
    public void A_malformed_binding_falls_back_to_the_default_is_reported_and_left_untouched(string stored)
    {
        _context.SeedRaw("activation.hotkey.binding", stored);

        var store = _context.LoadedStore();

        Assert.False(store.TryRead(SettingKeys.HotkeyBinding, out _));
        Assert.Equal("Ctrl+Alt+Win+Space", store.Read(SettingKeys.HotkeyBinding));
        Assert.Equal(
            ("activation.hotkey.binding", SettingFallbackReason.Invalid, "Text"),
            Assert.Single(_context.Log.Fallbacks));
        Assert.Equal(stored, _context.RawValueOf("activation.hotkey.binding"));
    }

    [Fact]
    public void Reading_the_default_never_persists_it()
    {
        _context.SeedRaw("activation.hotkey.binding", "Ctrl+Hyper+Q");

        var store = _context.LoadedStore();
        _ = store.Read(SettingKeys.HotkeyBinding);
        _ = store.Read(SettingKeys.HotkeyEnabled);

        Assert.Equal("Ctrl+Hyper+Q", _context.RawValueOf("activation.hotkey.binding"));
        Assert.Null(_context.RawValueOf("activation.hotkey.enabled"));
    }

    [Fact]
    public void A_malformed_binding_is_rejected_on_write()
    {
        var store = _context.LoadedStore();

        Assert.False(store.Write(SettingKeys.HotkeyBinding, "Ctrl+Hyper+Q"));
        Assert.Null(_context.RawValueOf("activation.hotkey.binding"));
    }

    [Theory]
    [InlineData("False", false)]
    [InlineData("True", true)]
    public void The_enabled_flag_round_trips(string stored, bool expected)
    {
        _context.SeedRaw("activation.hotkey.enabled", stored);

        Assert.Equal(expected, _context.LoadedStore().Read(SettingKeys.HotkeyEnabled));
    }
}
