using System.Text;
using Noto.Core.Markdown;

namespace Noto.Core.Links;

/// <summary>What a link's destination is, by ADR-019 §1's one allow-list.</summary>
public enum LinkKind
{
    /// <summary>Never opened: any other scheme, a network path, a relative path, white space.</summary>
    Refused,

    /// <summary>An http or https address, or one written "www.…".</summary>
    Web,

    /// <summary>A mailto address.</summary>
    Mail,

    /// <summary>A file: URI of a local, drive-rooted path: a file or a folder.</summary>
    Local,
}

/// <summary>
/// The single answer to "what is this link, and may Noto open it?" (ADR-019 §1). Pure: whether a local target exists is
/// asked when it is opened, not here.
/// </summary>
public static class LinkTarget
{
    /// <summary>
    /// Types a click must not run without asking (ADR-019 §2): programs, scripts, shortcuts and installers. Anything not
    /// known to be a document asks too: see <see cref="NeedsConfirmation"/>.
    /// </summary>
    private static readonly HashSet<string> DocumentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".log", ".csv", ".json", ".xml", ".yaml", ".yml", ".rtf",
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".odt", ".ods", ".odp", ".one",
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".tif", ".tiff", ".svg", ".heic",
        ".mp3", ".wav", ".flac", ".m4a", ".ogg", ".mp4", ".mov", ".mkv", ".avi", ".webm",
        ".zip", ".7z", ".rar", ".tar", ".gz",
    };

    /// <summary>
    /// The destination of the link at <paramref name="position"/> in <paramref name="document"/>, its escapes undone;
    /// <see langword="null"/> when there is no link there.
    /// </summary>
    public static string? At(MarkdownDocument document, int position)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.LinkAt(position) is not { } span)
        {
            return null;
        }

        string written = document.Source.Substring(span.Start, span.Length);
        var plain = new StringBuilder(written.Length);
        for (int i = 0; i < written.Length; i++)
        {
            // CommonMark: a backslash before ASCII punctuation is an escape.
            if (written[i] == '\\' && i + 1 < written.Length && char.IsAscii(written[i + 1]) && char.IsPunctuation(written[i + 1]) | char.IsSymbol(written[i + 1]))
            {
                i++;
            }

            plain.Append(written[i]);
        }

        return plain.ToString();
    }

    /// <summary>The kind of <paramref name="destination"/>, as written in the note.</summary>
    public static LinkKind Classify(string? destination) => Resolve(destination).Kind;

    /// <summary>
    /// The address to open for <paramref name="destination"/>, or <see langword="null"/> when it is refused: a "www."
    /// address gains "https://"; a local one is its full path.
    /// </summary>
    public static (LinkKind Kind, string? Address) Resolve(string? destination)
    {
        if (string.IsNullOrEmpty(destination) || destination.Any(char.IsWhiteSpace) || destination.Any(char.IsControl))
        {
            return (LinkKind.Refused, null);
        }

        string written = destination.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "https://" + destination : destination;

        if (!Uri.TryCreate(written, UriKind.Absolute, out Uri? uri))
        {
            return (LinkKind.Refused, null);
        }

        if (uri.Scheme is "http" or "https")
        {
            return uri.Host.Length > 0 ? (LinkKind.Web, uri.AbsoluteUri) : (LinkKind.Refused, null);
        }

        if (uri.Scheme == "mailto")
        {
            return (LinkKind.Mail, uri.OriginalString);
        }

        // A file: URI only, of a path on a drive: never a server (\\host\share opens a connection silently).
        if (uri.IsFile && !uri.IsUnc && written.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
            && Path.IsPathFullyQualified(uri.LocalPath) && uri.LocalPath.Length >= 3 && char.IsAsciiLetter(uri.LocalPath[0]) && uri.LocalPath[1] == ':')
        {
            return (LinkKind.Local, uri.LocalPath);
        }

        return (LinkKind.Refused, null);
    }

    /// <summary>
    /// Whether opening a local file must ask first (ADR-019 §2): anything that is not a known document type — programs,
    /// scripts, shortcuts, installers and the unknown. A folder never asks; its caller knows it is one.
    /// </summary>
    public static bool NeedsConfirmation(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return !DocumentTypes.Contains(Path.GetExtension(path));
    }

    /// <summary>The destination a note writes for a full local path: a file: URI.</summary>
    public static string ForPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("A link to a local file or folder names its full path.", nameof(path));
        }

        // Parentheses escaped: an unbalanced one would end the markdown destination early.
        return new Uri(path).AbsoluteUri.Replace("(", "%28", StringComparison.Ordinal).Replace(")", "%29", StringComparison.Ordinal);
    }
}
