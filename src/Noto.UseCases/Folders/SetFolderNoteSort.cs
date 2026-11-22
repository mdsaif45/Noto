using Noto.Core;
using Noto.Core.Commands;
using Noto.Core.Folders;
using Noto.UseCases.Commands;

namespace Noto.UseCases.Folders;

/// <summary>
/// Sets how a folder orders its notes: Manual, Name, Date created or Date modified (#242; parity C10). No note changes.
/// </summary>
public sealed record SetFolderNoteSort(FolderId FolderId, NoteSort Sort) : ICommand<CommandResult>;

/// <summary>
/// Handles <see cref="SetFolderNoteSort"/>.
/// </summary>
/// <remarks>
/// The folder's <c>NoteSort</c> only. No note's <c>SortOrder</c> is written, in any sort: the user's own order is kept
/// underneath, which is what lets Manual restore it.
/// </remarks>
public sealed class SetFolderNoteSortHandler(IFolderRepository folders, IClock clock)
{
    private readonly IFolderRepository _folders = folders ?? throw new ArgumentNullException(nameof(folders));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    public CommandResult Handle(SetFolderNoteSort command)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Validated before anything is written: the column holds a member's name, and a value outside the enum would
        // be written as a number no reader knows.
        if (!Enum.IsDefined(command.Sort))
        {
            return CommandResult.Failed(
                CommandFailure.InvalidInput($"Not a note sort. Expected one of {string.Join(", ", Enum.GetNames<NoteSort>())}."));
        }

        if (!FolderMutationGuard.TryLoadActive(_folders, command.FolderId, "sorted", out Folder folder, out var failure))
        {
            return failure;
        }

        if (folder.NoteSort == command.Sort)
        {
            // Already sorted that way: no write, no stamp (contract §4).
            return CommandResult.Success();
        }

        _folders.SetNoteSort(command.FolderId, command.Sort, _clock.UtcNow);

        return CommandResult.Success();
    }
}
