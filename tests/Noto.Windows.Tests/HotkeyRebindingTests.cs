using Noto.Core.Activation;
using Noto.Platform.Windows;
using Xunit;
using Xunit.Abstractions;

namespace Noto.Windows.Tests;

/// <summary>
/// Slice 1.2b: rebinding the show/hide hotkey never leaves Noto without one. The procedure is tested against recorded
/// effects (the order is the property), then against real registrations, with another owner holding a chord.
/// </summary>
public sealed class HotkeyRebindingTests(ITestOutputHelper output)
{
    private static readonly HotkeyChord Old = HotkeyChord.Parse("Ctrl+Alt+Win+Space");
    private static readonly HotkeyChord New = HotkeyChord.Parse("Ctrl+Alt+Win+K");

    // ---------------------------------------------------------------- the order

    [Fact]
    public void A_valid_chord_is_registered_before_the_old_is_released_and_stored_only_after_both()
    {
        var world = new World(Old);

        var result = world.Rebind(New);

        Assert.Equal(HotkeyRebindOutcome.Rebound, result.Outcome);
        Assert.Equal(["register Ctrl+Alt+Win+K", "release Ctrl+Alt+Win+SPACE", "persist Ctrl+Alt+Win+K"], world.Log);
        Assert.Equal(New, result.Active!.Chord);
        Assert.Equal(New, world.Stored);
        Assert.Equal([New], world.Held);
    }

    [Theory]
    [InlineData(HotkeyRegistrationStatus.Conflict)]
    [InlineData(HotkeyRegistrationStatus.Failed)]
    public void A_refused_chord_leaves_the_old_registered_and_stored_and_nothing_written(HotkeyRegistrationStatus refusal)
    {
        // Conflict: another process holds it. Failed: Windows reserves or rejects it.
        var world = new World(Old) { Refuse = { [New] = refusal } };
        var before = world.Active;

        var result = world.Rebind(New);

        Assert.Equal(HotkeyRebindOutcome.Refused, result.Outcome);
        Assert.Equal(refusal, result.Refusal);
        Assert.Equal(["register Ctrl+Alt+Win+K"], world.Log);
        Assert.Same(before, result.Active);
        Assert.Equal(Old, world.Stored);
        Assert.Equal([Old], world.Held);
    }

    [Fact]
    public void The_chord_already_registered_is_unchanged_and_touches_nothing()
    {
        var world = new World(Old);
        var before = world.Active;

        var result = world.Rebind(Old);

        Assert.Equal(HotkeyRebindOutcome.Unchanged, result.Outcome);
        Assert.Empty(world.Log);
        Assert.Same(before, result.Active);
    }

    [Fact]
    public void With_nothing_registered_the_stored_chord_can_be_taken_again()
    {
        // Refused at launch: the same chord is a fresh attempt, and nothing is released because nothing is held.
        var world = new World(Old, registered: false);

        var result = world.Rebind(Old);

        Assert.Equal(HotkeyRebindOutcome.Rebound, result.Outcome);
        Assert.Equal(["register Ctrl+Alt+Win+SPACE", "persist Ctrl+Alt+Win+SPACE"], world.Log);
    }

    [Fact]
    public void A_store_failure_after_registering_brings_both_back_to_the_old_chord()
    {
        var world = new World(Old) { PersistFails = true };

        var result = world.Rebind(New);

        Assert.Equal(HotkeyRebindOutcome.NotSavedRestored, result.Outcome);
        Assert.Equal(["register Ctrl+Alt+Win+K", "release Ctrl+Alt+Win+SPACE", "persist Ctrl+Alt+Win+K", "register Ctrl+Alt+Win+SPACE", "release Ctrl+Alt+Win+K"], world.Log);
        Assert.Equal(Old, result.Active!.Chord);
        Assert.Equal(Old, world.Stored);
        Assert.Equal([Old], world.Held);
    }

    [Fact]
    public void A_store_failure_when_the_old_chord_cannot_be_had_back_keeps_the_new_one_and_says_so()
    {
        // Noto is never left without a shortcut: the new one stays for the session; the stored, old one is used next start.
        var world = new World(Old) { PersistFails = true, Refuse = { [Old] = HotkeyRegistrationStatus.Conflict } };

        var result = world.Rebind(New);

        Assert.Equal(HotkeyRebindOutcome.NotSavedKeptNew, result.Outcome);
        Assert.Equal(New, result.Active!.Chord);
        Assert.Equal([New], world.Held);
        Assert.Equal(Old, world.Stored);
        Assert.Contains("could not be saved", HotkeyRebinding.Describe(result.Outcome, result.Refusal, New, Old), StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- what is announced

    [Fact]
    public void Each_outcome_says_what_is_in_effect()
    {
        Assert.Equal("The shortcut is now Ctrl+Alt+Win+K.", HotkeyRebinding.Describe(HotkeyRebindOutcome.Rebound, HotkeyRegistrationStatus.Registered, New, Old));
        Assert.Equal("Ctrl+Alt+Win+K is in use by another app. The shortcut is still Ctrl+Alt+Win+Space.", HotkeyRebinding.Describe(HotkeyRebindOutcome.Refused, HotkeyRegistrationStatus.Conflict, New, Old));
        Assert.Contains("Windows did not accept Ctrl+Alt+Win+K", HotkeyRebinding.Describe(HotkeyRebindOutcome.Refused, HotkeyRegistrationStatus.Failed, New, Old), StringComparison.Ordinal);
        Assert.Contains("still Ctrl+Alt+Win+Space", HotkeyRebinding.Describe(HotkeyRebindOutcome.NotSavedRestored, HotkeyRegistrationStatus.Registered, New, Old), StringComparison.Ordinal);
    }

    [Fact]
    public void A_ctrl_alt_letter_chord_is_allowed_with_the_altgr_warning()
    {
        HotkeyChord altGr = HotkeyChord.Parse("Ctrl+Alt+K");

        Assert.Contains("AltGr", HotkeyRebinding.Describe(HotkeyRebindOutcome.Rebound, HotkeyRegistrationStatus.Registered, altGr, Old), StringComparison.Ordinal);
        Assert.DoesNotContain("AltGr", HotkeyRebinding.Describe(HotkeyRebindOutcome.Rebound, HotkeyRegistrationStatus.Registered, New, Old), StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- the recorder's reading of a key

    [Theory]
    [InlineData('K', HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Win, "Ctrl+Alt+Win+K")]
    [InlineData('7', HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, "Ctrl+Shift+7")]
    [InlineData(0x70, HotkeyModifiers.Win, "Win+F1")]
    [InlineData(0x87, HotkeyModifiers.Alt, "Alt+F24")]
    [InlineData(0x20, HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Win, "Ctrl+Alt+Win+SPACE")]
    public void A_valid_key_with_a_primary_modifier_is_a_chord(int virtualKey, HotkeyModifiers held, string canonical)
    {
        (RecordedKind kind, HotkeyChord chord) = HotkeyRecording.Read(virtualKey, held);

        Assert.Equal(RecordedKind.Chord, kind);
        Assert.Equal(canonical, chord.Canonical);
    }

    [Theory]
    [InlineData('K', HotkeyModifiers.None)]
    [InlineData('K', HotkeyModifiers.Shift)]
    [InlineData(0x20, HotkeyModifiers.None)]
    public void An_allowed_key_without_ctrl_alt_or_win_is_rejected(int virtualKey, HotkeyModifiers held)
    {
        Assert.Equal(RecordedKind.NeedsModifier, HotkeyRecording.Read(virtualKey, held).Kind);
    }

    [Theory]
    [InlineData(0x0D)]   // Enter
    [InlineData(0x09)]   // Tab
    [InlineData(0x25)]   // Left
    [InlineData(0xBC)]   // the comma
    [InlineData(0x88)]   // F25 does not exist in the grammar
    public void A_key_outside_the_grammar_is_rejected(int virtualKey)
    {
        Assert.Equal(RecordedKind.Unsupported, HotkeyRecording.Read(virtualKey, HotkeyModifiers.Ctrl | HotkeyModifiers.Alt).Kind);
    }

    [Theory]
    [InlineData(0x10)]
    [InlineData(0x11)]
    [InlineData(0x12)]
    [InlineData(0x5B)]
    [InlineData(0x5C)]
    [InlineData(0xA2)]
    public void A_modifier_alone_waits_for_the_key(int virtualKey)
    {
        Assert.Equal(RecordedKind.Modifier, HotkeyRecording.Read(virtualKey, HotkeyModifiers.Ctrl).Kind);
        Assert.Equal("Ctrl+Alt+\u2026", HotkeyRecording.Partial(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt));
    }

    [Fact]
    public void Every_key_the_grammar_allows_can_be_recorded_and_registers_as_itself()
    {
        foreach (string key in HotkeyChord.SupportedKeys)
        {
            HotkeyChord chord = HotkeyChord.Parse("Ctrl+Alt+Win+" + key);
            (uint _, uint vk) = HotkeyNative.ToNative(chord);

            Assert.Equal((RecordedKind.Chord, chord), HotkeyRecording.Read((int)vk, chord.Modifiers));
        }
    }

    // ---------------------------------------------------------------- real registrations

    [Fact]
    [Trait("Category", DesktopSession.RequiresDesktopTrait)]
    public void With_real_registrations_a_chord_another_owner_holds_is_refused_and_the_old_keeps_working()
    {
        if (!DesktopSession.ShouldRun(out string reason))
        {
            output.WriteLine(reason);
            return;
        }

        // Unusual chords no other test uses (tests run in parallel), so no real shortcut is touched.
        HotkeyChord current = HotkeyChord.Parse("Ctrl+Alt+Shift+Win+F17");
        HotkeyChord taken = HotkeyChord.Parse("Ctrl+Alt+Shift+Win+F18");
        HotkeyChord free = HotkeyChord.Parse("Ctrl+Alt+Shift+Win+F19");

        using var holder = GlobalHotkey.Create();
        Assert.True(holder.Register(taken).IsRegistered);   // another owner, legitimately holding it

        var old = GlobalHotkey.Create();
        Assert.True(old.Register(current).IsRegistered);
        GlobalHotkey? active = old;
        HotkeyChord stored = current;

        try
        {
            var refused = HotkeyRebinding.Rebind(taken, stored, active, Real, static h => h.Dispose(), c => { stored = c; return true; });

            Assert.Equal(HotkeyRebindOutcome.Refused, refused.Outcome);
            Assert.Equal(HotkeyRegistrationStatus.Conflict, refused.Refusal);
            Assert.Same(old, refused.Active);
            Assert.True(old.IsRegistered);
            Assert.False(IsFree(current));   // still held: the old chord still works
            Assert.Equal(current, stored);

            var rebound = HotkeyRebinding.Rebind(free, stored, active, Real, static h => h.Dispose(), c => { stored = c; return true; });
            active = rebound.Active;

            Assert.Equal(HotkeyRebindOutcome.Rebound, rebound.Outcome);
            Assert.True(rebound.Active!.IsRegistered);
            Assert.True(IsFree(current));    // released
            Assert.False(IsFree(free));      // held by the new registration
            Assert.Equal(free, stored);
        }
        finally
        {
            active?.Dispose();
            old.Dispose();
        }

        static (HotkeyRegistration, GlobalHotkey?) Real(HotkeyChord chord)
        {
            var hotkey = GlobalHotkey.Create();
            HotkeyRegistration registration = hotkey.Register(chord);

            if (registration.IsRegistered)
            {
                return (registration, hotkey);
            }

            hotkey.Dispose();
            return (registration, null);
        }

        static bool IsFree(HotkeyChord chord)
        {
            using var probe = GlobalHotkey.Create();
            return probe.Register(chord).IsRegistered;
        }
    }

    // ---------------------------------------------------------------- the recorded world

    private sealed record Handle(HotkeyChord Chord);

    /// <summary>Registrations and the store as a log, with Windows' refusals and the store's failure set by the test.</summary>
    private sealed class World
    {
        public World(HotkeyChord stored, bool registered = true)
        {
            Stored = stored;

            if (registered)
            {
                Active = new Handle(stored);
                Held.Add(stored);
            }
        }

        public List<string> Log { get; } = [];

        public List<HotkeyChord> Held { get; } = [];

        public Dictionary<HotkeyChord, HotkeyRegistrationStatus> Refuse { get; } = [];

        public bool PersistFails { get; init; }

        public HotkeyChord Stored { get; private set; }

        public Handle? Active { get; }

        public HotkeyRebindResult<Handle> Rebind(HotkeyChord candidate) =>
            HotkeyRebinding.Rebind(candidate, Stored, Active, Register, Release, Persist);

        private (HotkeyRegistration, Handle?) Register(HotkeyChord chord)
        {
            Log.Add($"register {chord}");

            if (Refuse.TryGetValue(chord, out HotkeyRegistrationStatus refusal) || Held.Contains(chord))
            {
                return (new HotkeyRegistration(Held.Contains(chord) ? HotkeyRegistrationStatus.Conflict : refusal, 1409), null);
            }

            Held.Add(chord);
            return (new HotkeyRegistration(HotkeyRegistrationStatus.Registered, 0), new Handle(chord));
        }

        private void Release(Handle handle)
        {
            Log.Add($"release {handle.Chord}");
            Held.Remove(handle.Chord);
        }

        private bool Persist(HotkeyChord chord)
        {
            Log.Add($"persist {chord}");

            if (PersistFails)
            {
                return false;
            }

            Stored = chord;
            return true;
        }
    }
}
