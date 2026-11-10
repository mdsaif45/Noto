namespace Noto.Core.Workspace;

/// <summary>One page of the welcome: its title, one line under it, and what it says.</summary>
public sealed record WelcomeStep(string Title, string Summary, IReadOnlyList<string> Lines);

/// <summary>
/// The welcome's four steps and where the reader is in them (ADR-021).
/// </summary>
/// <remarks>
/// <para>
/// Moving is bounded: <see cref="Back"/> on the first step and <see cref="Next"/> on the last do nothing and say so,
/// so a surface cannot step outside the tour however its buttons are pressed. Finishing is the surface's to do on the
/// last step; the tour only says it is there (<see cref="IsLast"/>).
/// </para>
/// <para>
/// The words are here, not in the surface, so what each step says is tested beside the order of the steps.
/// </para>
/// </remarks>
public sealed class WelcomeTour
{
    private WelcomeTour(IReadOnlyList<WelcomeStep> steps) => Steps = steps;

    /// <summary>The steps, in order.</summary>
    public IReadOnlyList<WelcomeStep> Steps { get; }

    /// <summary>The step shown, from 0.</summary>
    public int Index { get; private set; }

    /// <summary>The step shown.</summary>
    public WelcomeStep Current => Steps[Index];

    /// <summary>Whether this is the first step: there is nothing to go back to.</summary>
    public bool IsFirst => Index == 0;

    /// <summary>Whether this is the last step: the primary action finishes.</summary>
    public bool IsLast => Index == Steps.Count - 1;

    /// <summary>Where the reader is, in words, so it is never shown by colour alone: "Step 2 of 4".</summary>
    public string Progress => $"Step {Index + 1} of {Steps.Count}";

    /// <summary>The primary action's label: Next, and Get started on the last step.</summary>
    public string PrimaryLabel => IsLast ? "Get started" : "Next";

    /// <summary>
    /// The welcome, telling the reader the show/hide shortcut they have: <paramref name="hotkey"/>, or
    /// <see langword="null"/> when it is turned off.
    /// </summary>
    public static WelcomeTour For(string? hotkey) => new(
    [
        new(
            "Welcome to Noto",
            "Notes that wait at the edge of your screen.",
            [
                string.IsNullOrWhiteSpace(hotkey)
                    ? "Turn on Show and hide Noto in Settings to bring Noto up from any app."
                    : $"Press {hotkey} in any app to show or hide Noto.",
                "Noto's icon in the notification area shows it too.",
                "Ctrl+W puts Noto away. What you write is kept.",
            ]),
        new(
            "Organize",
            "Folders hold your notes.",
            [
                "Press + at the foot of the folder list, or Ctrl+N, to make a folder. Double-click it, or press Ctrl+Down, to open it.",
                "In a folder, Ctrl+N writes a new note.",
                "Right-click a folder or a note to pin it to the top, fold a note, or move it to another folder.",
                "Drag to put things in order, or press Ctrl+Alt+Shift+Up or Down.",
            ]),
        new(
            "Write",
            "Every note is a card.",
            [
                "Select a card to write in it. Noto saves as you type.",
                "Markdown works as you type: # for a heading, - for a list, **bold** for bold.",
                "Ctrl+B, Ctrl+I and Ctrl+K format text and add links. The Format button has the rest.",
                "Paste or drop a picture into a note to keep it there.",
                "The search button, or Ctrl+F, searches every note.",
            ]),
        new(
            "Your data",
            "Your notes stay on this PC.",
            [
                "No account and no cloud: Noto keeps everything in its data folder.",
                "Settings > Data backs everything up to one file, and restores it.",
                "Export saves every note as Markdown. Import adds Markdown files.",
                "Settings > Help shows this tour again.",
            ]),
    ]);

    /// <summary>Moves to the next step.</summary>
    /// <returns>Whether it moved: never past the last step.</returns>
    public bool Next()
    {
        if (IsLast)
        {
            return false;
        }

        Index++;
        return true;
    }

    /// <summary>Moves to the step before.</summary>
    /// <returns>Whether it moved: never before the first step.</returns>
    public bool Back()
    {
        if (IsFirst)
        {
            return false;
        }

        Index--;
        return true;
    }
}
