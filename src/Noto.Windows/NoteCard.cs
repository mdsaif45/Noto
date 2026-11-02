using Microsoft.UI.Xaml;

namespace Noto;

/// <summary>
/// What the note list shows for one note.
/// </summary>
/// <remarks>
/// <para>
/// UI-owned for the same reason as <see cref="FolderListItem"/>: the XAML
/// compiler generates a property <i>setter</i> for every member of a type
/// named in <c>x:DataType</c>, and <see cref="Core.Notes.Note"/> is an
/// immutable record whose properties are <c>init</c>-only. Presentation
/// references the domain, never the reverse (ADR-009), so the accommodation
/// belongs on this side of the boundary.
/// </para>
/// <para>
/// Carries only what the list renders. The domain note also has
/// <c>Content</c>, <c>ColorKey</c>, <c>IsFolded</c>, <c>SortOrder</c> and
/// timestamps; none has an M2-2 surface, and copying them here because they
/// exist is how a display model quietly becomes a view-model.
/// </para>
/// </remarks>
public sealed class NoteListItem
{
    /// <summary>
    /// What a note with no title is called.
    /// </summary>
    /// <remarks>
    /// <see cref="Core.Notes.Note.Title"/> is the first non-empty line and is
    /// the <b>empty string</b> when there is none (contract §3) — an ordinary
    /// state for a new or whitespace-only note, not a defect. A row rendering
    /// nothing would be invisible in the list and would announce nothing to a
    /// screen reader, so the empty case is named rather than blank.
    /// </remarks>
    public const string UntitledLabel = "Untitled note";

    /// <summary>
    /// Required by the XAML type activator.
    /// </summary>
    /// <remarks>
    /// Without one the application launches and then fail-fasts with
    /// <c>0xC0000C04</c> the moment the list renders its first row — see
    /// <see cref="FolderListItem"/> for the full account.
    /// </remarks>
    public NoteListItem()
    {
    }

    public NoteListItem(string id, string title, bool isPinned)
    {
        Id = id;
        Title = title;
        IsPinned = isPinned;
    }

    /// <summary>
    /// The note's id, as its canonical ULID string.
    /// </summary>
    /// <remarks>
    /// <b>The pane addresses notes by this, never by row index or title.</b>
    /// A note's title is just its first line (parity B16), so two notes
    /// routinely share one; and O2 orders by pinned-then-SortOrder-then-Id, so
    /// a pin change moves a row and an index goes stale across a re-query.
    /// </remarks>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The note's title, exactly as the domain computes it.
    /// </summary>
    /// <remarks>
    /// Assigned from <see cref="Core.Notes.Note.Title"/>. The derivation — first
    /// non-empty line, ATX markers stripped — is contract §3 and stays in the
    /// domain; duplicating it here would be a second place for it to drift.
    /// </remarks>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Whether the note is pinned.
    /// </summary>
    /// <remarks>
    /// O2 sorts pinned notes to the top (parity B14). Without surfacing the
    /// flag the grouping is invisible and the order looks arbitrary.
    /// </remarks>
    public bool IsPinned { get; set; }

    /// <summary>
    /// What the row displays and announces.
    /// </summary>
    /// <remarks>
    /// The title, or <see cref="UntitledLabel"/> when the note has none. A
    /// projection rather than a converter: a converter would be a second type,
    /// registered in XAML, to express one fallback.
    /// </remarks>
    public string DisplayTitle => string.IsNullOrEmpty(Title) ? UntitledLabel : Title;

    /// <summary>Whether the pinned marker is shown for this row.</summary>
    public Visibility PinnedMarkerVisibility =>
        IsPinned ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// The note's display title.
    /// </summary>
    /// <remarks>
    /// <b>Accessibility, not debugging.</b> A generated <c>ListViewItem</c>
    /// container has no automation name of its own and falls back to
    /// <see cref="object.ToString"/> on its data item, so the default would
    /// announce "Noto.NoteListItem" to a screen reader for every row.
    /// </remarks>
    public override string ToString() => DisplayTitle;
}
