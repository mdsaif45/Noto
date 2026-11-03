using Noto.Core.Folders;
using Noto.Core.Notes;
using Noto.Core.Search;

namespace Noto.UseCases.Workspace;

/// <summary>A folder whose name matches: shown first, opened by entering it.</summary>
public sealed record FolderHit(FolderId Id, string Name, IReadOnlyList<TextRange> NameMatches);

/// <summary>A matching note, ready to show and to open (ADR-018 §3).</summary>
public sealed record NoteHit(
    NoteId Id,
    FolderId FolderId,
    string FolderName,
    string Title,
    IReadOnlyList<TextRange> TitleMatches,
    string Snippet,
    IReadOnlyList<TextRange> SnippetMatches,
    int Caret,
    string Content);

/// <summary>What Search shows for one query: folders first, then notes.</summary>
public sealed record SearchResults(SearchQuery Query, IReadOnlyList<FolderHit> Folders, IReadOnlyList<NoteHit> Notes)
{
    public bool IsEmpty => Folders.Count == 0 && Notes.Count == 0;
}

/// <summary>
/// Search (ADR-018): the folders whose name matches, then the notes the index finds, those whose title matches every
/// word first. No <c>CommandResult</c>: an empty result is an answer. A storage failure throws, as every query's does.
/// </summary>
public sealed class SearchWorkspaceQuery(INoteRepository notes, IFolderRepository folders)
{
    /// <summary>The most notes shown (ADR-018 §2).</summary>
    public const int Limit = 100;

    private readonly INoteRepository _notes = notes ?? throw new ArgumentNullException(nameof(notes));
    private readonly IFolderRepository _folders = folders ?? throw new ArgumentNullException(nameof(folders));

    /// <summary>The results for <paramref name="text"/>: empty for a query with no letter or digit, never run.</summary>
    /// <param name="text">What was typed.</param>
    /// <param name="folderId">Only this folder (E9), or <see langword="null"/> for every folder.</param>
    public SearchResults Execute(string? text, FolderId? folderId)
    {
        SearchQuery query = SearchQuery.Parse(text);
        if (query.IsEmpty)
        {
            return new SearchResults(query, [], []);
        }

        IReadOnlyList<Folder> active = _folders.ListActive();
        var names = active.ToDictionary(f => f.Id, f => f.Name);

        // A narrowed search is about notes in that folder: folders are not offered.
        var folderHits = folderId is null
            ? active.Where(f => SearchText.MatchesAll(f.Name, query))
                .Select(f => new FolderHit(f.Id, f.Name, SearchText.Matches(f.Name, query)))
                .ToList()
            : [];

        // The index's order is kept within each group: OrderBy is stable.
        var noteHits = _notes.FindMatching(query, folderId, Limit)
            .Select(n => Hit(n, query, names))
            .OrderBy(h => SearchText.MatchesAll(h.Title, query) ? 0 : 1)
            .ToList();

        return new SearchResults(query, folderHits, noteHits);
    }

    private static NoteHit Hit(Note note, SearchQuery query, Dictionary<FolderId, string> names)
    {
        string title = note.Title;
        string snippet = SearchText.Snippet(note.Content, query);
        FolderId folder = note.FolderId ?? throw new InvalidOperationException("An active note has a folder (I7).");

        return new NoteHit(
            note.Id,
            folder,
            names.GetValueOrDefault(folder, string.Empty),
            title,
            SearchText.Matches(title, query),
            snippet,
            SearchText.Matches(snippet, query),
            SearchText.FirstMatch(note.Content, query),
            note.Content);
    }
}
