using System.Text.RegularExpressions;
using Noto.Core.Markdown;

namespace Noto.Core.Editing;

/// <summary>
/// Code detection on paste (D39, #187): plain text that reads as code goes into a fenced code block, when Settings
/// asks for it. Conservative on purpose: a paste that is wrongly reformatted is worse than code left as text, and
/// Ctrl+Z gives the user back nothing they can see went wrong.
/// </summary>
public static partial class PastedCode
{
    /// <summary>The fewest non-blank lines that can be code: a single line is never wrapped.</summary>
    public const int MinimumLines = 2;

    // Lower-case words a line of code starts with, across C#, JavaScript, Python and the shell. Prose capitalises its
    // first word, so these rarely start a sentence.
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "using", "namespace", "public", "private", "protected", "internal", "static", "class", "struct", "interface",
        "enum", "record", "return", "var", "let", "const", "function", "import", "export", "def", "elif", "async",
        "await", "if", "else", "for", "foreach", "while", "switch", "case", "try", "catch", "except", "finally", "throw",
        "raise", "with", "yield", "lambda", "fn", "func", "package", "print", "console", "cd", "ls", "mkdir", "rm", "cp",
        "mv", "echo", "sudo", "apt", "brew", "npm", "npx", "yarn", "pnpm", "pip", "python", "node", "dotnet", "git",
        "docker", "kubectl", "curl", "wget", "chmod", "fi", "done", "esac",
    };

    /// <summary>
    /// Whether <paramref name="pasted"/> reads as code: at least <see cref="MinimumLines"/> non-blank lines, and at
    /// least three in five of them look like code (see <see cref="IsCodeLine"/>). Text that already holds a fence is
    /// Markdown, not code.
    /// </summary>
    public static bool IsCode(string pasted)
    {
        ArgumentNullException.ThrowIfNull(pasted);
        string[] lines = [.. pasted.ReplaceLineEndings("\n").Split('\n').Where(l => !string.IsNullOrWhiteSpace(l))];

        // ponytail: line signals only; a real classifier (token statistics) if users report misses or false wraps.
        return lines.Length >= MinimumLines
            && !lines.Any(l => l.TrimStart().StartsWith("```", StringComparison.Ordinal) || l.TrimStart().StartsWith("~~~", StringComparison.Ordinal))
            && lines.Count(IsCodeLine) * 5 >= lines.Length * 3;
    }

    /// <summary>
    /// The paste in a fenced code block on lines of its own, replacing the selection, the caret on the line after it.
    /// <see langword="null"/> when the paste is not code (<see cref="IsCode"/>), when the selection starts or ends
    /// inside a code block (its text is code already), or when the page's HTML brought a code block of its own
    /// (<paramref name="html"/>, as HtmlToMarkdown made it): that paste is an ordinary one.
    /// </summary>
    public static TextEdit? Fence(string text, TextSelection selection, string pasted, string? html = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(pasted);

        if ((html?.Contains("```", StringComparison.Ordinal) ?? false) || !IsCode(pasted)
            || MarkdownDocument.Parse(text).Blocks.Any(b => b.Kind == MarkdownBlockKind.CodeFence && (Inside(b, selection.Start) || Inside(b, selection.End))))
        {
            return null;
        }

        // Leading blank lines and trailing whitespace go (an editor's copy often ends part way into the next line);
        // the first line's indent stays.
        string code = pasted.ReplaceLineEndings("\n").TrimEnd();
        code = code[(code.LastIndexOf('\n', code.Length - code.TrimStart().Length) + 1)..];

        // IsCode refuses text with a fence line of its own, so nothing in the code can close this one.
        const string fence = "```";
        string before = selection.Start > 0 && text[selection.Start - 1] != '\n' ? "\n" : string.Empty;
        bool lineFollows = selection.End < text.Length && text[selection.End] == '\n';
        string after = lineFollows ? string.Empty : "\n";
        string block = before + fence + "\n" + code + "\n" + fence + after;
        return new TextEdit(selection.Start, selection.Length, block, TextSelection.Caret(selection.Start + block.Length + (lineFollows ? 1 : 0)));
    }

    /// <summary>
    /// A line that looks like code: it ends with <c>; { } [ (</c>, starts with a closing bracket, a comment, a
    /// shebang or a lower-case keyword, is a JSON member or an assignment, or holds an operator prose does not use.
    /// </summary>
    public static bool IsCodeLine(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        string trimmed = line.Trim();

        if (trimmed.Length == 0)
        {
            return false;
        }

        if (trimmed[^1] is ';' or '{' or '}' or '[' or '(' || trimmed[0] is '}' or ']' or ')')
        {
            return true;
        }

        if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith("/*", StringComparison.Ordinal)
            || trimmed.StartsWith("#!", StringComparison.Ordinal) || trimmed.StartsWith("#include", StringComparison.Ordinal)
            || trimmed.StartsWith("$ ", StringComparison.Ordinal))
        {
            return true;
        }

        Match word = LeadingWord().Match(trimmed);
        if (word.Success && Keywords.Contains(word.Groups[1].Value))
        {
            return true;
        }

        return JsonMember().IsMatch(trimmed) || Assignment().IsMatch(trimmed) || Operator().IsMatch(trimmed);
    }

    // The fence block is its fence lines and the lines between: an offset after its first character and up to its end.
    private static bool Inside(MarkdownBlock block, int at) => at > block.Start && at <= block.End;

    // A lower-case word followed by a space, an opening bracket, a colon, a dot, a semicolon or the end of the line.
    [GeneratedRegex(@"^([a-z]+)(?:[\s(:.;]|$)")]
    private static partial Regex LeadingWord();

    [GeneratedRegex("^\"[^\"]*\"\\s*:")]
    private static partial Regex JsonMember();

    // name = value, name.field = value, name[i] = value; not == or =>.
    [GeneratedRegex(@"^[A-Za-z_$][\w.$\[\]]*\s*(?:[+\-*/]?=)(?![=>])\s*\S")]
    private static partial Regex Assignment();

    [GeneratedRegex(@"=>|!=|&&|\+\+|\+=|-=|</|/>|\(\);|\s--?[a-z][\w-]*")]
    private static partial Regex Operator();
}
