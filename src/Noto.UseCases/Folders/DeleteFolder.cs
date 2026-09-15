using Noto.Core;
using Noto.Core.Commands;
using Noto.Core.Folders;

namespace Noto.UseCases.Folders;

/// <summary>
/// Moves a folder and its active notes into the recycle bin
/// (design §6, parity C11).
/// </summary>
public sealed record DeleteFolder(FolderId FolderId);

/// <summary>
/// Handles <see cref="DeleteFolder"/> — Cases A and B.
/// </summary>
/// <remarks>
/// <para>
/// A soft delete. The rows stay, which is what lets the bin list them and
/// <c>RestoreFolder</c> bring them back (C11, ADR-002 — data cannot be
/// recovered from a server, so it must not be destroyed locally).
/// </para>
/// <para>
/// The cascade is atomic and takes <b>one timestamp</b> for the folder and
/// every note it takes with it (contract §4, §10). The handler reads the clock
/// exactly once and hands that instant to the repository; the alternative — a
/// clock call per row — produces a cascade whose rows disagree about when it
/// happened, which no later query can repair.
/// </para>
/// <para>
/// Siblings are not renumbered. O5: deleting leaves gaps, and gaps are
/// harmless. The deleted rows keep their own <c>SortOrder</c>, which is what
/// makes restore return them roughly where they were (I6).
/// </para>
/// </remarks>
public sealed class DeleteFolderHandler(IFolderRepository folders, IClock clock)
{
    private readonly IFolderRepository _folders = folders
        ?? throw new ArgumentNullException(nameof(folders));

    private readonly IClock _clock = clock
        ?? throw new ArgumentNullException(nameof(clock));

    public CommandResult Handle(DeleteFolder command)
    {
        ArgumentNullException.ThrowIfNull(command);

        // I5: deleting an already-deleted folder is InvalidState, not a silent
        // success — it would otherwise restamp the bin entry and move the
        // folder to the top of the recycle list for no reason the user caused.
        if (!FolderMutationGuard.TryEnsureActive(_folders, command.FolderId, "deleted", out var failure))
        {
            return failure;
        }

        // Read once. Everything the transaction writes carries this instant.
        DateTimeOffset now = _clock.UtcNow;

        // Case A — the folder and its ACTIVE notes go to the bin together.
        // Case B — notes already in the bin are left exactly as they are,
        // keeping their original DeletedAt. Both live in the repository's
        // single transaction, because splitting them here would make a partial
        // cascade observable.
        _folders.SoftDeleteWithNotes(command.FolderId, now);

        return CommandResult.Success();
    }
}
