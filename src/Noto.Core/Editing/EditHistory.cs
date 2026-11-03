namespace Noto.Core.Editing;

/// <summary>What kind of change an edit was: it decides whether it joins the previous undo step.</summary>
public enum EditKind
{
    /// <summary>Characters typed one at a time.</summary>
    Typing,

    /// <summary>Characters removed by Backspace or Delete.</summary>
    Deletion,

    /// <summary>Anything else: a command, a paste, a replaced selection, an IME composition. Always its own step.</summary>
    Other,
}

/// <summary>
/// Noto's own undo and redo (ADR-017 §3): RichEdit's records Noto's restyles, so it is switched off. One step is one
/// command, one paste, or one run of typing or deleting; a run of typing ends at whitespace, a jump of the caret, or a
/// change of kind.
/// </summary>
public sealed class EditHistory
{
    private readonly List<Step> _undo = [];
    private readonly List<Step> _redo = [];

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>
    /// Records <paramref name="edit"/>, just made to <paramref name="before"/> with <paramref name="selectionBefore"/>
    /// selected. A new edit ends what redo could have restored.
    /// </summary>
    public void Record(string before, TextEdit edit, EditKind kind, TextSelection selectionBefore)
    {
        ArgumentNullException.ThrowIfNull(before);
        _redo.Clear();

        var step = new Step(edit.Start, before.Substring(edit.Start, edit.Length), edit.Replacement, selectionBefore, edit.After, kind);

        if (_undo.Count > 0 && Joins(_undo[^1], step))
        {
            Step last = _undo[^1];
            _undo[^1] = kind switch
            {
                EditKind.Typing => last with { Inserted = last.Inserted + step.Inserted, SelectionAfter = step.SelectionAfter },

                // Delete removes what follows, at the same place; Backspace removes what precedes.
                _ when step.Start == last.Start => last with { Removed = last.Removed + step.Removed, SelectionAfter = step.SelectionAfter },
                _ => last with { Start = step.Start, Removed = step.Removed + last.Removed, SelectionAfter = step.SelectionAfter },
            };
            return;
        }

        _undo.Add(step);
    }

    /// <summary>The edit that reverses the last step, which then becomes redoable; <see langword="null"/> when there is none.</summary>
    public TextEdit? Undo()
    {
        if (_undo.Count == 0)
        {
            return null;
        }

        Step step = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(step);
        return new TextEdit(step.Start, step.Inserted.Length, step.Removed, step.SelectionBefore);
    }

    /// <summary>The edit that makes the last undone step again; <see langword="null"/> when there is none.</summary>
    public TextEdit? Redo()
    {
        if (_redo.Count == 0)
        {
            return null;
        }

        Step step = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(step);
        return new TextEdit(step.Start, step.Removed.Length, step.Inserted, step.SelectionAfter);
    }

    /// <summary>Forgets every step: a different note is being edited.</summary>
    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }

    /// <summary>
    /// Typing joins typing that ended where it starts, unless it starts a new word; deleting joins deleting that
    /// touches it (Backspace runs leftwards, Delete stays put).
    /// </summary>
    private static bool Joins(Step last, Step next)
    {
        if (last.Kind != next.Kind)
        {
            return false;
        }

        return next.Kind switch
        {
            EditKind.Typing => next.Removed.Length == 0
                && last.Removed.Length == 0
                && next.Start == last.Start + last.Inserted.Length
                && !(next.Inserted.Length > 0 && char.IsWhiteSpace(next.Inserted[0]) && last.Inserted.Length > 0 && !char.IsWhiteSpace(last.Inserted[^1])),
            EditKind.Deletion => next.Inserted.Length == 0
                && last.Inserted.Length == 0
                && (next.Start + next.Removed.Length == last.Start || next.Start == last.Start),
            _ => false,
        };
    }

    private sealed record Step(int Start, string Removed, string Inserted, TextSelection SelectionBefore, TextSelection SelectionAfter, EditKind Kind);
}
