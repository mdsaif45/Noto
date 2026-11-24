using Noto.Core;
using Noto.Core.Commands;
using Noto.Core.Folders;
using Noto.Core.Notes;
using Noto.UseCases.Commands;

namespace Noto.UseCases.Folders;

/// <summary>
/// Duplicates a folder with its notes (parity C12, #232): "&lt;name&gt; copy", right after it, holding a copy of each of
/// its active notes in their order.
/// </summary>
public sealed record DuplicateFolder(FolderId FolderId) : ICommand<CommandResult<FolderId>>;

/// <summary>
/// Handles <see cref="DuplicateFolder"/>.
/// </summary>
/// <remarks>
/// <para>
/// The folder and its notes are written in one transaction (<see cref="IFolderRepository.AddWithNotes"/>), so a
/// failure leaves no half-made folder. Binned notes are not copied: the copy is of what the folder shows.
/// </para>
/// <para>
/// The copy keeps the folder's colour and its note sort (#242), and is not pinned, as a duplicated note is not (B22):
/// pinned folders sort first (O2), so the copy of a pinned folder goes to the top of the unpinned ones. Each note keeps
/// its text, colour, mode, pin, fold and order. Pictures are shared, not copied (ADR-019 §4); tags are not copied, as no surface shows them.
/// </para>
/// </remarks>
public sealed class DuplicateFolderHandler(IFolderRepository folders, INoteRepository notes, IClock clock)
{
    private readonly IFolderRepository _folders = folders ?? throw new ArgumentNullException(nameof(folders));
    private readonly INoteRepository _notes = notes ?? throw new ArgumentNullException(nameof(notes));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    public CommandResult<FolderId> Handle(DuplicateFolder command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!FolderMutationGuard.TryLoadActive(_folders, command.FolderId, "duplicated", out Folder original, out var failure))
        {
            return CommandResult.Failed<FolderId>(failure.Failure!);
        }

        DateTimeOffset now = _clock.UtcNow;
        var copy = new Folder
        {
            Id = FolderId.New(),
            Name = FolderName.CopyOf(original.Name, _folders.ListActive().Select(f => f.Name)),
            ColorKey = original.ColorKey,
            NoteSort = original.NoteSort,
            CreatedAt = now,
            UpdatedAt = now,
        };

        var notes = _notes.ListInFolder(original.Id)
            .Select(n => n with { Id = NoteId.New(), FolderId = copy.Id, CreatedAt = now, UpdatedAt = now })
            .ToList();

        _folders.AddWithNotes(copy, original.IsPinned ? FolderPlacement.First : FolderPlacement.After(original.Id), notes);

        return CommandResult.Success(copy.Id);
    }
}
