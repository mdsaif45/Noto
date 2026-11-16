namespace Noto.Core.Workspace;

/// <summary>
/// A folder row's or a note card's state as assistive technology says it (#201, ADR-016 §8): the one source for the
/// state words in a row's name, help text, item status and full description, so they never disagree.
/// </summary>
/// <remarks>
/// Narrator reads a list item's name and position, never its help text (measured on v0.2: "Work, 1 of 2", never
/// "Pinned"), so the state goes in the name too, after the title. The colour and mode names are the caller's: they are
/// the theme's and the menu's words.
/// </remarks>
public static class RowState
{
    /// <summary>
    /// The state words, comma-separated, in this order: "Pinned"; "Folded" and the lines it hides; then each other
    /// non-empty word given (a colour, a mode, a count). Empty when there are none.
    /// </summary>
    public static string Of(bool pinned, bool folded, int hiddenLines, params string?[] others)
    {
        ArgumentNullException.ThrowIfNull(others);

        List<string> parts = [];

        if (pinned)
        {
            parts.Add("Pinned");
        }

        if (folded)
        {
            parts.Add("Folded");

            if (hiddenLines > 0)
            {
                parts.Add(hiddenLines == 1 ? "1 line hidden" : $"{hiddenLines} lines hidden");
            }
        }

        parts.AddRange(others.OfType<string>().Where(o => o.Length > 0));
        return string.Join(", ", parts);
    }

    /// <summary>A row's name: its title, then its state when it has one ("Work, Pinned"); the title alone otherwise.</summary>
    public static string Name(string title, string state) => string.IsNullOrEmpty(state) ? title : $"{title}, {state}";
}
