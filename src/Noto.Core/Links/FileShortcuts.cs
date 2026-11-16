using System.Globalization;
using Noto.Core.Images;
using Noto.Core.Markdown;

namespace Noto.Core.Links;

/// <summary>How a file or folder link is drawn in the editor (J16, #196).</summary>
public enum FileLinkStyle
{
    /// <summary>The file's icon, its name, and a second line with its type and size, or "Folder".</summary>
    Standard,

    /// <summary>A small icon and the name, on one line.</summary>
    Compact,
}

/// <summary>What a picture dropped or pasted into a note becomes (J30, #196).</summary>
public enum PictureDrop
{
    /// <summary>A copy is kept beside the notes and drawn in the note (ADR-019 §4): Noto's behaviour before the setting.</summary>
    KeepCopy,

    /// <summary>A shortcut to the file where it is.</summary>
    Shortcut,
}

/// <summary>What a text file dropped or pasted into a note becomes (J30, #196).</summary>
public enum TextFileDrop
{
    /// <summary>A shortcut to the file where it is: the editor's behaviour before the setting.</summary>
    Shortcut,

    /// <summary>Its text, inserted where it was dropped.</summary>
    InsertText,
}

/// <summary>What the editor does with one file given to it (<see cref="FileShortcuts.Choose"/>).</summary>
public enum FileDrop
{
    /// <summary>A link to it, on a line of its own: a shortcut.</summary>
    Link,

    /// <summary>A copy kept beside the notes and drawn in the note.</summary>
    KeepPicture,

    /// <summary>Its text inserted.</summary>
    InsertText,
}

/// <summary>A line that is one link to a local file or folder, drawn as a shortcut (D34, I3).</summary>
/// <param name="Line">The line.</param>
/// <param name="Name">The link's text: the file's name as the note writes it.</param>
/// <param name="Destination">The link's destination as the note writes it, escapes undone: what opening it is given.</param>
/// <param name="Path">The full local path the link opens.</param>
public readonly record struct FileShortcut(MarkdownBlock Line, MarkdownSpan Name, string Destination, string Path);

/// <summary>
/// File and folder shortcuts (D34, I3, J16, J30; #196): which lines are shortcuts, what a dropped file becomes, and the
/// type and size line a shortcut shows. Pure: whether a file exists, its type name and its size are the caller's to
/// read; nothing here reads a file.
/// </summary>
public static class FileShortcuts
{
    /// <summary>The file types that are text: imported as notes (ADR-020), and inserted when the setting says so.</summary>
    public static IReadOnlyList<string> TextTypes { get; } = [".md", ".markdown", ".txt"];

    private static readonly string[] Units = ["KB", "MB", "GB", "TB"];

    /// <summary>
    /// The lines of <paramref name="document"/> that are exactly one link to a local file or folder. A file link inside
    /// a sentence stays a link: a shortcut is a line of its own, as every dropped or pasted one is.
    /// </summary>
    public static IEnumerable<FileShortcut> In(MarkdownDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        foreach (MarkdownBlock block in document.Blocks)
        {
            // [name](target): the name starts after the line's "[", the target after "](" and ends before its ")".
            foreach (MarkdownSpan name in block.Spans.Where(s => s.Kind == MarkdownSpanKind.Link && s.Start == block.Start + 1))
            {
                bool whole = block.Spans.Any(s => s.Kind == MarkdownSpanKind.LinkTarget && s.Start == name.End + 2 && s.End + 1 == block.End);
                if (whole && LinkTarget.At(document, name.Start) is { } destination && LinkTarget.Resolve(destination) is (LinkKind.Local, { } path))
                {
                    yield return new FileShortcut(block, name, destination, path);
                }
            }
        }
    }

    /// <summary>
    /// What a file given to the editor becomes. A picture is kept, as before, unless the setting asks for a shortcut;
    /// a text file is linked, as before, unless the setting asks for its text; anything else is linked.
    /// </summary>
    /// <param name="path">The file's path.</param>
    /// <param name="pictures">The picture setting.</param>
    /// <param name="texts">The text file setting.</param>
    /// <param name="keepsPictures">Noto has somewhere to keep pictures; without it a picture is linked.</param>
    /// <param name="hasOwnPath">
    /// The file is where the user keeps it. A picture dragged from a web page is a copy Noto removes once the drop is
    /// done, so a shortcut to it would lead nowhere: it is kept instead.
    /// </param>
    public static FileDrop Choose(string path, PictureDrop pictures, TextFileDrop texts, bool keepsPictures = true, bool hasOwnPath = true)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (keepsPictures && ImageReference.TypeOf(path) is not null)
        {
            return pictures == PictureDrop.Shortcut && hasOwnPath ? FileDrop.Link : FileDrop.KeepPicture;
        }

        return texts == TextFileDrop.InsertText && TextTypes.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
            ? FileDrop.InsertText
            : FileDrop.Link;
    }

    /// <summary>
    /// A Standard shortcut's second line: the type and size ("PDF Document · 2.4 MB"), "Folder", or, when the target is
    /// gone, "No longer there".
    /// </summary>
    /// <param name="path">The target's path: its extension names the type when the shell gives none.</param>
    /// <param name="exists">The target is there.</param>
    /// <param name="folder">The target is a folder.</param>
    /// <param name="typeName">The shell's name for the type, if it gave one.</param>
    /// <param name="bytes">The file's size, if it could be read.</param>
    public static string Details(string path, bool exists, bool folder, string? typeName, long? bytes)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!exists)
        {
            return "No longer there";
        }

        if (folder)
        {
            return "Folder";
        }

        string extension = Path.GetExtension(path).TrimStart('.');
        string type = !string.IsNullOrWhiteSpace(typeName) ? typeName.Trim()
            : extension.Length > 0 ? extension.ToUpperInvariant() + " file"
            : "File";

        return bytes is { } size ? $"{type} · {Size(size)}" : type;
    }

    /// <summary>A size as Explorer counts it, in steps of 1024: "1 byte", "900 bytes", "2.4 KB", "14 MB".</summary>
    public static string Size(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);

        if (bytes < 1024)
        {
            return bytes == 1 ? "1 byte" : string.Create(CultureInfo.InvariantCulture, $"{bytes} bytes");
        }

        double value = bytes;
        int unit = -1;
        do
        {
            value /= 1024;
            unit++;
        }
        while (value >= 1024 && unit < Units.Length - 1);

        // One decimal below 10, whole numbers above: "2.4 KB", "14 KB".
        double shown = value < 10 ? Math.Round(value, 1) : Math.Round(value);
        return string.Create(CultureInfo.InvariantCulture, $"{shown:0.#} {Units[unit]}");
    }
}
