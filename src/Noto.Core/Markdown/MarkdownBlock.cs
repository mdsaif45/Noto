namespace Noto.Core.Markdown;

/// <summary>What a block of the source is (markdown-model.md §2).</summary>
public enum MarkdownBlockKind
{
    /// <summary>A line of text.</summary>
    Paragraph,

    /// <summary>A line of nothing but spaces and tabs, or nothing.</summary>
    Blank,

    /// <summary>An ATX heading, levels 1–6.</summary>
    Heading,

    /// <summary>A line starting with <c>&gt;</c>.</summary>
    Quote,

    /// <summary>A <c>-</c>, <c>*</c> or <c>+</c> list item.</summary>
    BulletItem,

    /// <summary>A <c>1.</c> or <c>1)</c> list item.</summary>
    OrderedItem,

    /// <summary>A list item with a <c>[ ]</c> or <c>[x]</c> box.</summary>
    TaskItem,

    /// <summary>A fenced code block: its fence lines and the lines between, or to the end of the note when unclosed.</summary>
    CodeFence,

    /// <summary>A line of three or more <c>-</c>, <c>*</c> or <c>_</c>.</summary>
    Separator,
}

/// <summary>
/// One block: a line, or a fenced code block's lines. <see cref="Start"/> and <see cref="Length"/> cover its text
/// without its final line break.
/// </summary>
/// <param name="Kind">What the block is.</param>
/// <param name="Start">The block's first code unit in the source.</param>
/// <param name="Length">Its length, without its final line break.</param>
/// <param name="Level">A heading's level (1–6); 0 otherwise.</param>
/// <param name="Indent">A list item's leading whitespace in columns (a tab reaches the next multiple of four); 0 otherwise.</param>
/// <param name="IsChecked">Whether a task item's box is checked.</param>
/// <param name="Spans">The block's spans, ordered as <see cref="MarkdownDocument.Spans"/>.</param>
public sealed record MarkdownBlock(
    MarkdownBlockKind Kind,
    int Start,
    int Length,
    int Level,
    int Indent,
    bool IsChecked,
    IReadOnlyList<MarkdownSpan> Spans)
{
    /// <summary>The code unit after the block's text.</summary>
    public int End => Start + Length;
}
