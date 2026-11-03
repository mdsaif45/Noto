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
                MarkdownSpan? source = block.Spans.Where(s => s.Kind == MarkdownSpanKind.LinkTarget && s.Start > image.Start && s.End < image.End).Cast<MarkdownSpan?>().LastOrDefault();
                if (source is { } s && TryFileName(content.Substring(s.Start, s.Length), out string name))
                {
                    yield return name;
                }
            }
        }
    }

    [GeneratedRegex(@"^attachments/(?<name>[0-9A-HJKMNP-TV-Z]{26}\.(?:png|jpg|jpeg|gif|webp|bmp))$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
