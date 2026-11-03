using System.Globalization;
using System.Net;
using System.Text;

namespace Noto.Core.Editing;

/// <summary>
/// Formatted paste (P2.7): HTML from the clipboard, a browser's or an office app's, as the Markdown subset Noto
/// speaks (markdown-model.md §2). Text, never code: no script runs and no resource is fetched; script and style
/// content is dropped, and a link whose address is not http, https, mailto or file keeps only its text.
/// </summary>
/// <remarks>
/// Tolerant by design: clipboard HTML is often a fragment with unclosed or stray tags. Unknown tags are ignored and
/// their text kept. The result is plain Markdown source; nothing that is not Markdown reaches the note.
/// </remarks>
public static class HtmlToMarkdown
{
    private static readonly string[] SafeSchemes = ["http:", "https:", "mailto:", "file:"];

    /// <summary>The fragment as Markdown: one line per block, list nesting by indent, trailing space trimmed.</summary>
    public static string Convert(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        var writer = new Writer();
        int i = 0;

        while (i < html.Length)
        {
            int open = html.IndexOf('<', i);
            if (open < 0)
            {
                writer.Text(html[i..]);
                break;
            }

            writer.Text(html[i..open]);

            if (html.AsSpan(open).StartsWith("<!--", StringComparison.Ordinal))
            {
                int endComment = html.IndexOf("-->", open + 4, StringComparison.Ordinal);
                i = endComment < 0 ? html.Length : endComment + 3;
                continue;
            }

            int close = html.IndexOf('>', open + 1);
            if (close < 0)
            {
                writer.Text(html[open..]);
                break;
            }

            Tag tag = Tag.Parse(html.AsSpan(open + 1, close - open - 1));
            i = close + 1;

            if (!tag.Closing && tag.Name is "script" or "style")
            {
                int end = html.IndexOf("</" + tag.Name, i, StringComparison.OrdinalIgnoreCase);
                i = end < 0 ? html.Length : end;
                continue;
            }

            writer.Tag(tag);
        }

        return writer.Result();
    }

    private readonly record struct Tag(string Name, bool Closing, string? Href, bool Checked, bool Checkbox)
    {
        public static Tag Parse(ReadOnlySpan<char> inside)
        {
            bool closing = inside.StartsWith("/");
            ReadOnlySpan<char> body = closing ? inside[1..] : inside;
            int nameEnd = 0;
            while (nameEnd < body.Length && (char.IsAsciiLetterOrDigit(body[nameEnd]) || body[nameEnd] == ':'))
            {
                nameEnd++;
            }

            string name = body[..nameEnd].ToString().ToLowerInvariant();
            string attributes = body[nameEnd..].ToString();
            return new Tag(
                name,
                closing,
                Attribute(attributes, "href"),
                attributes.Contains("checked", StringComparison.OrdinalIgnoreCase),
                name == "input" && string.Equals(Attribute(attributes, "type"), "checkbox", StringComparison.OrdinalIgnoreCase));
        }

        private static string? Attribute(string attributes, string name)
        {
            int at = attributes.IndexOf(name + "=", StringComparison.OrdinalIgnoreCase);
            if (at < 0)
            {
                return null;
            }

            int start = at + name.Length + 1;
            if (start >= attributes.Length)
            {
                return null;
            }

            char quote = attributes[start];
            if (quote is '"' or '\'')
            {
                int end = attributes.IndexOf(quote, start + 1);
                return end < 0 ? attributes[(start + 1)..] : attributes[(start + 1)..end];
            }

            int stop = attributes.IndexOfAny([' ', '\t', '/'], start);
            return stop < 0 ? attributes[start..] : attributes[start..stop];
        }
    }

    /// <summary>Builds the Markdown line by line: inline markers into the current line, blocks as line prefixes.</summary>
    private sealed class Writer
    {
        private readonly List<string> _lines = [];
        private readonly StringBuilder _line = new();
        private readonly Stack<(bool Ordered, int Next)> _lists = new();
        private readonly Stack<string?> _links = new();
        private readonly Dictionary<string, int> _open = [];
        private int _pre;
        private int _quote;
        private int _linkStart = -1;

        public void Text(string raw)
        {
            if (raw.Length == 0)
            {
                return;
            }

            string text = WebUtility.HtmlDecode(raw);

            if (_pre > 0)
            {
                // Code keeps its line breaks and its spaces.
                string[] pieces = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
                for (int k = 0; k < pieces.Length; k++)
                {
                    if (k > 0)
                    {
                        Break();
                    }

                    _line.Append(pieces[k]);
                }

                return;
            }

            // Ordinary text: any run of whitespace is one space, none at the start of a line.
            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c))
                {
                    if (_line.Length > 0 && _line[^1] != ' ' && !EndsWithPrefix())
                    {
                        _line.Append(' ');
                    }
                }
                else
                {
                    _line.Append(c);
                }
            }
        }

        public void Tag(Tag tag)
        {
            switch (tag.Name)
            {
                case "b" or "strong":
                    Mark("**", tag.Closing);
                    break;
                case "i" or "em":
                    Mark("*", tag.Closing);
                    break;
                case "s" or "strike" or "del":
                    Mark("~~", tag.Closing);
                    break;
                case "u" or "ins":
                    Mark("==", tag.Closing);
                    break;
                case "code" when _pre == 0:
                    Mark("`", tag.Closing);
                    break;
                case "a":
                    Link(tag);
                    break;
                case "br":
                    Break();
                    break;
                case "hr" when !tag.Closing:
                    Block();
                    _lines.Add("---");
                    break;
                case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                    Block();
                    if (!tag.Closing)
                    {
                        _line.Append('#', tag.Name[1] - '0').Append(' ');
                    }

                    break;
                case "p" or "div" or "section" or "article" or "header" or "footer" or "tr" or "table":
                    Block();
                    break;
                case "blockquote":
                    Block();
                    _quote = Math.Max(0, _quote + (tag.Closing ? -1 : 1));
                    break;
                case "pre":
                    // A fence on each side; the code between is copied as it is.
                    Block();
                    _lines.Add("```");
                    _pre = tag.Closing ? Math.Max(0, _pre - 1) : _pre + 1;
                    break;
                case "ul" or "ol":
                    Block();
                    if (tag.Closing)
                    {
                        _ = _lists.TryPop(out _);
                    }
                    else
                    {
                        _lists.Push((tag.Name == "ol", 1));
                    }

                    break;
                case "li" when !tag.Closing:
                    Block();
                    Item();
                    break;
                case "li":
                    Block();
                    break;
                case "input" when tag.Checkbox:
                    if (_line.ToString().TrimEnd().EndsWith('-'))
                    {
                        _line.Append(tag.Checked ? "[x] " : "[ ] ");
                    }

                    break;
            }
        }

        public string Result()
        {
            Block();
            if (_pre > 0)
            {
                _lines.Add("```");
            }

            while (_lines.Count > 0 && _lines[^1].Length == 0)
            {
                _lines.RemoveAt(_lines.Count - 1);
            }

            return string.Join("\n", _lines);
        }

        private void Mark(string marker, bool closing)
        {
            // A closing tag with no opening one marks nothing.
            _open.TryGetValue(marker, out int depth);
            if (closing && depth == 0)
            {
                return;
            }

            _open[marker] = depth + (closing ? -1 : 1);

            // Markers hug the text: a space before a closing marker moves outside it, or Markdown would not close it.
            if (closing && _line.Length > 0 && _line[^1] == ' ')
            {
                _line.Length--;
                _line.Append(marker).Append(' ');
            }
            else
            {
                _line.Append(marker);
            }
        }

        private void Link(Tag tag)
        {
            if (!tag.Closing)
            {
                string? href = tag.Href is { } h && SafeSchemes.Any(s => h.TrimStart().StartsWith(s, StringComparison.OrdinalIgnoreCase)) && !h.Any(char.IsWhiteSpace) ? h.Trim() : null;
                _links.Push(href);
                _linkStart = href is null ? -1 : _line.Length;
                if (href is not null)
                {
                    _line.Append('[');
                }

                return;
            }

            if (_links.TryPop(out string? target) && target is not null)
            {
                if (_line.Length > _linkStart + 1)
                {
                    _line.Append("](").Append(target).Append(')');
                }
                else
                {
                    _line.Length = _linkStart;
                }
            }
        }

        private void Item()
        {
            int depth = Math.Max(0, _lists.Count - 1);
            string indent = new(' ', depth * 2);

            if (_lists.TryPop(out var list))
            {
                string marker = list.Ordered ? list.Next.ToString(CultureInfo.InvariantCulture) + ". " : "- ";
                _lists.Push((list.Ordered, list.Next + 1));
                _line.Append(indent).Append(marker);
            }
            else
            {
                _line.Append("- ");
            }
        }

        private bool EndsWithPrefix()
        {
            string s = _line.ToString();
            return s.EndsWith("- ", StringComparison.Ordinal) || s.EndsWith("# ", StringComparison.Ordinal) || s.EndsWith(". ", StringComparison.Ordinal) && s.TrimStart().TrimEnd('.', ' ').All(char.IsAsciiDigit);
        }

        /// <summary>Ends the current line if it holds anything; a block boundary never makes an empty line.</summary>
        private void Block()
        {
            if (_line.ToString().Trim().Length > 0)
            {
                Break();
            }
            else
            {
                _line.Clear();
            }
        }

        private void Break()
        {
            string line = _line.ToString().TrimEnd();
            _line.Clear();

            if (_pre == 0 && _quote > 0 && line.Length > 0)
            {
                line = string.Concat(Enumerable.Repeat("> ", _quote)) + line;
            }

            _lines.Add(line);
        }
    }
}
