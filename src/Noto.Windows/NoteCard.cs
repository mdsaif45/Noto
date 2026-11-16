using System.ComponentModel;
using Microsoft.UI.Xaml;
using Noto.Core.Notes;
using Noto.Core.Workspace;

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
    private NoteMode _mode;

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
        _mode = note.Mode;
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
            Changed(nameof(HiddenLinesText));
            Changed(nameof(UnfoldName));
            Changed(nameof(UnfoldPillVisibility));
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

    /// <summary>
    /// How the note's text is read (#195): Markdown, Code or plain text. Changed in place, as a colour is, so the card
    /// keeps its container and focus; its title, read view and help text follow.
    /// </summary>
    public NoteMode Mode
    {
        get => _mode;
        set
        {
            _mode = value;
            Changed(nameof(Mode));
            Changed(nameof(DisplayTitle));
            Changed(nameof(ReadText));
            Changed(nameof(HelpText));
            Changed(nameof(FormatVisibility));
        }
    }

    /// <summary>The Format button is for Markdown: a Code or plain text note has nothing to format (#195).</summary>
    public Visibility FormatVisibility => Mode == NoteMode.Markdown ? Visibility.Visible : Visibility.Collapsed;

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
            Changed(nameof(HelpText));
            Changed(nameof(HiddenLinesText));
            Changed(nameof(UnfoldName));
            Changed(nameof(UnfoldPillVisibility));
        }
    }

    /// <summary>The lines a folded card hides (F6, #149); counted only while folded, so typing never counts them.</summary>
    public int HiddenLines => IsFolded ? NoteTitle.LinesBelow(Content) : 0;

    /// <summary>A folded card's pill, "+ N lines": how much is hidden, at a glance.</summary>
    public string HiddenLinesText => HiddenLines > 0 ? "+ " + Lines(HiddenLines) : string.Empty;

    /// <summary>The pill's name for assistive technology: what pressing it does, and what it shows.</summary>
    public string UnfoldName => HiddenLines > 0 ? $"Unfold, {Lines(HiddenLines)} hidden" : "Unfold";

    /// <summary>The pill shows on a folded card that hides something; a one-line note folded hides nothing.</summary>
    public Visibility UnfoldPillVisibility => HiddenLines > 0 ? Visibility.Visible : Visibility.Collapsed;

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

    /// <summary>The title (B16), computed by the domain as the note's mode reads it (#195), or <see cref="UntitledLabel"/>.</summary>
    public string DisplayTitle => NoteTitle.From(Content, Mode) is { Length: > 0 } title ? title : UntitledLabel;

    /// <summary>
    /// The read view (ADR-016 §3): the whole source, or only the title when folded. P2 replaces this with the
    /// rendered note; the seam is this one property.
    /// </summary>
    public string ReadText => IsFolded || string.IsNullOrWhiteSpace(Content) ? DisplayTitle : Content;

    /// <summary>
    /// The card's states, for assistive technology (ADR-016 §8): in its help text, item status and full description,
    /// and after its title in its name (#201). A colour is named here too, so it is never only a colour (F7, #150), and
    /// so is a mode that is not Markdown (#195), so a note that shows its markup is never mistaken for one that lost its
    /// formatting.
    /// </summary>
    public string HelpText =>
        RowState.Of(IsPinned, IsFolded, HiddenLines, NotePalette.NameOf(ColorKey), Mode == NoteMode.Markdown ? string.Empty : NameOf(Mode));

    /// <summary>
    /// The card's name: its title, then its states ("Weekly sync, Folded, 3 lines hidden"; #201). <b>Accessibility:</b>
    /// the card's container has no name of its own and announces its item's <see cref="object.ToString"/>, read each
    /// time it is asked, so a fold, colour or mode changed in place is in the name at once. Narrator reads a list
    /// item's name and position, not its help text.
    /// </summary>
    public override string ToString() => RowState.Name(DisplayTitle, HelpText);

    /// <summary>A mode as the card's menu, Settings and the help text name it (#195).</summary>
    public static string NameOf(NoteMode mode) => mode switch
    {
        NoteMode.Code => "Code",
        NoteMode.PlainText => "Plain text",
        _ => "Markdown",
    };

    private static string Lines(int count) => count == 1 ? "1 line" : $"{count} lines";

    private void Changed(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
