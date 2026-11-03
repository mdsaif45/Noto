using System.Diagnostics;
using Noto.Core.Markdown;
using Xunit;
using Xunit.Abstractions;

namespace Noto.Core.Tests.Markdown;

/// <summary>
/// Realistic notes and the dense corpus: what they parse as, how many spans they hold (ADR-017 §5's question), and
/// what a parse costs (markdown-model.md §6). The ceilings are generous so a slow CI runner cannot fail them; the
/// measured numbers are written to the test output.
/// </summary>
public sealed class MarkdownCorpusTests(ITestOutputHelper output)
{
    [Fact]
    public void Realistic_notes_parse_into_the_blocks_they_are_written_as()
    {
        Dictionary<string, MarkdownDocument> parsed = MarkdownCorpus.Realistic.ToDictionary(r => r.Name, r => MarkdownDocument.Parse(r.Text));

        Assert.Equal(6, parsed["groceries"].Blocks.Count(b => b.Kind == MarkdownBlockKind.TaskItem));
        Assert.Equal(2, parsed["groceries"].Blocks.Count(b => b.IsChecked));
        Assert.Equal(4, parsed["meeting notes"].Blocks.Count(b => b.Kind == MarkdownBlockKind.TaskItem));
        Assert.Equal(2, parsed["code snippets"].Blocks.Count(b => b.Kind == MarkdownBlockKind.CodeFence));
        Assert.DoesNotContain(parsed["code snippets"].Spans, s => s.Kind == MarkdownSpanKind.Emphasis && parsed["code snippets"].Source.Substring(s.Start, s.Length) == "not");
        Assert.Equal(6, parsed["reading list"].Spans.Count(s => s.Kind == MarkdownSpanKind.Link));
        Assert.Contains(parsed["project plan"].Blocks, b => b.Kind == MarkdownBlockKind.OrderedItem);
        Assert.Contains(parsed["project plan"].Blocks, b => b.Kind == MarkdownBlockKind.BulletItem && b.Indent == 3);
        Assert.Equal(2, parsed["project plan"].Blocks.Count(b => b.Kind == MarkdownBlockKind.Separator || b.Kind == MarkdownBlockKind.Quote));

        // Pasted text keeps its CRLFs; "5*3" and "* … *" with spaces around are text, as are snake_case names.
        MarkdownDocument pasted = parsed["pasted document"];
        Assert.Contains("\r\n", pasted.Source, StringComparison.Ordinal);
        Assert.Equal(["printed"], pasted.Spans.Where(s => s.Kind != MarkdownSpanKind.Markup).Select(s => pasted.Source.Substring(s.Start, s.Length)));
    }

    [Fact]
    public void Span_density_of_realistic_notes_against_the_dense_corpus()
    {
        // ADR-017 §5: how often would a 400-span limit be reached? Reported, and held loosely: realistic notes are
        // far below the dense corpus's density.
        foreach ((string name, string text) in MarkdownCorpus.Realistic)
        {
            MarkdownDocument document = MarkdownDocument.Parse(text);
            output.WriteLine($"{name,-18} {text.Length,7:N0} chars {document.Spans.Count,6:N0} spans {1000.0 * document.Spans.Count / text.Length,6:F1} per 1000 chars");
        }

        string large = MarkdownCorpus.LargeRealistic(100_000);
        string dense = MarkdownCorpus.Dense(100_636);
        double largeDensity = 1000.0 * MarkdownDocument.Parse(large).Spans.Count / large.Length;
        double denseDensity = 1000.0 * MarkdownDocument.Parse(dense).Spans.Count / dense.Length;
        output.WriteLine($"{"large realistic",-18} {large.Length,7:N0} chars {MarkdownDocument.Parse(large).Spans.Count,6:N0} spans {largeDensity,6:F1} per 1000 chars");
        output.WriteLine($"{"dense (spike)",-18} {dense.Length,7:N0} chars {MarkdownDocument.Parse(dense).Spans.Count,6:N0} spans {denseDensity,6:F1} per 1000 chars");

        Assert.True(largeDensity < denseDensity, $"realistic {largeDensity:F1} vs dense {denseDensity:F1}");
    }

    [Theory]
    [InlineData("dense", 100_636, 250)]
    [InlineData("large realistic", 100_000, 250)]
    [InlineData("realistic", 2_000, 25)]
    public void A_parse_stays_inside_its_budget(string corpus, int chars, int ceilingMs)
    {
        string text = corpus switch
        {
            "dense" => MarkdownCorpus.Dense(chars),
            "large realistic" => MarkdownCorpus.LargeRealistic(chars),
            _ => MarkdownCorpus.Realistic[1].Text,
        };

        for (int warm = 0; warm < 3; warm++)
        {
            _ = MarkdownDocument.Parse(text);
        }

        var times = new List<double>();
        long allocated = 0;

        for (int run = 0; run < 15; run++)
        {
            long bytes = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            MarkdownDocument document = MarkdownDocument.Parse(text);
            times.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
            GC.KeepAlive(document);
        }

        times.Sort();
        double median = times[times.Count / 2];
        output.WriteLine($"{corpus}: {text.Length:N0} chars, median {median:F3} ms, max {times[^1]:F3} ms, {allocated / 1024.0:F0} KB allocated per parse");

        Assert.True(median < ceilingMs, $"median {median:F3} ms");
    }
}
