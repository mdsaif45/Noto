using System.Globalization;
using System.Text;

namespace Noto.Core.Folders;

/// <summary>
/// The one normalisation every folder name goes through before it is stored (create-and-rename §2.1).
/// </summary>
/// <remarks>
/// Leading and trailing whitespace is removed, and each run of line breaks, tabs and other control or separator
/// characters inside becomes one space: a name is one line, however it was typed or pasted. Nothing else changes: no
/// length cap, no case folding, and duplicates are allowed (Case G). Names already stored are never rewritten; only a
/// create or a rename normalises.
/// </remarks>
public static class FolderName
{
    /// <summary>The name as stored, or <see langword="null"/> when nothing is left of it.</summary>
    public static string? Normalise(string? name)
    {
        if (name is null)
        {
            return null;
        }

        var result = new StringBuilder(name.Length);
        bool inBreak = false;

        foreach (char c in name)
        {
            if (IsBreak(c))
            {
                inBreak = true;
                continue;
            }

            if (inBreak)
            {
                result.Append(' ');
                inBreak = false;
            }

            result.Append(c);
        }

        string normalised = result.ToString().Trim();
        return normalised.Length == 0 ? null : normalised;
    }

    // Control characters (tab, CR, LF and the rest) and the Unicode line and paragraph separators.
    private static bool IsBreak(char c) =>
        char.IsControl(c)
        || CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;
}
