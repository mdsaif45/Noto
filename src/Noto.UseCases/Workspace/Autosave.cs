namespace Noto.UseCases.Workspace;

/// <summary>
/// When unsaved text is written while the user types (parity B19, P2.1): once typing pauses for
/// <see cref="Quiet"/>, and never later than <see cref="Longest"/> after the text first became unsaved, so a long burst
/// of typing is written as it goes. Leaving, hiding and closing still save at once (ADR-016 §2).
/// </summary>
public static class Autosave
{
    /// <summary>How long typing must pause before the text is written.</summary>
    public static readonly TimeSpan Quiet = TimeSpan.FromSeconds(1);

    /// <summary>The longest any typed text stays unsaved, however steadily the user types.</summary>
    public static readonly TimeSpan Longest = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long to wait, from a change made now, before writing text that has been unsaved for
    /// <paramref name="unsavedFor"/>: <see cref="Quiet"/>, cut short so the write lands by <see cref="Longest"/>.
    /// </summary>
    public static TimeSpan Delay(TimeSpan unsavedFor) =>
        TimeSpan.FromTicks(Math.Clamp((Longest - unsavedFor).Ticks, 0, Quiet.Ticks));
}
