using System.Globalization;
using Noto.Core.Markdown;

namespace Noto.Core.Images;

/// <summary>A picture written in a note: its source as written, and its caption as shown.</summary>
public readonly record struct NotePicture(string Source, string Caption)
{
    /// <summary>What the preview calls it: its caption, or "Picture" when it has none.</summary>
    public string Name => Caption.Trim() is { Length: > 0 } caption ? caption : "Picture";
}

/// <summary>
/// A note's pictures in the order they are written, and the one a preview shows (D35, G44, #268). Space on a card
/// previews its first, Space on a picture in the editor that one; Left and Right step through the rest and stop at
/// either end, never wrapping round. Every picture is listed, one written as a web address too: the preview finds each
/// file through the image store, which knows only the files Noto keeps, so a web picture is never fetched (ADR-019,
/// principle 4) and is shown as one that can't be.
/// </summary>
public sealed class NotePictures
{
    private NotePictures(IReadOnlyList<NotePicture> all, int index)
    {
        All = all;
        Index = index;
    }

    /// <summary>Every picture of the note, in the order written; never empty.</summary>
    public IReadOnlyList<NotePicture> All { get; }

    /// <summary>The one shown, from 0.</summary>
    public int Index { get; }

    /// <summary>The picture shown.</summary>
    public NotePicture Current => All[Index];

    /// <summary>Where the one shown is, as the preview says it: "Picture 1 of 3".</summary>
    public string Position => string.Create(CultureInfo.InvariantCulture, $"Picture {Index + 1} of {All.Count}");

    /// <summary>The note's pictures, the first shown; <see langword="null"/> when it has none.</summary>
    public static NotePictures? Of(MarkdownDocument document)
    {
        var all = Read(document);
        return all.Count == 0 ? null : new NotePictures([.. all.Select(p => p.Picture)], 0);
    }

    /// <summary>
    /// The note's pictures with the one the editor's selection <c>[start, end)</c> is on shown; <see langword="null"/>
    /// when it is on none, and Space types a space. On one means: the picture selected alone, as Ctrl+C copies it
    /// (<see cref="ImageReference.Selected"/>); or the caret inside its hidden markup, where a typed space would only
    /// break the reference. A caret in, or at either end of, its caption is text being typed, and stays so.
    /// </summary>
    public static NotePictures? At(MarkdownDocument document, int start, int end)
    {
        var all = Read(document);
        int index = start == end
            ? all.FindIndex(p => (p.Image.Start < start && start < p.Image.Start + 2) || (p.Source.Start - 2 < start && start < p.Image.End))
            : ImageReference.SelectedPicture(document, start, end) is { } selected ? all.FindIndex(p => p.Image == selected.Image) : -1;

        return index < 0 ? null : new NotePictures([.. all.Select(p => p.Picture)], index);
    }

    /// <summary>The same pictures, <paramref name="by"/> places on: Left is -1, Right +1. Stops at the first and the last.</summary>
    public NotePictures Step(int by) => new(All, Math.Clamp(Index + by, 0, All.Count - 1));

    private static List<(MarkdownSpan Image, MarkdownSpan Source, NotePicture Picture)> Read(MarkdownDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        string text = document.Source;
        var all = new List<(MarkdownSpan, MarkdownSpan, NotePicture)>();

        foreach (MarkdownBlock block in document.Blocks)
        {
            foreach (MarkdownSpan image in block.Spans.Where(s => s.Kind == MarkdownSpanKind.Image))
            {
                if (ImageReference.SourceOf(block, image) is { } source)
                {
                    all.Add((image, source, new NotePicture(text.Substring(source.Start, source.Length), ImageReference.CaptionOf(text, image, source))));
                }
            }
        }

        return all;
    }
}
