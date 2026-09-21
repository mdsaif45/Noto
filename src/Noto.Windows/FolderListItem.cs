using Microsoft.UI.Xaml;

namespace Noto;

/// <summary>
/// What the folder pane shows for one folder.
/// </summary>
/// <remarks>
/// <para>
/// <b>This type exists because of a WinUI constraint, not a domain one, and
/// the distinction matters.</b> The XAML compiler generates a property
/// <i>setter</i> for every member of any type named in <c>x:DataType</c>.
/// <see cref="Core.Folders.Folder"/> is an immutable record whose properties
/// are <c>init</c>-only, so the generated <c>XamlTypeInfo.g.cs</c> does not
/// compile against it — thirteen CS8852 errors, one per property.
/// </para>
/// <para>
/// The domain model is correct as it stands: immutability is what makes a
/// command the only way to change state (ADR-010). The accommodation therefore
/// belongs on this side of the boundary. Presentation references the domain,
/// never the reverse (ADR-009), and this is exactly that rule being applied —
/// a presentation concern absorbed by presentation.
/// </para>
/// <para>
/// Kept to what the pane displays. It is not a view-model layer and should
/// not become one without a decision to that effect: the moment it carries
/// behaviour or state, the question of where presentation logic lives needs
/// answering properly.
/// </para>
/// </remarks>
public sealed class FolderListItem
{
    /// <summary>
    /// Required by the XAML type activator.
    /// </summary>
    /// <remarks>
    /// <b>Not optional, and the failure mode is brutal.</b> The generated
    /// <c>XamlTypeInfo</c> activates a bound type through a parameterless
    /// constructor. Without one the application launches, shows its window,
    /// and then fail-fasts with <c>0xC0000C04</c> the moment the list renders
    /// its first row — no exception dialog, no managed stack trace, and an
    /// empty list looks perfectly healthy. Found by launching the real host
    /// with one folder in the database; a headless test cannot see it.
    /// </remarks>
    public FolderListItem()
    {
    }

    public FolderListItem(string id, string name, bool isPinned)
    {
        Id = id;
        Name = name;
        IsPinned = isPinned;
    }

    /// <summary>
    /// The folder's id, as its canonical ULID string.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The pane addresses folders by this, never by row index.</b> Folder
    /// names are deliberately not unique (contract Case G), so a name cannot
    /// identify a row; and O2 orders by pinned-then-SortOrder-then-Id, so a
    /// rename can move a row and an index goes stale the moment the list is
    /// re-queried.
    /// </para>
    /// <para>
    /// A <see langword="string"/> rather than a
    /// <see cref="Core.Folders.FolderId"/> for the same reason this whole type
    /// exists — the XAML compiler needs a settable property. It is converted
    /// back at the command boundary, where an invalid value would be a
    /// programming error rather than user input.
    /// </para>
    /// </remarks>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The folder's name, exactly as stored.
    /// </summary>
    /// <remarks>
    /// Not trimmed. The engine stores folder names verbatim — §7a's trimming
    /// rule is scoped to tags and says explicitly that "no other entity's name
    /// is affected" — so <c>" Work "</c> is displayed as it was typed. The pane
    /// must not normalise here: that would make the list disagree with what a
    /// rename round-trips.
    /// </remarks>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Whether the folder is pinned.
    /// </summary>
    /// <remarks>
    /// O2 sorts pinned folders to the top of the list. Without surfacing the
    /// flag the grouping is invisible and the order looks arbitrary — the user
    /// sees folders that are not in name order and not in creation order, with
    /// nothing on screen explaining why.
    /// </remarks>
    public bool IsPinned { get; set; }

    /// <summary>
    /// Whether the pinned marker is shown for this row.
    /// </summary>
    /// <remarks>
    /// A projection of <see cref="IsPinned"/> rather than a converter: a
    /// converter would be a second type, registered in XAML, to express one
    /// boolean. This stays on the UI-owned model, which is where a
    /// presentation concern belongs (ADR-009), and keeps the domain free of
    /// <see cref="Visibility"/>.
    /// </remarks>
    public Visibility PinnedMarkerVisibility =>
        IsPinned ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// The folder's name.
    /// </summary>
    /// <remarks>
    /// <b>Accessibility, not debugging.</b> A generated <c>ListViewItem</c>
    /// container has no automation name of its own and falls back to
    /// <see cref="object.ToString"/> on its data item, so the default would
    /// announce "Noto.FolderListItem" to a screen reader for every row.
    /// Observed with UI Automation against the running application.
    /// </remarks>
    public override string ToString() => Name;
}
