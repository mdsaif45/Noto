using Noto.Core.Notes;

namespace Noto.Core.Folders;

/// <summary>
/// How a folder orders its notes (#242; parity C10): the user's own order, or by name, date created or date modified.
/// </summary>
/// <remarks>
/// <para>
/// Chosen per folder, because folders hold different kinds of notes, and stored on the folder by member name
/// (migration 6). A sort is a view over the folder's notes, never a rewrite of them: no note's <c>SortOrder</c> changes,
/// so choosing <see cref="Manual"/> again shows the order the user made.
/// </para>
/// <para>
/// One direction per key, the natural one: names A to Z, dates newest first.
/// </para>
/// </remarks>
public enum NoteSort
{
    /// <summary>The order the user made by moving and dragging (O2): every folder before #242, and the default.</summary>
    Manual = 0,

    /// <summary>By title (the first line, as titled), culture-aware and case-insensitive.</summary>
    Name,

    /// <summary>By <see cref="Note.CreatedAt"/>, newest first.</summary>
    Created,

    /// <summary>
    /// By <see cref="Note.UpdatedAt"/>, newest first: the note's last stored change, which is what the contract stamps
    /// (§4), so a pin, fold or colour counts as well as an edit.
    /// </summary>
    Modified,
}

/// <summary>The rules every surface shares for <see cref="NoteSort"/> (#242).</summary>
public static class NoteSorts
{
    /// <summary>
    /// The sort stored as <paramref name="stored"/>: a member's name, exactly. Anything else, a missing value included,
    /// reads as <see cref="NoteSort.Manual"/>, which is how every folder was ordered before it was stored.
    /// </summary>
    public static NoteSort Parse(string? stored) => stored switch
    {
        nameof(NoteSort.Name) => NoteSort.Name,
        nameof(NoteSort.Created) => NoteSort.Created,
        nameof(NoteSort.Modified) => NoteSort.Modified,
        _ => NoteSort.Manual,
    };

    /// <summary>The sort as the menu names it.</summary>
    public static string NameOf(NoteSort sort) => sort switch
    {
        NoteSort.Name => "Name",
        NoteSort.Created => "Date created",
        NoteSort.Modified => "Date modified",
        _ => "Manual",
    };

    /// <summary>
    /// Why a note cannot be moved by hand in <paramref name="sort"/>, and what to do about it: the notice a refused drag
    /// or move shows.
    /// </summary>
    public static string ManualOnly(NoteSort sort) =>
        $"Sorted by {NameOf(sort).ToLowerInvariant()}: choose Manual to arrange notes yourself";

    /// <summary>
    /// <paramref name="notes"/>, given in O2 order (pinned first, then <c>SortOrder</c>, then <c>Id</c>), in
    /// <paramref name="sort"/>'s order. Pinned notes stay first in every order; notes that tie keep their manual order,
    /// so the result is deterministic. <see cref="NoteSort.Manual"/> returns them as given. In Name, a note with no title
    /// has no name to sort by: it goes after the named ones, not first under an empty string, where its card would read
    /// "Untitled note" at the top of an A to Z list.
    /// </summary>
    public static IReadOnlyList<Note> Apply(IReadOnlyList<Note> notes, NoteSort sort)
    {
        ArgumentNullException.ThrowIfNull(notes);

        // OrderBy is stable: whatever the key leaves tied stays in the manual order it was given in.
        IOrderedEnumerable<Note> pinnedFirst = notes.OrderByDescending(n => n.IsPinned);

        return sort switch
        {
            NoteSort.Name => [.. pinnedFirst.ThenBy(n => string.IsNullOrWhiteSpace(n.Title)).ThenBy(n => n.Title, StringComparer.CurrentCultureIgnoreCase)],
            NoteSort.Created => [.. pinnedFirst.ThenByDescending(n => n.CreatedAt)],
            NoteSort.Modified => [.. pinnedFirst.ThenByDescending(n => n.UpdatedAt)],
            _ => notes,
        };
    }
}
