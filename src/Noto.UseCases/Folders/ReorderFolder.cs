using Noto.Core;
using Noto.Core.Commands;
using Noto.Core.Folders;

namespace Noto.UseCases.Folders;

/// <summary>
/// Repositions a folder within the folder collection (design §6, parity C9).
/// </summary>
/// <param name="AfterFolderId">
/// The active folder to sit immediately after, or <see langword="null"/> to
/// place this one first.
/// </param>
/// <remarks>
/// The signature the contract specifies: <c>ReorderFolder(folderId,
/// afterFolderId?)</c>. No scope argument, because the folder collection is a
/// single scope; no <c>SortOrder</c> argument either — the engine computes it
/// (contract §5), which keeps the O6 renormalisation trigger out of the caller.
/// </remarks>
public sealed record ReorderFolder(FolderId FolderId, FolderId? AfterFolderId);

/// <summary>
/// Handles <see cref="ReorderFolder"/>.
/// </summary>
/// <remarks>
/// <para>
/// Atomic with any renormalisation it triggers (design §9). The ordering rules
/// themselves are <see cref="Noto.Core.Folders.FolderPlacement"/> translated by
/// the repository into the shared ordering engine — folders do not carry a
/// second copy of the midpoint algorithm.
/// </para>
/// <para>
/// Pinning is not consulted. O2 partitions the <i>display</i> by
/// <c>IsPinned</c>; the underlying sequence is one scope, so reordering a
/// pinned folder works exactly as it does for an unpinned one.
/// </para>
/// </remarks>
public sealed class ReorderFolderHandler(IFolderRepository folders, IClock clock)
{
    private readonly IFolderRepository _folders = folders
        ?? throw new ArgumentNullException(nameof(folders));

    private readonly IClock _clock = clock
        ?? throw new ArgumentNullException(nameof(clock));

    public CommandResult Handle(ReorderFolder command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!FolderMutationGuard.TryEnsureActive(_folders, command.FolderId, "reordered", out var failure))
        {
            return failure;
        }

        FolderPlacement placement;

        if (command.AfterFolderId is { } target)
        {
            // The target must be ACTIVE. Deleted rows never participate in
            // ordering (I6), so anchoring to one would compute a position from
            // a row the user cannot see.
            if (!_folders.IsActiveSibling(target))
            {
                return CommandResult.Failed(
                    CommandFailure.InvalidInput($"Folder '{target}' is not an active folder."));
            }

            placement = FolderPlacement.After(target);
        }
        else
        {
            placement = FolderPlacement.First;
        }

        // The repository reports whether anything actually moved. A
        // same-position reorder writes nothing and stamps nothing (contract
        // §5), so it is a success with no side effect rather than a failure.
        _folders.Reorder(command.FolderId, placement, _clock.UtcNow);

        return CommandResult.Success();
    }
}
