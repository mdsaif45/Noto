using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Noto.Core.Folders;
using Noto.Core.Notes;
using Noto.Core.Search;
using Noto.Infrastructure.Storage;
using Noto.Infrastructure.Tests.Folders;
using Noto.UseCases.Workspace;
using Xunit;

namespace Noto.Infrastructure.Tests.Notes;

/// <summary>
/// Search over the real index (ADR-018): what the triggers keep, what a query means, what is shown, and the budget.
/// </summary>
public sealed class SearchTests : IDisposable
{
    private readonly FolderTestContext _db = new();

    public void Dispose() => _db.Dispose();

    private List<NoteId> Find(string text, FolderId? folder = null) =>
        _db.Notes.FindMatching(SearchQuery.Parse(text), folder, 100).Select(n => n.Id).ToList();

    [Fact]
    public void A_word_start_matches_ignoring_case_and_diacritics_but_not_mid_word()
    {
        FolderId folder = _db.SeedFolder();
        NoteId note = _db.SeedNote(folder, "# Weekly sync\n\nThe Café meeting");

        Assert.Equal([note], Find("meet"));
        Assert.Equal([note], Find("CAFE"));
        Assert.Equal([note], Find("café WEEK"));
        Assert.Empty(Find("ting"));
        Assert.Empty(Find("meet absent"));
    }

    [Theory]
    [InlineData("it's")]
    [InlineData("\"quoted\"")]
    [InlineData("star*")]
    [InlineData("NEAR(a b)")]
    [InlineData("AND")]
    [InlineData("OR NOT")]
    [InlineData("col:on")]
    [InlineData("-minus")]
    [InlineData("(paren")]
    [InlineData("^caret")]
    [InlineData("{brace}")]
    public void No_typed_text_is_FTS5_syntax(string text)
    {
        // ADR-003: an apostrophe once crashed search. Every one of these is a plain term.
        FolderId folder = _db.SeedFolder();
        _db.SeedNote(folder, "nothing relevant");

        List<NoteId> found = Find(text);

        Assert.Empty(found);
    }

    [Fact]
    public void Punctuation_typed_inside_a_word_finds_that_word()
    {
        FolderId folder = _db.SeedFolder();
        NoteId note = _db.SeedNote(folder, "it's the C# and foo-bar note");

        Assert.Equal([note], Find("it's"));
        Assert.Equal([note], Find("foo-bar"));
        Assert.Equal([note], Find("\"note\""));
    }

    [Fact]
    public void An_edit_reindexes_the_note_and_nothing_else_does()
    {
        FolderId folder = _db.SeedFolder();
        FolderId other = _db.SeedFolder("Other");
        NoteId note = _db.SeedNote(folder, "alpha");

        Execute("UPDATE Notes SET Content = 'beta' WHERE Id = $id;", note);
        Assert.Empty(Find("alpha"));
        Assert.Equal([note], Find("beta"));

        // Moving, pinning, folding and reordering leave the index alone (the update trigger is on Content).
        Execute($"UPDATE Notes SET FolderId = '{other.Value}', IsPinned = 1, IsFolded = 1, SortOrder = 9 WHERE Id = $id;", note);
        Assert.Equal([note], Find("beta"));
        Assert.Equal(1L, IndexCount("beta"));
    }

    [Fact]
    public void Binned_notes_and_notes_in_binned_folders_are_not_shown()
    {
        FolderId folder = _db.SeedFolder();
        FolderId binned = _db.SeedFolder("Binned", deletedAt: _db.Clock.UtcNow);
        _db.SeedNote(folder, "keyword deleted", deletedAt: _db.Clock.UtcNow);
        _db.SeedNote(binned, "keyword in a binned folder");
        NoteId shown = _db.SeedNote(folder, "keyword shown");

        Assert.Equal([shown], Find("keyword"));
    }

    [Fact]
    public void A_purged_note_leaves_the_index()
    {
        FolderId folder = _db.SeedFolder();
        NoteId note = _db.SeedNote(folder, "ephemeral", deletedAt: _db.Clock.UtcNow);

        Assert.Equal(1L, IndexCount("ephemeral"));
        Execute("DELETE FROM Notes WHERE Id = $id;", note);
        Assert.Equal(0L, IndexCount("ephemeral"));
    }

    [Fact]
    public void A_folder_scope_narrows_the_search()
    {
        FolderId here = _db.SeedFolder("Here");
        FolderId there = _db.SeedFolder("There");
        NoteId mine = _db.SeedNote(here, "shared word");
        NoteId theirs = _db.SeedNote(there, "shared word");

        Assert.Equal([mine], Find("shared", here));
        Assert.Equal(2, Find("shared").Count);
        Assert.Contains(theirs, Find("shared"));
    }

    [Fact]
    public void The_index_rank_orders_notes_whose_titles_do_not_match()
    {
        // bm25: more of the word in less text ranks first.
        FolderId folder = _db.SeedFolder();
        NoteId once = _db.SeedNote(folder, "First\n\n" + string.Concat(Enumerable.Repeat("filler words go here ", 40)) + "invoice");
        NoteId often = _db.SeedNote(folder, "Second\n\ninvoice invoice invoice");

        Assert.Equal([often, once], Find("invoice"));
    }

    [Fact]
    public void The_limit_is_kept()
    {
        FolderId folder = _db.SeedFolder();
        for (int i = 0; i < 12; i++)
        {
            _db.SeedNote(folder, $"common {i}");
        }

        Assert.Equal(5, _db.Notes.FindMatching(SearchQuery.Parse("common"), null, 5).Count);
    }

    [Fact]
    public void An_empty_query_is_refused_rather_than_run()
    {
        Assert.Throws<ArgumentException>(() => _db.Notes.FindMatching(SearchQuery.Parse("  "), null, 10));
    }

    [Fact]
    public void Folders_come_first_and_notes_whose_title_matches_lead_the_notes()
    {
        FolderId groceries = _db.SeedFolder("Groceries");
        _db.SeedFolder("Work");
        NoteId body = _db.SeedNote(groceries, "Shopping\n\ngroceries for the week groceries groceries");
        NoteId title = _db.SeedNote(groceries, "Groceries list\n\neggs");
        var search = new SearchWorkspaceQuery(_db.Notes, _db.Folders);

        SearchResults all = search.Execute("groc", null);

        Assert.Equal([groceries], all.Folders.Select(f => f.Id));
        Assert.Equal([title, body], all.Notes.Select(n => n.Id));
        Assert.Equal("Groceries", all.Notes[0].FolderName);
        Assert.Equal([new TextRange(0, 4)], all.Notes[0].TitleMatches);
        Assert.Equal("eggs", all.Notes[0].Snippet);
        Assert.Equal(0, all.Notes[0].Caret);
        Assert.Equal(10, all.Notes[1].Caret);

        // A folder is offered only when its name has every word.
        FolderId plans = _db.SeedFolder("Work plans");
        Assert.Equal([plans], search.Execute("plan work", null).Folders.Select(f => f.Id));
        Assert.Empty(search.Execute("work budget", null).Folders);

        // Narrowed to a folder, folders are not offered.
        Assert.Empty(search.Execute("groc", groceries).Folders);
        Assert.True(search.Execute("   ", null).IsEmpty);
    }

    [Fact]
    public void An_existing_database_is_indexed_when_it_reaches_schema_4()
    {
        FolderId folder = _db.SeedFolder();
        NoteId note = _db.SeedNote(folder, "written before search existed");

        // Back to schema 3, as a database from before this release: no index, no triggers.
        using (SqliteConnection connection = _db.Database.OpenConnection())
        {
            foreach (string sql in new[]
            {
                "DROP TRIGGER Notes_Fts_Insert;", "DROP TRIGGER Notes_Fts_Delete;", "DROP TRIGGER Notes_Fts_Update;",
                "DROP TABLE NotesFts;", "PRAGMA user_version = 3;",
            })
            {
                using var command = connection.CreateCommand();
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
        }

        _db.Database.Release();
        new NotoDatabase(_db.DatabasePath).Initialize();

        Assert.Equal([note], Find("existed"));
    }

    [Fact]
    public void Nothing_runs_VACUUM_without_rebuilding_the_index()
    {
        // ADR-012 / ADR-018 §1: VACUUM can renumber the implicit rowid the index is keyed by.
        string src = SourceRoot();
        foreach (string file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            string code = Regex.Replace(File.ReadAllText(file), @"^\s*///?.*$", string.Empty, RegexOptions.Multiline);
            if (Regex.IsMatch(code, @"\bVACUUM\b", RegexOptions.IgnoreCase))
            {
                Assert.Contains("INSERT INTO NotesFts (NotesFts) VALUES ('rebuild')", code, StringComparison.Ordinal);
            }
        }
    }

    private void Execute(string sql, NoteId id)
    {
        using SqliteConnection connection = _db.Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", id.Value);
        command.ExecuteNonQuery();
    }

    /// <summary>Index rows for a word, deleted notes included: what the triggers keep, not what Search shows.</summary>
    private long IndexCount(string word)
    {
        using SqliteConnection connection = _db.Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM NotesFts WHERE NotesFts MATCH $w;";
        command.Parameters.AddWithValue("$w", word);
        return (long)command.ExecuteScalar()!;
    }

    private static string SourceRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Join(directory.FullName, "src");
            if (Directory.Exists(Path.Join(candidate, "Noto.Infrastructure")))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("src not found above " + AppContext.BaseDirectory);
    }
}
