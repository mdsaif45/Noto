using System.Diagnostics;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Noto.Core.Folders;
using Noto.Core.Notes;
using Noto.Infrastructure.Tests.Folders;
using Noto.UseCases.Workspace;
using Xunit;
using Xunit.Abstractions;

namespace Noto.Infrastructure.Tests.Notes;

/// <summary>Timed alone: a benchmark sharing the machine with the parallel suite measures the suite.</summary>
[CollectionDefinition(nameof(SearchBenchmark), DisableParallelization = true)]
public sealed class SearchBenchmark;

/// <summary>N5: a search under 100 ms at 10,000 notes (ADR-018 §4), over realistic notes of about 1 KB.</summary>
[Collection(nameof(SearchBenchmark))]
public sealed class SearchBenchmarkTests(ITestOutputHelper output) : IDisposable
{
    private readonly FolderTestContext _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Ten_thousand_notes_answer_within_the_budget()
    {
        FolderId[] folders = Enumerable.Range(0, 10).Select(i => _db.SeedFolder($"Folder {i}")).ToArray();
        var clock = Stopwatch.StartNew();
        SeedMany(folders, 10_000);
        output.WriteLine($"seeded 10,000 notes (index maintained by trigger) in {clock.ElapsedMilliseconds} ms");

        // Each query's best of three: what the query costs, not what else the machine was doing at that moment.
        var search = new SearchWorkspaceQuery(_db.Notes, _db.Folders);
        var timings = new List<double>();
        foreach (string text in new[] { "meet", "project plan", "q", "alpha", "invoice 2026", "rfc", "zzz", "the", "tom", "release" })
        {
            double best = double.MaxValue;
            for (int run = 0; run < 3; run++)
            {
                clock.Restart();
                _ = search.Execute(text, null);
                best = Math.Min(best, clock.Elapsed.TotalMilliseconds);
            }

            timings.Add(best);
        }

        timings.Sort();
        output.WriteLine($"query ms (sorted): {string.Join(", ", timings.Select(t => t.ToString("F1", CultureInfo.InvariantCulture)))}");
        Assert.True(timings[timings.Count / 2] < 100, $"median {timings[timings.Count / 2]} ms");
        Assert.True(timings[^1] < 250, $"slowest {timings[^1]} ms");
    }

    private void SeedMany(FolderId[] folders, int count)
    {
        string[] words = ["alpha", "project", "plan", "meeting", "invoice", "release", "notes", "tom", "priya", "budget", "design", "review"];
        using SqliteConnection connection = _db.Database.OpenConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = "INSERT INTO Notes (Id, FolderId, Content, ColorKey, IsPinned, IsFolded, SortOrder, CreatedAt, UpdatedAt, DeletedAt) VALUES ($id, $f, $c, NULL, 0, 0, $s, $t, $t, NULL);";
        var id = insert.Parameters.Add("$id", SqliteType.Text);
        var folder = insert.Parameters.Add("$f", SqliteType.Text);
        var content = insert.Parameters.Add("$c", SqliteType.Text);
        var sort = insert.Parameters.Add("$s", SqliteType.Real);
        insert.Parameters.AddWithValue("$t", _db.Clock.UtcNow.ToString("O"));

        for (int i = 0; i < count; i++)
        {
            string w(int k) => words[(i * 7 + k * 5) % words.Length];
            id.Value = NoteId.New().Value;
            folder.Value = folders[i % folders.Length].Value;
            sort.Value = i;
            content.Value = $"# {w(0)} {w(1)} {i}\n\nAttendees: Priya, Tom, me. We discussed the {w(2)} and the {w(3)} for 2026.\n\n"
                + $"- [ ] follow up on {w(4)}\n- [x] send the {w(5)}\n\nSee [the RFC](https://example.com/{w(6)}) and `release.md`.\n\n"
                + string.Concat(Enumerable.Repeat($"Plain paragraph text about {w(7)} that goes on for a while. ", 8));
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }
}
