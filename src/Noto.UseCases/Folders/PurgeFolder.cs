using Noto.Core;
using Noto.Core.Commands;
using Noto.Core.Folders;
using Noto.UseCases.Commands;

namespace Noto.UseCases.Folders;

/// <summary>
/// Permanently deletes a folder that is in the recycle bin, with its deleted notes (deletion-semantics §13, case F).
/// </summary>
public sealed record PurgeFolder(FolderId FolderId) : ICommand<CommandResult>;

/// <summary>A folder and its deleted notes have been permanently deleted. Raised only after they are gone.</summary>
public sealed record FolderPurged(FolderId FolderId) : IEvent;

/// <summary>Handles <see cref="PurgeFolder"/>.</summary>
/// <remarks>
/// Refused while an active note still lives in the folder (I7): it would be left in no folder, where no surface shows
/// it. The user restores the folder, or moves the note, first.
/// </remarks>
public sealed class PurgeFolderHandler(IFolderRepository folders)
{
    private readonly IFolderRepository _folders = folders ?? throw new ArgumentNullException(nameof(folders));

    public CommandResult Handle(PurgeFolder command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (RecycleBinRules.RefusePurge(command.FolderId, _folders.GetLifecycle(command.FolderId)) is { } refused)
        {
            return CommandResult.Failed(refused);
        }

        return _folders.Purge(command.FolderId) switch
        {
            FolderPurge.Purged => CommandResult.Success(),
            FolderPurge.ActiveNotesRemain => CommandResult.Failed(RecycleBinRules.ActiveNotesRemain(command.FolderId)),
            _ => CommandResult.Failed(CommandFailure.NotFound($"No folder '{command.FolderId}' in the recycle bin.")),
        };
    }
}
