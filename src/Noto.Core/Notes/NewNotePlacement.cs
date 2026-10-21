namespace Noto.Core.Notes;

/// <summary>Where a new note goes in its folder (parity B5, J8; create-and-rename §2.3).</summary>
public enum NewNotePlacement
{
    /// <summary>At the end of the folder: Noto's behaviour before the setting existed, and the default.</summary>
    Bottom = 0,

    /// <summary>At the top of the folder's unpinned notes.</summary>
    Top,

    /// <summary>Just above the selected note.</summary>
    AboveSelected,

    /// <summary>Just below the selected note.</summary>
    BelowSelected,
}

/// <summary>Turns the <see cref="NewNotePlacement"/> setting into a position among a folder's notes.</summary>
/// <remarks>
/// A new note is never pinned, and pinned notes sort first (O2), so every placement is decided within the folder's
/// unpinned notes: "top" is the top of those, and a pinned selection places the note at the top of them, the nearest
/// position to it a new note can have. Any setting it does not know, and any selection that is not one of the folder's
/// active unpinned or pinned notes, falls back to <see cref="NewNotePlacement.Bottom"/>.
/// </remarks>
public static class NewNotePlacementRule
{
    /// <param name="setting">The user's setting.</param>
    /// <param name="selected">The selected note, if any.</param>
    /// <param name="siblings">The folder's active notes in display order (O2: pinned first).</param>
    public static NotePlacement Resolve(NewNotePlacement setting, NoteId? selected, IReadOnlyList<Note> siblings)
    {
        ArgumentNullException.ThrowIfNull(siblings);

        switch (setting)
        {
            case NewNotePlacement.Top:
                return NotePlacement.First;

            case NewNotePlacement.AboveSelected:
            case NewNotePlacement.BelowSelected:
                break;

            case NewNotePlacement.Bottom:
            default:
                return NotePlacement.Last;
        }

        int index = selected is { } id ? IndexOf(siblings, id) : -1;
        if (index < 0)
        {
            return NotePlacement.Last;
        }

        if (siblings[index].IsPinned)
        {
            return NotePlacement.First;
        }

        if (setting == NewNotePlacement.BelowSelected)
        {
            return NotePlacement.After(siblings[index].Id);
        }

        // Above: after the unpinned note before it, or first when it is the first unpinned note.
        for (int i = index - 1; i >= 0; i--)
        {
            if (!siblings[i].IsPinned)
            {
                return NotePlacement.After(siblings[i].Id);
            }
        }

        return NotePlacement.First;
    }

    private static int IndexOf(IReadOnlyList<Note> siblings, NoteId id)
    {
        for (int i = 0; i < siblings.Count; i++)
        {
            if (siblings[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }
}
