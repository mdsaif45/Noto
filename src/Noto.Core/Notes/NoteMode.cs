using Noto.Core.Editing;

namespace Noto.Core.Notes;

/// <summary>
/// How a note's text is read and written (#195; parity D27, D28, D45): as Markdown, as code, or as plain text.
/// </summary>
/// <remarks>
/// <para>
/// A reading of the same text, never a conversion: the content is stored exactly as typed in every mode, and a note
/// switched back to Markdown renders as it did before. Stored per note by member name (migration 5), and chosen for new
/// notes by <see cref="Settings.SettingKeys.NotesNewMode"/>.
/// </para>
/// <para>
/// Markdown is the default and the only mode that styles: Code and PlainText show the text exactly as typed, with no
/// markup hidden. Code is monospace with spell check off; PlainText keeps the note font and spell check.
/// </para>
/// </remarks>
public enum NoteMode
{
    /// <summary>Styled Markdown with its markup hidden (ADR-017): every note before #195, and the default.</summary>
    Markdown = 0,

    /// <summary>The text as typed, monospace, spell check off; Tab types <see cref="NoteModes.CodeIndent"/>.</summary>
    Code,

    /// <summary>The text as typed, in the note font, spell checked.</summary>
    PlainText,
}

/// <summary>The rules every surface shares for <see cref="NoteMode"/> (#195).</summary>
public static class NoteModes
{
    /// <summary>
    /// What Tab types in a Code note: four spaces, not a tab character. Four is the width most languages' style guides
    /// use and the width the parser already gives a tab (MarkdownBlock.Indent); spaces look the same in every editor the
    /// text is pasted into, which a tab does not.
    /// </summary>
    public const string CodeIndent = "    ";

    /// <summary>
    /// The mode stored as <paramref name="stored"/>: a member's name, exactly. Anything else, a missing value
    /// included, reads as <see cref="NoteMode.Markdown"/>, which is how every note read before it was stored.
    /// </summary>
    public static NoteMode Parse(string? stored) => stored switch
    {
        nameof(NoteMode.Code) => NoteMode.Code,
        nameof(NoteMode.PlainText) => NoteMode.PlainText,
        _ => NoteMode.Markdown,
    };

    /// <summary>Tab in a Code note: <see cref="CodeIndent"/> over the selection, the caret after it.</summary>
    public static TextEdit? IndentCode(string text, TextSelection selection)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new TextEdit(selection.Start, selection.Length, CodeIndent, TextSelection.Caret(selection.Start + CodeIndent.Length));
    }
}
