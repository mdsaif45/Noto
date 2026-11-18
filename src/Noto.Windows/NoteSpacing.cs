using System.Diagnostics;
using Noto.Core.Workspace;

namespace Noto;

/// <summary>
/// The note text's line height and paragraph spacing shown now (#234; parity D49, D50, J14, J15): the one place the
/// editor and every card's read view take them from, so a card looks the same reading and editing. What each preset
/// measures is Core's (<see cref="NoteText"/>).
/// </summary>
/// <remarks>
/// Not a property the views inherit, as the family and size are (F13): XAML has no inherited property of its own, so
/// a change is announced, and each view loaded draws itself again (<see cref="MarkdownReadView"/>,
/// <see cref="NoteEditorHost"/>); a view loaded later draws with what is set then.
/// </remarks>
internal static class NoteSpacing
{
    /// <summary>The line height's preset now.</summary>
    internal static TextSpacing LineHeight { get; private set; } = TextSpacing.Normal;

    /// <summary>The paragraph spacing's preset now.</summary>
    internal static TextSpacing Paragraphs { get; private set; } = TextSpacing.Normal;

    /// <summary>The presets changed: every view loaded draws itself again.</summary>
    internal static event EventHandler? Changed;

    /// <summary>A line's height, as a multiple of its text's size; 0 for the font's own (Normal).</summary>
    internal static double LineMultiple => NoteText.LineHeight(LineHeight);

    /// <summary>The space after each paragraph, in DIPs, at the note text's <paramref name="size"/>.</summary>
    internal static double SpaceAfter(double size) => NoteText.SpaceAfter(Paragraphs, size);

    /// <summary>Whether a heading after the first line keeps its space above.</summary>
    internal static bool SpacesHeadings => NoteText.SpacesHeadings(Paragraphs);

    /// <summary>Shows the note text in these presets, from now and in every view loaded now. Set by the application.</summary>
    internal static void Apply(TextSpacing lineHeight, TextSpacing paragraphs)
    {
        if (lineHeight == LineHeight && paragraphs == Paragraphs)
        {
            return;
        }

        LineHeight = lineHeight;
        Paragraphs = paragraphs;
        Changed?.Invoke(null, EventArgs.Empty);
        Trace.WriteLine($"Noto.Settings note-spacing line-height={lineHeight} paragraphs={paragraphs}");
    }
}
