namespace Noto.Core.Markdown;

/// <summary>What a span of the source is (markdown-model.md §2).</summary>
public enum MarkdownSpanKind
{
    /// <summary>Syntax characters: hidden by the editor, or dimmed when markup is shown.</summary>
    Markup,

    /// <summary>A heading's text; <see cref="MarkdownSpan.Level"/> is 1–6.</summary>
    Heading,

    /// <summary>A quote's text.</summary>
    Quote,

    /// <summary>A list item's marker (<c>- </c>, <c>1. </c>) and the blanks after it. Shown.</summary>
    ListMarker,

    /// <summary>A task's box: <c>[ ]</c>, <c>[x]</c> or <c>[X]</c>. Shown.</summary>
    TaskBox,

    /// <summary>A checked task's text.</summary>
    TaskDone,

    /// <summary>A line inside a fenced code block. Nothing inside it is formatting.</summary>
    CodeBlock,

    /// <summary>A separator's marks (<c>---</c>).</summary>
    Separator,

    /// <summary>Text between <c>**</c> or <c>__</c>.</summary>
    Strong,

    /// <summary>Text between <c>*</c> or <c>_</c>.</summary>
    Emphasis,

    /// <summary>Text between <c>~~</c>.</summary>
    Strikethrough,

    /// <summary>Text between <c>==</c> (Noto's and SideNotes' underline).</summary>
    Underline,

    /// <summary>A code span's text.</summary>
    Code,

    /// <summary>A link's text.</summary>
    Link,

    /// <summary>A link's destination: hidden like markup, kept apart so it can be opened.</summary>
    LinkTarget,
}

/// <summary>
/// A range of the source, <c>[Start, Start + Length)</c> in UTF-16 code units of the exact string parsed, and what
/// it is.
/// </summary>
/// <param name="Kind">What the range is.</param>
/// <param name="Start">The first code unit.</param>
/// <param name="Length">The number of code units.</param>
/// <param name="Level">A heading's level; 0 for every other kind.</param>
public readonly record struct MarkdownSpan(MarkdownSpanKind Kind, int Start, int Length, int Level = 0)
{
    /// <summary>The code unit after the span.</summary>
    public int End => Start + Length;
}
