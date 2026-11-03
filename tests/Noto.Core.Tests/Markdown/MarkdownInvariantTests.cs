using System.Text;
using Noto.Core.Markdown;
using Xunit;

namespace Noto.Core.Tests.Markdown;

/// <summary>
/// Invariants I1–I5 and I7 (markdown-model.md §1) over seeded random sources and random edits. The seeds are fixed so
/// a failure reproduces; the alphabet is mostly syntax, plus the awkward text.
/// </summary>
public sealed class MarkdownInvariantTests
{
    private static readonly string[] Alphabet =
    [
        "*", "**", "_", "__", "~~", "~", "==", "=", "`", "```", "~~~", "[", "]", "(", ")", "![", "\\", "#", "## ", "> ",
        "- ", "* ", "1. ", "- [ ] ", "- [x] ", "---", " ", "  ", "\t", "a", "word", "b", "1", ".", "\n", "\r\n", "\r",
        "👍", "👨‍👩‍👧", "漢字", "é", "\uD83D", "\uDC4D", "https://x.y/(z)",
    ];

    public static TheoryData<int> Seeds() => new(Enumerable.Range(1, 40));

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Random_sources_parse_well_formed_and_deterministically(int seed)
    {
        var random = new Random(seed);

        for (int n = 0; n < 50; n++)
        {
            string source = RandomSource(random, random.Next(0, 60));

            MarkdownDocument document = MarkdownDocument.Parse(source);

            AssertWellFormed(document);
            Assert.Equal(Md.Shape(document), Md.Shape(MarkdownDocument.Parse(source)));
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void After_any_edit_everything_outside_the_changed_region_styles_as_before(int seed)
    {
        var random = new Random(seed);

        for (int n = 0; n < 50; n++)
        {
            string before = RandomSource(random, random.Next(0, 40));
            (int start, int removed, string inserted) = RandomEdit(random, before, Alphabet);
            string after = Md.Edit(before, start, removed, inserted);

            AssertRegionIsComplete(MarkdownDocument.Parse(before), MarkdownDocument.Parse(after));
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void An_edit_that_cannot_open_or_close_a_fence_changes_only_its_own_block(int seed)
    {
        // No backtick, tilde or line break in what is typed or deleted: the fence state cannot change (I7).
        string[] local = [.. Alphabet.Where(t => !t.Contains('`', StringComparison.Ordinal) && !t.Contains('~', StringComparison.Ordinal) && !t.Contains('\n', StringComparison.Ordinal) && !t.Contains('\r', StringComparison.Ordinal))];
        var random = new Random(seed);
        int checkedEdits = 0;

        foreach (string note in MarkdownCorpus.Realistic.Select(r => r.Text))
        {
            for (int n = 0; n < 20; n++)
            {
                (int start, int removed, string inserted) = RandomEdit(random, note, local);
                string gone = note.Substring(start, removed);
                int lineStart = note.AsSpan(0, start).LastIndexOfAny('\r', '\n') + 1;
                int lineEnd = note.AsSpan(start).IndexOfAny('\r', '\n') is var e and >= 0 ? start + e : note.Length;

                // Typing on a fence line itself ("```" + "a") can close or open a fence: that is not a local edit.
                if (gone.AsSpan().IndexOfAny("\r\n") >= 0 || note.AsSpan(lineStart, lineEnd - lineStart).IndexOfAny('`', '~') >= 0)
                {
                    continue;
                }

                string after = Md.Edit(note, start, removed, inserted);
                MarkdownDocument now = MarkdownDocument.Parse(after);
                (int regionStart, int length) = MarkdownDocument.ChangedRegion(MarkdownDocument.Parse(note), now);

                MarkdownBlock edited = now.Blocks.First(b => b.End >= start);
                Assert.True(
                    length == 0 || (regionStart >= edited.Start && regionStart + length <= edited.End),
                    $"seed {seed}: an edit at {start} restyled [{regionStart}, {regionStart + length}) outside its block [{edited.Start}, {edited.End})");
                checkedEdits++;
            }
        }

        Assert.True(checkedEdits > 0);
    }

    [Fact]
    public void Every_corpus_note_is_well_formed()
    {
        foreach ((string name, string text) in MarkdownCorpus.Realistic.Concat([("dense 100 KB", MarkdownCorpus.Dense(100_636))]))
        {
            MarkdownDocument document = MarkdownDocument.Parse(text);

            AssertWellFormed(document);
            Assert.True(document.Blocks.Count > 0, name);
        }
    }

    /// <summary>I1, I2, I3, I5: the source untouched; blocks tile it line by line; spans in their block, ordered, whole.</summary>
    internal static void AssertWellFormed(MarkdownDocument document)
    {
        string source = document.Source;

        // Blocks tile the source: in order, separated by exactly one line break each, from 0 to the end.
        Assert.Equal(0, document.Blocks[0].Start);
        Assert.Equal(source.Length, document.Blocks[^1].End);

        for (int i = 1; i < document.Blocks.Count; i++)
        {
            string gap = source[document.Blocks[i - 1].End..document.Blocks[i].Start];
            Assert.True(gap is "\n" or "\r" or "\r\n", $"between blocks {i - 1} and {i}: '{gap}'");
        }

        MarkdownSpan? previous = null;

        foreach (MarkdownBlock block in document.Blocks)
        {
            foreach (MarkdownSpan span in block.Spans)
            {
                Assert.True(span.Length > 0, $"empty span {span}");
                Assert.True(span.Start >= block.Start && span.End <= block.End, $"{span} outside its block [{block.Start}, {block.End})");
                Assert.DoesNotContain(source.Substring(span.Start, span.Length), c => c is '\r' or '\n');
                Assert.False(SplitsPair(source, span.Start), $"{span} starts inside a surrogate pair");
                Assert.False(SplitsPair(source, span.End), $"{span} ends inside a surrogate pair");

                if (previous is { } p)
                {
                    Assert.True(p.Start < span.Start || (p.Start == span.Start && (p.Length > span.Length || (p.Length == span.Length && p.Kind <= span.Kind))), $"{p} then {span}: out of order");
                }

                previous = span;
            }
        }

        Assert.Equal(document.Blocks.Sum(b => b.Spans.Count), document.Spans.Count);
    }

    /// <summary>The host restyles only the changed region: outside it, after's spans must be before's, shifted past it.</summary>
    internal static void AssertRegionIsComplete(MarkdownDocument before, MarkdownDocument after)
    {
        (int start, int length) = MarkdownDocument.ChangedRegion(before, after);
        int shift = after.Source.Length - before.Source.Length;
        int end = start + length;

        var outsideAfter = after.Spans.Where(s => s.End <= start || s.Start >= end).ToList();
        var outsideBefore = before.Spans.Where(s => s.End <= start || s.Start >= end - shift).Select(s => s.Start >= end - shift ? s with { Start = s.Start + shift } : s).ToList();

        Assert.Equal(outsideBefore, outsideAfter);
    }

    private static bool SplitsPair(string source, int at) =>
        at > 0 && at < source.Length && char.IsHighSurrogate(source[at - 1]) && char.IsLowSurrogate(source[at]);

    private static string RandomSource(Random random, int tokens)
    {
        var text = new StringBuilder();

        for (int i = 0; i < tokens; i++)
        {
            text.Append(Alphabet[random.Next(Alphabet.Length)]);
        }

        return text.ToString();
    }

    private static (int Start, int Removed, string Inserted) RandomEdit(Random random, string source, string[] alphabet)
    {
        int start = random.Next(0, source.Length + 1);
        int removed = random.Next(0, Math.Min(4, source.Length - start) + 1);
        string inserted = random.Next(3) == 0 ? string.Empty : alphabet[random.Next(alphabet.Length)];
        return (start, removed, inserted);
    }
}
