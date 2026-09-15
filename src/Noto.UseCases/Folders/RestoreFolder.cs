using Noto.Core;
using Noto.Core.Commands;
using Noto.Core.Folders;

namespace Noto.UseCases.Folders;

/// <summary>
/// Brings a folder back out of the recycle bin (design §6, Case D).
/// </summary>
public sealed record RestoreFolder(FolderId FolderId);

/// <summary>
/// Handles <see cref="RestoreFolder"/> — Case D.
/// </summary>
/// <remarks>
/// <para>
/// Restores the folder <b>and every still-deleted note whose <c>FolderId</c>
/// points at it</b>, in one transaction under one timestamp.
/// </para>
/// <para>
/// <b>This includes a note the user deleted individually before the folder was
/// deleted.</b> That is the contract's Case D, and it is worth stating plainly
/// because the opposite reading is the intuitive one: restoring only what this
/// folder's deletion had cascaded would require recording <i>why</i> each note
/// was deleted — a <c>DeletedWithFolderId</c> column, which
/// deletion-semantics §5 forbids outright. <c>DeletedAt</c> says whether,
/// <c>FolderId</c> says where; nothing records why.
/// </para>
/// <para>
/// The user-visible consequence is that the folder comes back whole. A note
/// they want left in the bin can be deleted again afterwards, which is
/// recoverable; the alternative failure — a note stranded in the bin pointing
/// at a live folder, reachable only through the bin — is the worse one.
/// </para>
/// <para>
/// Case G: the restored name may collide with an existing folder. That is
/// allowed, because folder names are not unique.
/// </para>
/// </remarks>
public sealed class RestoreFolderHandler(IFolderRepository folders, IClock clock)
{
    private readonly IFolderRepository _folders = folders
        ?? throw new ArgumentNullException(nameof(folders));

    private readonly IClock _clock = clock
        ?? throw new ArgumentNullException(nameof(clock));

    public CommandResult Handle(RestoreFolder command)
    {
        ArgumentNullException.ThrowIfNull(command);

        switch (_folders.GetLifecycle(command.FolderId))
        {
            case FolderLifecycle.Missing:
                return CommandResult.Failed(
                    CommandFailure.NotFound($"No folder '{command.FolderId}'."));

            case FolderLifecycle.Active:
                // Contract §6: InvalidState covers "RestoreNote/RestoreFolder
                // on a non-deleted one". Restoring something that was never in
                // the bin is a caller mistake, not a silent success — and here
                // it would also drag every deleted note of a live folder back
                // out, which the user never asked for.
                return CommandResult.Failed(
                    CommandFailure.InvalidState($"Folder '{command.FolderId}' is not deleted."));

            case FolderLifecycle.Deleted:
            default:
                break;
        }

        // Read once; the folder and every restored note carry this instant.
        DateTimeOffset now = _clock.UtcNow;

        _folders.RestoreWithNotes(command.FolderId, now);

        return CommandResult.Success();
    }
}
