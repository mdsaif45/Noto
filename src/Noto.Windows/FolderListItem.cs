namespace Noto;

/// <summary>
/// What the list shows for one folder.
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
/// Kept to what the spike displays. It is not a view-model layer and should
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

    public FolderListItem(string name) => Name = name;

    /// <summary>
    /// The folder's name, as stored.
    /// </summary>
    /// <remarks>
    /// A plain settable property: <c>x:Bind</c> needs one, and a UI-owned type
    /// can afford it where the domain record cannot.
    /// </remarks>
    public string Name { get; set; } = string.Empty;
}
