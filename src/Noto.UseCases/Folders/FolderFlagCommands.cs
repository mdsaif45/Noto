using Noto.Core;
using Noto.Core.Commands;
using Noto.Core.Folders;

namespace Noto.UseCases.Folders;

/// <summary>Pins a folder to the top of the list (design §6, parity C8).</summary>
public sealed record PinFolder(FolderId FolderId);

/// <summary>Unpins a folder (design §6, parity C8).</summary>
public sealed record UnpinFolder(FolderId FolderId);

/// <summary>Handles <see cref="PinFolder"/>.</summary>
/// <remarks>
/// <para>
/// Sets <c>IsPinned</c> and stamps <c>UpdatedAt</c>. <b><c>SortOrder</c> is not
/// touched</b>: pinning is a display partition (O2) laid over the sequence, not
/// a rewrite of it, which is what lets unpinning return the folder to exactly
/// where it sits. A pin that moved the folder would destroy that position.
/// </para>
/// <para>
/// Pinning an already-pinned folder is a <b>true no-op</b>: success, no write,
/// no stamp. Contract §4 states this as a general consequence of "each entity
/// row it changes" rather than as a per-command exception — a command that
/// changes no row stamps nothing, which is why the same rule covers
/// same-position reorder (§5) and both tag no-ops (§7). It is not cosmetic:
/// <c>UpdatedAt</c> is observable state feeding parity C10's "sort by
/// modified", so a redundant pin that stamped would move the folder in a view
/// the user never touched.
/// </para>
/// </remarks>
public sealed class PinFolderHandler(IFolderRepository folders, IClock clock)
{
    private readonly IFolderRepository _folders = folders
        ?? throw new ArgumentNullException(nameof(folders));

    private readonly IClock _clock = clock
        ?? throw new ArgumentNullException(nameof(clock));

    public CommandResult Handle(PinFolder command)
    {
        ArgumentNullException.ThrowIfNull(command);

        // I5 first: a deleted folder is InvalidState whatever its pinned state,
        // because the precondition is checked before idempotency (§7).
        if (!FolderMutationGuard.TryLoadActive(
            _folders, command.FolderId, "pinned", out Folder folder, out var failure))
        {
            return failure;
        }

        if (folder.IsPinned)
        {
            // Already in the requested state: success, but no write and no
            // stamp (contract §4). Pinning something twice is not an error.
            return CommandResult.Success();
        }

        // IsPinned only. SortOrder is untouched, so unpinning later returns the
        // folder to exactly where it sits in the sequence.
        _folders.SetPinned(command.FolderId, isPinned: true, _clock.UtcNow);

        return CommandResult.Success();
    }
}

/// <summary>Handles <see cref="UnpinFolder"/>.</summary>
/// <remarks>
/// The folder's <c>SortOrder</c> was never disturbed by pinning, so there is
/// nothing to restore — it simply stops being lifted by O2. Unpinning an
/// already-unpinned folder is a true no-op, for the same §4 reason as
/// <see cref="PinFolderHandler"/>.
/// </remarks>
public sealed class UnpinFolderHandler(IFolderRepository folders, IClock clock)
{
    private readonly IFolderRepository _folders = folders
        ?? throw new ArgumentNullException(nameof(folders));

    private readonly IClock _clock = clock
        ?? throw new ArgumentNullException(nameof(clock));

    public CommandResult Handle(UnpinFolder command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!FolderMutationGuard.TryLoadActive(
            _folders, command.FolderId, "unpinned", out Folder folder, out var failure))
        {
            return failure;
        }

        if (!folder.IsPinned)
        {
            return CommandResult.Success();
        }

        _folders.SetPinned(command.FolderId, isPinned: false, _clock.UtcNow);

        return CommandResult.Success();
    }
}
