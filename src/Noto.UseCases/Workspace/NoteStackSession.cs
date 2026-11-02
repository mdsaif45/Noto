using Noto.Core.Commands;
using Noto.Core.Folders;
using Noto.Core.Notes;
using Noto.UseCases.Commands;
using Noto.UseCases.Notes;

namespace Noto.UseCases.Workspace;

/// <summary>
/// The note stack's session (ADR-016): the open folder's notes, the one card being edited, and its unsaved text.
/// </summary>
/// <remarks>
/// <para>
/// Plain and framework-free, so the rules that protect what the user typed are tested without a window: one card is
/// edited at a time; leaving it, editing another, creating or leaving the folder saves first; a save that fails keeps
/// the card editing with its text. The window owns only what is visual: cards, focus, notices.
/// </para>
/// <para>
/// Reads go through the query and every write through the dispatcher (ADR-010). A storage failure is thrown, as from
/// the handlers, for the window to report; a business failure is returned.
/// </para>
/// </remarks>
public sealed class NoteStackSession(ListNotesInFolderQuery list, CommandDispatcher commands)
{
    private readonly ListNotesInFolderQuery _list = list ?? throw new ArgumentNullException(nameof(list));
    private readonly CommandDispatcher _commands = commands ?? throw new ArgumentNullException(nameof(commands));

    /// <summary>The folder whose notes are stacked; <see langword="null"/> when none is open.</summary>
    public FolderId? Folder { get; private set; }

    private List<Note> _notes = [];

    /// <summary>The open folder's notes as last read, in display order (O2), with every save since applied.</summary>
    public IReadOnlyList<Note> Notes => _notes;

    /// <summary>The card being edited; <see langword="null"/> when none is.</summary>
    public NoteId? Editing { get; private set; }

    /// <summary>The editing card's text as the user has it now.</summary>
    public string Draft { get; private set; } = string.Empty;

    /// <summary>The editing card's text as stored.</summary>
    public string Baseline { get; private set; } = string.Empty;

    /// <summary>
    /// Whether the editing card holds text that is not stored. Typing and undoing it is clean; with no card editing,
    /// both texts are empty.
    /// </summary>
    public bool IsDirty => !string.Equals(Draft, Baseline, StringComparison.Ordinal);

    /// <summary>Opens <paramref name="folder"/>: nothing is edited until a card is chosen.</summary>
    public void Open(FolderId folder)
    {
        Folder = folder;
        _notes = [];
        StopEditing();
    }

    /// <summary>Closes the folder. The caller has saved or discarded first (<see cref="EndEdit"/>).</summary>
    public void Close()
    {
        Folder = null;
        _notes = [];
        StopEditing();
    }

    /// <summary>Reads the open folder's notes. An editing card that is no longer there stops editing.</summary>
    public IReadOnlyList<Note> Read()
    {
        _notes = Folder is { } folder ? [.. _list.Execute(folder)] : [];

        if (Editing is { } editing && IndexOf(editing) < 0)
        {
            StopEditing();
        }

        return Notes;
    }

    /// <summary>The editor's text changed. Text for a card that is no longer the editing one is ignored.</summary>
    public void UpdateDraft(NoteId note, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (Editing == note)
        {
            Draft = text;
        }
    }

    /// <summary>Stores the draft if it differs from what is stored. The card keeps editing either way.</summary>
    public CommandResult Save()
    {
        if (!IsDirty || Editing is not { } editing)
        {
            return CommandResult.Success();
        }

        string text = Draft;
        CommandResult result = _commands.Dispatch(new UpdateNoteContent(editing, text));

        if (result.IsSuccess)
        {
            // What is stored now: editing this card again starts from it, not from the read before the save.
            Baseline = text;
            int index = IndexOf(editing);
            _notes[index] = _notes[index] with { Content = text };
        }

        return result;
    }

    /// <summary>Edits <paramref name="note"/>, saving the card being edited first; a failed save keeps that card.</summary>
    public CommandResult BeginEdit(NoteId note)
    {
        if (Editing == note)
        {
            return CommandResult.Success();
        }

        int index = IndexOf(note);

        if (index < 0)
        {
            return CommandResult.Failed(CommandFailure.NotFound($"No note '{note}' in the open folder."));
        }

        CommandResult left = EndEdit();

        if (!left.IsSuccess)
        {
            return left;
        }

        Editing = note;
        Baseline = Notes[index].Content;
        Draft = Baseline;
        return CommandResult.Success();
    }

    /// <summary>Stops editing, saving first; a failed save keeps the card editing with its text.</summary>
    public CommandResult EndEdit()
    {
        CommandResult saved = Save();

        if (saved.IsSuccess)
        {
            StopEditing();
        }

        return saved;
    }

    /// <summary>Stops editing without saving: for a note that is gone, or a close the user chose to discard.</summary>
    public void DiscardEdit() => StopEditing();

    /// <summary>
    /// Creates an empty note at <paramref name="placement"/> beside <paramref name="selected"/> (B5) and edits it,
    /// saving the card being edited first.
    /// </summary>
    public CommandResult<NoteId> Create(NewNotePlacement placement, NoteId? selected)
    {
        if (Folder is not { } folder)
        {
            return CommandResult.Failed<NoteId>(CommandFailure.InvalidState("No folder is open."));
        }

        CommandResult left = EndEdit();

        if (!left.IsSuccess)
        {
            return CommandResult.Failed<NoteId>(left.Failure!);
        }

        CommandResult<NoteId> created = _commands.Dispatch(new CreateNote(folder, string.Empty, placement, selected));

        if (created.IsSuccess)
        {
            Read();
            _ = BeginEdit(created.Value);
        }

        return created;
    }

    /// <summary>
    /// Moves <paramref name="note"/> to the recycle bin (B7). If it was being edited, its unsaved text goes with it,
    /// unsaved: the read that follows no longer finds it (<see cref="Read"/>).
    /// </summary>
    public CommandResult Delete(NoteId note)
    {
        CommandResult result = _commands.Dispatch(new DeleteNote(note));

        if (result.IsSuccess)
        {
            Read();
        }

        return result;
    }

    /// <summary>The card <paramref name="step"/> places from <paramref name="note"/>; <see langword="null"/> past either end.</summary>
    public NoteId? Neighbour(NoteId note, int step)
    {
        int index = IndexOf(note);
        int target = index + step;

        return index >= 0 && target >= 0 && target < Notes.Count ? Notes[target].Id : null;
    }

    /// <summary>Where focus goes when <paramref name="note"/> leaves the stack: the next card, else the previous.</summary>
    public NoteId? AfterRemoval(NoteId note) => Neighbour(note, 1) ?? Neighbour(note, -1);

    private int IndexOf(NoteId note)
    {
        for (int i = 0; i < Notes.Count; i++)
        {
            if (Notes[i].Id == note)
            {
                return i;
            }
        }

        return -1;
    }

    private void StopEditing()
    {
        Editing = null;
        Draft = string.Empty;
        Baseline = string.Empty;
    }
}
