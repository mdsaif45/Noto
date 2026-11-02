using System.Text;

namespace Noto.Spike.HiddenMarkdown;

internal static class Corpus
{
    /// <summary>Round-trip samples: every construct, awkward characters, every line ending, with and without a final break.</summary>
    public static readonly (string Name, string Text)[] Fidelity =
    [
        ("inline", "a **bold** b *em* c ~~strike~~ d ==under== e ::mark:: f `code` g [link](https://example.com/x)"),
        ("blocks", "# H1\n## H2\n##### H5\n> quote **q**\n- item\n1. one\n- [ ] open\n- [x] done\n---\n```\nvar x = 1;\n```\nend"),
        ("lf-final", "line one\nline two\n"),
        ("crlf", "line one\r\nline **two**\r\nthree"),
        ("cr-only", "line one\rline *two*\rthree"),
        ("blank-lines", "\n\na\n\n\nb\n\n"),
        ("unicode", "emoji 👍🏽 and 👨‍👩‍👧 **bold 漢字** é tab\tend  "),
        ("unclosed", "**not closed and *also not\n`tick\n[label](no close"),
        ("empty", ""),
    ];

    public const string Caret = "a **bold** b";

    public static string Small() =>
        "# Groceries\n- [ ] **milk** and *eggs*\n- [x] bread\n\nCall `555-0100` about the [order](https://example.com/o).\n" +
        "> Remember ::Tuesday::\n---\n1. first\n2. second with ~~old~~ new\n";

    /// <summary>A deterministic note of at least <paramref name="chars"/> characters, the mixture a long note holds.</summary>
    public static string Large(int chars)
    {
        string[] templates =
        [
            "## Section {0}",
            "Some **bold text** and *italic words* with `inline code` and a [link](https://example.com/{0}) here.",
            "- [ ] Task item number {0} with ~~struck~~ text",
            "- [x] Done item {0}",
            "1. Ordered item with ==underlined== and ::marked:: words",
            "> A quote line {0} with **strong** emphasis",
            "```",
            "var x = {0}; // code inside a fence",
            "```",
            "---",
            "Plain paragraph text that goes on for a while to simulate a real note, number {0}, with no markup at all.",
            "",
        ];

        var sb = new StringBuilder();
        for (int i = 0; sb.Length < chars; i++)
        {
            sb.Append(string.Format(System.Globalization.CultureInfo.InvariantCulture, templates[i % templates.Length], i)).Append('\n');
        }

        return sb.ToString();
    }
}
