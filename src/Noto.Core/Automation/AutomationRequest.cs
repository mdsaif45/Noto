using Noto.Core.Folders;

namespace Noto.Core.Automation;

/// <summary>
/// What a <c>noto://</c> link or a command line asks Noto to do (#192, ADR-022). Only ever to create or to show: no
/// request deletes, moves, overwrites, exports, reads a file or runs anything, because a link can come from any web
/// page.
/// </summary>
/// <remarks>
/// Made only by <see cref="NotoUri.Parse"/>, so every value here has passed its rules: text cleaned and capped, names
/// and folder references normalised as a folder name is.
/// </remarks>
public abstract record AutomationRequest
{
    private AutomationRequest()
    {
    }

    /// <summary>A note holding <paramref name="Text"/> (SideNotes' add-note-with-text).</summary>
    /// <param name="Text">Plain text, stored as typed: a link in it opens only on the user's click (ADR-019).</param>
    /// <param name="Folder">A folder's id or name; <see langword="null"/> for where Ctrl+N would put it.</param>
    public sealed record NewNote(string Text, string? Folder) : AutomationRequest;

    /// <summary>A folder's notes (SideNotes' open/&lt;uuid&gt;).</summary>
    /// <param name="Folder">The folder's id or name.</param>
    public sealed record OpenFolder(string Folder) : AutomationRequest;

    /// <summary>The folder list (C7).</summary>
    public sealed record ShowFolders : AutomationRequest;

    /// <summary>A new folder (C2).</summary>
    /// <param name="Name">The name, normalised as every folder name is.</param>
    public sealed record NewFolder(string Name) : AutomationRequest;

    /// <summary>Search, with <paramref name="Query"/> in its box (E6).</summary>
    public sealed record Search(string Query) : AutomationRequest;

    /// <summary>
    /// The folder a reference names: the one with that id, else the first in display order with that name, ignoring
    /// case. Names are not unique (Case G), so an id is the reference that always means one folder.
    /// </summary>
    /// <param name="folders">The active folders, in display order.</param>
    /// <param name="reference">An id or a name.</param>
    public static Folder? FindFolder(IReadOnlyList<Folder> folders, string reference)
    {
        ArgumentNullException.ThrowIfNull(folders);

        return folders.FirstOrDefault(f => string.Equals(f.Id.Value, reference, StringComparison.OrdinalIgnoreCase))
            ?? folders.FirstOrDefault(f => string.Equals(FolderName.Normalise(f.Name), reference, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>Why a link or a command line was refused (#192). Traced; never shown with the input itself.</summary>
public enum AutomationRefusal
{
    /// <summary>Not refused.</summary>
    None,

    /// <summary>Not a <c>noto://</c> link.</summary>
    NotNoto,

    /// <summary>Not well formed: a bad escape, a fragment, a parameter without a value, a path where none belongs.</summary>
    Malformed,

    /// <summary>A verb Noto does not have. The list is closed: nothing else is reachable.</summary>
    UnknownVerb,

    /// <summary>A parameter the verb does not take, or one given twice.</summary>
    UnknownArgument,

    /// <summary>A parameter the verb needs is missing.</summary>
    MissingArgument,

    /// <summary>Longer than the caps (<see cref="NotoUri.MaxLength"/>, <see cref="NotoUri.MaxTextLength"/>, <see cref="NotoUri.MaxNameLength"/>).</summary>
    TooLong,

    /// <summary>Not valid Unicode: an escape that is not UTF-8, or a lone surrogate.</summary>
    InvalidText,

    /// <summary>A name or folder reference with nothing left once normalised.</summary>
    EmptyName,

    /// <summary>A command line that asks for more than one thing.</summary>
    MoreThanOne,
}

/// <summary>What a link or a command line came to: a request, a refusal, or (on a command line) nothing asked.</summary>
/// <param name="Request">The request; <see langword="null"/> when refused or when nothing was asked.</param>
/// <param name="Refusal">Why it was refused; <see cref="AutomationRefusal.None"/> otherwise.</param>
public readonly record struct AutomationParse(AutomationRequest? Request, AutomationRefusal Refusal);
