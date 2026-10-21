using Noto.Core.Commands;
using Noto.Core.Folders;
using Noto.UseCases.Commands;

namespace Noto.UseCases.Folders;

/// <summary>
/// Permanently deletes everything in the recycle bin that can go (deletion-semantics §13), in one transaction.
/// </summary>
public sealed record EmptyRecycleBin : ICommand<CommandResult<RecycleBinPurge>>;

/// <summary>The recycle bin has been emptied. Raised only after the transaction commits.</summary>
/// <param name="NotesPurged">Deleted notes removed.</param>
/// <param name="FoldersPurged">Deleted folders removed.</param>
/// <param name="FoldersKept">Deleted folders kept because an active note still lives in them (I7).</param>
public sealed record RecycleBinEmptied(int NotesPurged, int FoldersPurged, int FoldersKept) : IEvent;

/// <summary>Handles <see cref="EmptyRecycleBin"/>.</summary>
/// <remarks>
/// Every deleted note goes, then every deleted folder no active note lives in. A folder an active note still lives in
/// is kept and reported in the result (I7). An empty bin empties successfully. Nothing is deleted automatically
/// anywhere: this command, <see cref="PurgeFolder"/> and <c>PurgeNote</c> are the only ways anything leaves the bin.
/// </remarks>
public sealed class EmptyRecycleBinHandler(IFolderRepository folders)
{
    private readonly IFolderRepository _folders = folders ?? throw new ArgumentNullException(nameof(folders));

    public CommandResult<RecycleBinPurge> Handle(EmptyRecycleBin command)
    {
        ArgumentNullException.ThrowIfNull(command);

        return CommandResult.Success(_folders.EmptyRecycleBin());
    }
}
