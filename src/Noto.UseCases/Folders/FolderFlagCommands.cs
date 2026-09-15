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
/// Unlike the note flag commands, this does not short-circuit when the folder
/// is already pinned. Doing so would need a read method on the repository whose
/// only purpose is that check, and the approved persistence surface does not
/// have one. The observable contract is unaffected — pinning twice is a success
/// either way (§7) — but it does mean a redundant pin restamps
/// <c>UpdatedAt</c>. That is a deliberate, recorded difference, not an
/// oversight.
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

        if (!FolderMutationGuard.TryEnsureActive(_folders, command.FolderId, "pinned", out var failure))
        {
            return failure;
        }

        _folders.SetPinned(command.FolderId, isPinned: true, _clock.UtcNow);

        return CommandResult.Success();
    }
}

/// <summary>Handles <see cref="UnpinFolder"/>.</summary>
/// <remarks>
/// The folder's <c>SortOrder</c> was never disturbed by pinning, so there is
/// nothing to restore — it simply stops being lifted by O2.
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

        if (!FolderMutationGuard.TryEnsureActive(_folders, command.FolderId, "unpinned", out var failure))
        {
            return failure;
        }

        _folders.SetPinned(command.FolderId, isPinned: false, _clock.UtcNow);

        return CommandResult.Success();
    }
}
