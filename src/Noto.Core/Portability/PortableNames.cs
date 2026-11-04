using System.Text;

namespace Noto.Core.Portability;

/// <summary>
/// File and folder names for an export (ADR-020 §4): a note's title or a folder's name made safe for Windows, and made
/// unique within its directory in a fixed order, so the same workspace always exports to the same names.
/// </summary>
public static class PortableNames
{
    /// <summary>The longest name written, in characters: long enough to read, short enough for deep paths.</summary>
    public const int MaxLength = 80;

    /// <summary>The name of something that has none.</summary>
    public const string Untitled = "Untitled";

    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// <paramref name="name"/> as a Windows file or folder name: characters Windows forbids and control characters
    /// become <c>_</c>, leading and trailing spaces and trailing dots go, a reserved device name gains a <c>_</c>, it is
    /// cut at <see cref="MaxLength"/> characters (never inside a surrogate pair), and nothing left is
    /// <see cref="Untitled"/>.
    /// </summary>
    public static string Safe(string? name)
    {
        var safe = new StringBuilder(name?.Length ?? 0);
        foreach (char c in name ?? string.Empty)
        {
            safe.Append(char.IsControl(c) || c is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*' ? '_' : c);
        }

        string text = safe.ToString().Trim();
        if (text.Length > MaxLength)
        {
            int cut = char.IsLowSurrogate(text[MaxLength]) ? MaxLength - 1 : MaxLength;
            text = text[..cut].TrimEnd();
        }

        text = text.TrimEnd('.', ' ');
        if (text.Length == 0)
        {
            return Untitled;
        }

        string stem = text.Split('.')[0];
        return Reserved.Contains(stem) ? text + "_" : text;
    }
}

/// <summary>
/// Hands out names unique within one directory, ignoring case as Windows does: the first <c>Notes</c> is
/// <c>Notes</c>, the next <c>Notes (2)</c>, then <c>Notes (3)</c>. Asked in the same order, it answers the same.
/// </summary>
public sealed class NameAllocator
{
    private readonly HashSet<string> _taken;

    /// <summary>A directory that already holds <paramref name="taken"/>.</summary>
    public NameAllocator(IEnumerable<string>? taken = null) => _taken = new(taken ?? [], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// <paramref name="name"/>, or the first of <c>name (2)</c>, <c>name (3)</c>… not yet taken, with
    /// <paramref name="extension"/> (such as <c>.md</c>) after it; the result is taken.
    /// </summary>
    public string Take(string name, string extension = "")
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(extension);

        string candidate = name + extension;
        for (int n = 2; !_taken.Add(candidate); n++)
        {
            candidate = $"{name} ({n}){extension}";
        }

        return candidate;
    }
}
