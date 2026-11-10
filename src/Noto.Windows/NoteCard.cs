using System.ComponentModel;
using Microsoft.UI.Xaml;
using Noto.Core.Notes;

namespace Noto;

/// <summary>
/// One card in the note stack (ADR-016 §2): what it shows and whether it is the card being edited.
/// </summary>
/// <remarks>
/// <para>
/// UI-owned, as <see cref="FolderListItem"/> is: the XAML compiler generates a setter for every member of a type
/// named in <c>x:DataType</c>, and the domain <see cref="Note"/> is immutable. Presentation references the domain,
/// never the reverse (ADR-009). Nothing here is stored on the note but state the domain owns.
/// </para>
/// <para>
/// Notifies changes because a card is not rebuilt when it starts or stops editing, or when its text is saved: its
/// container, and the keyboard focus in it, stay where they are.
/// </para>
/// </remarks>
public sealed class NoteCard : INotifyPropertyChanged
{
    /// <summary>What a note with no title is called: a card that showed nothing would be invisible and silent.</summary>
    public const string UntitledLabel = "Untitled note";

    private string _content = string.Empty;
    private bool _isEditing;
    private bool _isFolded;
    private string? _colorKey;

    /// <summary>Required by the XAML type activator (see <see cref="FolderListItem"/>).</summary>
    public NoteCard()
    {
    }

    public NoteCard(Note note, bool isEditing)
    {
        ArgumentNullException.ThrowIfNull(note);

        Id = note.Id.Value;
        IsPinned = note.IsPinned;
        _isFolded = note.IsFolded;
        _colorKey = note.ColorKey;
        _content = note.Content;
        _isEditing = isEditing;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The note's id. The stack addresses cards by it, never by index or title (B16: titles repeat).</summary>
    public string Id { get; set; } = string.Empty;

    public bool IsPinned { get; set; }

    /// <summary>Whether the card shows its title only (B12). Changed in place, so the card keeps its container and focus.</summary>
    public bool IsFolded
    {
        get => _isFolded;
        set
        {
            _isFolded = value;
            Changed(nameof(IsFolded));
            Changed(nameof(ReadText));
            Changed(nameof(HelpText));
            Changed(nameof(FoldedMarkerVisibility));
        }
    }

    /// <summary>The note's palette key (F7, #150), or <see langword="null"/>; its paint is the card's (NotePalette).</summary>
    public string? ColorKey
    {
        get => _colorKey;
        set
        {
            _colorKey = value;
            Changed(nameof(ColorKey));
            Changed(nameof(HelpText));
        }
    }

    /// <summary>A folded card's chevron: folded is visible at a glance, not only in its help text.</summary>
    public Visibility FoldedMarkerVisibility => IsFolded ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Pinned cards sort first (O2); the marker makes that grouping visible.</summary>
    public Visibility PinnedMarkerVisibility => IsPinned ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The note's markdown source, as stored.</summary>
    public string Content
    {
        get => _content;
        set
        {
            _content = value;
            Changed(nameof(Content));
            Changed(nameof(DisplayTitle));
            Changed(nameof(ReadText));
        }
    }

    /// <summary>Whether this is the one card being edited: it holds the editor and its bar, and hides its read view.</summary>
    public bool IsEditing
    {
        get => _isEditing;
        set
        {
            _isEditing = value;
            Changed(nameof(IsEditing));
            Changed(nameof(IsReading));
        }
    }

    /// <summary>The read view shows while the card is not being edited.</summary>
    public bool IsReading => !IsEditing;

    /// <summary>The title (B16), computed by the domain, or <see cref="UntitledLabel"/>.</summary>
    public string DisplayTitle => NoteTitle.From(Content) is { Length: > 0 } title ? title : UntitledLabel;

    /// <summary>
    /// The read view (ADR-016 §3): the whole source, or only the title when folded. P2 replaces this with the
    /// rendered note; the seam is this one property.
    /// </summary>
    public string ReadText => IsFolded || string.IsNullOrWhiteSpace(Content) ? DisplayTitle : Content;

    /// <summary>
    /// The card's states, for assistive technology: its name is the title (ADR-016 §8). A colour is named here too,
    /// so it is never only a colour (F7, #150).
    /// </summary>
    public string HelpText
    {
        get
        {
            string states = (IsPinned, IsFolded) switch
            {
                (true, true) => "Pinned, folded",
                (true, false) => "Pinned",
                (false, true) => "Folded",
                _ => string.Empty,
            };

            return NotePalette.NameOf(ColorKey) is not { } colour ? states
                : states.Length == 0 ? colour
                : $"{states}, {colour}";
        }
    }

    /// <summary>
    /// The title. <b>Accessibility:</b> a generated container with no name of its own announces its item's
    /// <see cref="object.ToString"/>, which would otherwise be "Noto.NoteCard" for every card.
    /// </summary>
    public override string ToString() => DisplayTitle;

    private void Changed(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
