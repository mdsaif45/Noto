using System.Text.RegularExpressions;
using Noto.Core.Identifiers;
using Noto.Core.Markdown;

namespace Noto.Core.Images;

/// <summary>
/// How a note refers to an image Noto keeps (ADR-019 §4): <c>attachments/&lt;ULID&gt;.&lt;type&gt;</c>, a name Noto made,
/// never one a file or the clipboard brought. Anything else written as an image's source is not shown as an image.
/// </summary>
public static partial class ImageReference
{
    /// <summary>The largest image Noto keeps, in bytes.</summary>
    public const long MaxBytes = 25L * 1024 * 1024;

    /// <summary>The folder, inside the data root, every reference points into.</summary>
    public const string Folder = "attachments";

    /// <summary>The types an image may be: what the system's decoder reads and a note's reader expects.</summary>
    public static IReadOnlyList<string> Types { get; } = ["png", "jpg", "jpeg", "gif", "webp", "bmp"];

    /// <summary>A new reference for an image of <paramref name="type"/> (one of <see cref="Types"/>, any case).</summary>
    public static string New(string type)
    {
        ArgumentNullException.ThrowIfNull(type);
        string normal = type.TrimStart('.').ToLowerInvariant();
        if (!Types.Contains(normal))
        {
            throw new ArgumentException($"Not an image type Noto keeps: '{type}'.", nameof(type));
        }

        return $"{Folder}/{Ulid.NewId()}.{normal}";
    }

    /// <summary>Whether <paramref name="reference"/> is one Noto made; if so, its file name within <see cref="Folder"/>.</summary>
    public static bool TryFileName(string? reference, out string fileName)
    {
        Match match = reference is null ? Match.Empty : Pattern().Match(reference);
        fileName = match.Success ? match.Groups["name"].Value : string.Empty;
        return match.Success;
    }

    /// <summary>The type of a file by its extension, if it is one Noto keeps; otherwise <see langword="null"/>.</summary>
    public static string? TypeOf(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        return Types.Contains(extension) ? extension : null;
    }

    /// <summary>Every reference Noto made in <paramref name="content"/>: what keeps an image's file from the sweep.</summary>
    public static IEnumerable<string> In(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        MarkdownDocument document = MarkdownDocument.Parse(content);
        foreach (MarkdownBlock block in document.Blocks)
        {
            foreach (MarkdownSpan image in block.Spans.Where(s => s.Kind == MarkdownSpanKind.Image))
            {
                if (SourceOf(block, image) is { } s && TryFileName(content.Substring(s.Start, s.Length), out string name))
                {
                    yield return name;
                }
            }
        }
    }

    /// <summary>
    /// The source, as written, of the picture on the line holding <paramref name="position"/>: the one there, else the
    /// line's last, which is the one drawn on top. A picture is drawn below its line, so a right-click on it lands on
    /// that line (D32, #187). <see langword="null"/> when the line has none, even when the paragraph it is part of has
    /// one on another line (a picture line and the text under it are one paragraph).
    /// </summary>
    public static string? At(MarkdownDocument document, int position) =>
        PictureAt(document, position) is { } found ? document.Source.Substring(found.Source.Start, found.Source.Length) : null;

    /// <summary>
    /// The name Save picture as suggests for the picture <see cref="At"/> finds (D33, #235): its caption, as shown, made a
    /// file name (a picture's own extension dropped, since the type is the file's); with no caption left, the kept
    /// file's name. <see langword="null"/> when the line has no picture.
    /// </summary>
    public static string? SaveNameAt(MarkdownDocument document, int position)
    {
        if (PictureAt(document, position) is not { } found)
        {
            return null;
        }

        string text = document.Source;
        string caption = CaptionOf(text, found.Image, found.Source);
        if (TypeOf(caption) is not null)
        {
            caption = Path.GetFileNameWithoutExtension(caption);
        }

        // What Windows refuses in a file name, and the dots and blanks it drops from the end.
        string name = string.Concat(caption.Where(c => c >= ' ' && !"\\/:*?\"<>|".Contains(c, StringComparison.Ordinal))).Trim().TrimEnd('.', ' ');
        return name.Length > 0 ? name : Path.GetFileNameWithoutExtension(text.Substring(found.Source.Start, found.Source.Length));
    }

    /// <summary>The picture on the line holding <paramref name="position"/>, as <see cref="At"/> chooses it, and its source.</summary>
    private static (MarkdownSpan Image, MarkdownSpan Source)? PictureAt(MarkdownDocument document, int position)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Blocks.FirstOrDefault(b => b.Start <= position && position <= b.End) is not { } block)
        {
            return null;
        }

        string text = document.Source;
        int at = Math.Clamp(position, 0, text.Length);
        int lineStart = at == 0 ? 0 : text.LastIndexOf('\n', at - 1) + 1;
        int lineEnd = text.IndexOf('\n', at) is var end and >= 0 ? end : text.Length;
        List<MarkdownSpan> images = [.. block.Spans.Where(s => s.Kind == MarkdownSpanKind.Image && s.Start >= lineStart && s.Start < lineEnd)];
        MarkdownSpan? image = images.Where(s => s.Start <= position && position <= s.End).Cast<MarkdownSpan?>().FirstOrDefault()
            ?? images.Cast<MarkdownSpan?>().LastOrDefault();

        return image is { } i && SourceOf(block, i) is { } s ? (i, s) : null;
    }

    /// <summary>
    /// The source, as written, of the picture <c>[start, end)</c> selects and nothing visible besides: blanks aside, the
    /// selection lies inside one <c>![caption](source)</c> and holds its whole caption, the only part of it shown (D32,
    /// #187: Ctrl+C on a selected picture copies the picture). <see langword="null"/> otherwise.
    /// </summary>
    public static string? Selected(MarkdownDocument document, int start, int end) =>
        SelectedPicture(document, start, end) is { } found ? document.Source.Substring(found.Source.Start, found.Source.Length) : null;

    /// <summary>The picture <see cref="Selected"/> finds, and its source: the one Space previews from a selection (#268).</summary>
    internal static (MarkdownSpan Image, MarkdownSpan Source)? SelectedPicture(MarkdownDocument document, int start, int end)
    {
        ArgumentNullException.ThrowIfNull(document);
        string text = document.Source;
        start = Math.Clamp(start, 0, text.Length);
        end = Math.Clamp(end, start, text.Length);

        while (start < end && char.IsWhiteSpace(text[start]))
        {
            start++;
        }

        while (end > start && char.IsWhiteSpace(text[end - 1]))
        {
            end--;
        }

        if (start == end || document.Blocks.FirstOrDefault(b => b.Start <= start && end <= b.End) is not { } block)
        {
            return null;
        }

        foreach (MarkdownSpan image in block.Spans.Where(s => s.Kind == MarkdownSpanKind.Image && s.Start <= start && end <= s.End))
        {
            // The caption is between "![" and "](".
            if (SourceOf(block, image) is { } s && start <= image.Start + 2 && end >= s.Start - 2)
            {
                return (image, s);
            }
        }

        return null;
    }

    /// <summary>A picture's caption as shown: what is between <c>![</c> and <c>](</c>, its escaped brackets unescaped.</summary>
    internal static string CaptionOf(string text, MarkdownSpan image, MarkdownSpan source) =>
        text[(image.Start + 2)..(source.Start - 2)].Replace("\\[", "[", StringComparison.Ordinal).Replace("\\]", "]", StringComparison.Ordinal);

    /// <summary>The span of an image's source, inside its <c>(…)</c>.</summary>
    internal static MarkdownSpan? SourceOf(MarkdownBlock block, MarkdownSpan image) =>
        block.Spans.Where(s => s.Kind == MarkdownSpanKind.LinkTarget && s.Start > image.Start && s.End < image.End).Cast<MarkdownSpan?>().LastOrDefault();

    [GeneratedRegex(@"^attachments/(?<name>[0-9A-HJKMNP-TV-Z]{26}\.(?:png|jpg|jpeg|gif|webp|bmp))$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
