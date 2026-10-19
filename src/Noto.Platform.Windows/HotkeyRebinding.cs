using Noto.Core.Activation;

namespace Noto.Platform.Windows;

/// <summary>How a rebind of the show/hide hotkey ended (Slice 1).</summary>
public enum HotkeyRebindOutcome
{
    /// <summary>The new chord is registered, the old one released, and the new one stored.</summary>
    Rebound,

    /// <summary>The chord asked for is the one already registered: nothing was done.</summary>
    Unchanged,

    /// <summary>Windows refused the new chord (held by another process, or reserved): the old one is still registered and stored.</summary>
    Refused,

    /// <summary>The shortcut is turned off: there is nothing to rebind.</summary>
    Off,

    /// <summary>The new chord registered but could not be stored: the old one was registered again and the new one released.</summary>
    NotSavedRestored,

    /// <summary>
    /// The new chord registered but could not be stored, and the old one could not be registered again (something took
    /// it in between): the new one is kept for this session so Noto still has a shortcut; the stored, old one is tried
    /// at the next start. Reported, never silent.
    /// </summary>
    NotSavedKeptNew,
}

/// <summary>A rebind's outcome, Windows' reason if it refused, and the registration now active.</summary>
/// <typeparam name="THandle">One registration of one chord: <see cref="GlobalHotkey"/>, or a test double.</typeparam>
public readonly record struct HotkeyRebindResult<THandle>(HotkeyRebindOutcome Outcome, HotkeyRegistrationStatus Refusal, THandle? Active)
    where THandle : class;

/// <summary>
/// Rebinding the show/hide hotkey without ever leaving Noto without one (Slice 1), as a pure procedure over its three
/// effects so the order is tested without Windows.
/// </summary>
/// <remarks>
/// <para>
/// <b>The old chord stays registered until the new one is.</b> A failed registration therefore changes nothing: the old
/// chord still works and is still stored.
/// </para>
/// <code>
///   candidate == current and registered      -> Unchanged
///   register(candidate)
///     refused                                -> Refused          (old untouched, nothing stored)
///     registered
///       release(old)
///       persist(candidate)
///         stored                             -> Rebound
///         not stored                                             (Windows has new, the store has old)
///           register(current)
///             registered -> release(new)     -> NotSavedRestored (both agree on old again)
///             refused                        -> NotSavedKeptNew  (new kept for the session, reported)
/// </code>
/// <para>
/// The effects belong to their owners: registering and releasing are <see cref="GlobalHotkey"/>'s, each chord on its
/// own instance because one instance holds one chord; storing is the typed preferences'. <c>persist</c> reports a
/// failure by returning <see langword="false"/>; it does not throw.
/// </para>
/// </remarks>
public static class HotkeyRebinding
{
    /// <summary>Rebinds from <paramref name="current"/> to <paramref name="candidate"/>, in the safe order.</summary>
    /// <param name="candidate">The chord asked for, already valid by the grammar.</param>
    /// <param name="current">The stored chord.</param>
    /// <param name="active">The registration now held, or <see langword="null"/> when none is (refused at launch).</param>
    /// <param name="register">Registers a chord on a new registration; the handle is <see langword="null"/> when refused.</param>
    /// <param name="release">Releases one registration.</param>
    /// <param name="persist">Stores a chord; whether it was kept.</param>
    public static HotkeyRebindResult<THandle> Rebind<THandle>(
        HotkeyChord candidate,
        HotkeyChord current,
        THandle? active,
        Func<HotkeyChord, (HotkeyRegistration Registration, THandle? Handle)> register,
        Action<THandle> release,
        Func<HotkeyChord, bool> persist)
        where THandle : class
    {
        ArgumentNullException.ThrowIfNull(register);
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(persist);

        if (candidate == current && active is not null)
        {
            return new(HotkeyRebindOutcome.Unchanged, HotkeyRegistrationStatus.Registered, active);
        }

        (HotkeyRegistration registration, THandle? fresh) = register(candidate);

        if (!registration.IsRegistered || fresh is null)
        {
            return new(HotkeyRebindOutcome.Refused, registration.Status, active);
        }

        if (active is not null)
        {
            release(active);
        }

        if (persist(candidate))
        {
            return new(HotkeyRebindOutcome.Rebound, HotkeyRegistrationStatus.Registered, fresh);
        }

        // Windows has the new chord, the store the old: bring them back into agreement on the old one if it can be had.
        (HotkeyRegistration back, THandle? restored) = register(current);

        if (back.IsRegistered && restored is not null)
        {
            release(fresh);
            return new(HotkeyRebindOutcome.NotSavedRestored, HotkeyRegistrationStatus.Registered, restored);
        }

        return new(HotkeyRebindOutcome.NotSavedKeptNew, back.Status, fresh);
    }

    /// <summary>A chord as the user reads it: <c>Ctrl+Alt+Win+Space</c>.</summary>
    public static string Show(HotkeyChord chord) => chord.Key == "SPACE" ? chord.Canonical[..^"SPACE".Length] + "Space" : chord.Canonical;

    /// <summary>What Settings announces after a rebind: what is in effect now, and why.</summary>
    public static string Describe(HotkeyRebindOutcome outcome, HotkeyRegistrationStatus refusal, HotkeyChord candidate, HotkeyChord current) => outcome switch
    {
        HotkeyRebindOutcome.Rebound => $"The shortcut is now {Show(candidate)}."
            + (candidate.MayBeTypedWithAltGr ? $" On some keyboard layouts AltGr acts as Ctrl+Alt, so {Show(candidate)} may stop you typing a character with AltGr." : string.Empty),
        HotkeyRebindOutcome.Unchanged => $"{Show(candidate)} is already the shortcut.",
        HotkeyRebindOutcome.Refused when refusal == HotkeyRegistrationStatus.Conflict =>
            $"{Show(candidate)} is in use by another app. The shortcut is still {Show(current)}.",
        HotkeyRebindOutcome.Refused => $"Windows did not accept {Show(candidate)}. The shortcut is still {Show(current)}.",
        HotkeyRebindOutcome.Off => "Turn the shortcut on to change it.",
        HotkeyRebindOutcome.NotSavedRestored => $"{Show(candidate)} could not be saved. The shortcut is still {Show(current)}.",
        HotkeyRebindOutcome.NotSavedKeptNew =>
            $"{Show(candidate)} works now but could not be saved, and {Show(current)} could not be taken back. When Noto next starts, it uses {Show(current)}.",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };
}

/// <summary>What one key press means to the shortcut recorder (Slice 1).</summary>
public enum RecordedKind
{
    /// <summary>A modifier on its own: keep waiting for the key.</summary>
    Modifier,

    /// <summary>A key the grammar does not allow (arrows, Enter, Tab, punctuation).</summary>
    Unsupported,

    /// <summary>An allowed key without Ctrl, Alt or Win.</summary>
    NeedsModifier,

    /// <summary>A whole chord, valid by the grammar.</summary>
    Chord,
}

/// <summary>Turns a key press and the modifiers held into a chord, by the <see cref="HotkeyChord"/> grammar and nothing wider.</summary>
public static class HotkeyRecording
{
    /// <summary>Reads one key press.</summary>
    /// <param name="virtualKey">The pressed key's virtual-key code.</param>
    /// <param name="held">The modifiers held with it.</param>
    public static (RecordedKind Kind, HotkeyChord Chord) Read(int virtualKey, HotkeyModifiers held)
    {
        if (IsModifier(virtualKey))
        {
            return (RecordedKind.Modifier, default);
        }

        if (KeyFor(virtualKey) is not string key)
        {
            return (RecordedKind.Unsupported, default);
        }

        string text = string.Join('+', Names(held).Append(key));

        return HotkeyChord.TryParse(text, out HotkeyChord chord)
            ? (RecordedKind.Chord, chord)
            : (RecordedKind.NeedsModifier, default);
    }

    /// <summary>The modifiers held, as the user reads them, for "Ctrl+Alt+…" while recording.</summary>
    public static string Partial(HotkeyModifiers held) => string.Join('+', Names(held).Append("…"));

    /// <summary>The grammar's name for a virtual key: the inverse of what <see cref="GlobalHotkey"/> registers.</summary>
    internal static string? KeyFor(int virtualKey) => virtualKey switch
    {
        0x20 => "SPACE",
        >= 'A' and <= 'Z' => ((char)virtualKey).ToString(),
        >= '0' and <= '9' => ((char)virtualKey).ToString(),
        >= 0x70 and <= 0x87 => "F" + (virtualKey - 0x6F).ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => null,
    };

    // Shift, Ctrl, Alt (VK_SHIFT/CONTROL/MENU and their left/right forms) and the Windows keys.
    private static bool IsModifier(int virtualKey) => virtualKey is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or (>= 0xA0 and <= 0xA5);

    private static IEnumerable<string> Names(HotkeyModifiers held)
    {
        if (held.HasFlag(HotkeyModifiers.Ctrl))
        {
            yield return "Ctrl";
        }

        if (held.HasFlag(HotkeyModifiers.Alt))
        {
            yield return "Alt";
        }

        if (held.HasFlag(HotkeyModifiers.Shift))
        {
            yield return "Shift";
        }

        if (held.HasFlag(HotkeyModifiers.Win))
        {
            yield return "Win";
        }
    }
}
