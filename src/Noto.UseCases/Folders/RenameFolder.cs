using Noto.Core;
using Noto.Core.Commands;
using Noto.Core.Folders;

namespace Noto.UseCases.Folders;

/// <summary>
/// Renames a folder (design §6, parity C13).
/// </summary>
public sealed record RenameFolder(FolderId FolderId, string Name);

/// <summary>
/// Handles <see cref="RenameFolder"/>.
/// </summary>
/// <remarks>
/// A single-row update, so no transaction (design §9).
/// </remarks>
public sealed class RenameFolderHandler(IFolderRepository folders, IClock clock)
{
    private readonly IFolderRepository _folders = folders
        ?? throw new ArgumentNullException(nameof(folders));

    private readonly IClock _clock = clock
        ?? throw new ArgumentNullException(nameof(clock));

    public CommandResult Handle(RenameFolder command)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Lifecycle before input, matching every other mutation: contract §7
        // orders the precondition check ahead of anything else, so a deleted
        // folder reports InvalidState whatever the caller passed as a name.
        if (!FolderMutationGuard.TryEnsureActive(_folders, command.FolderId, "renamed", out var failure))
        {
            return failure;
        }

        if (string.IsNullOrWhiteSpace(command.Name))
        {
            return CommandResult.Failed(
                CommandFailure.InvalidInput("A folder name must not be empty."));
        }

        // No duplicate-name check: folder names are not unique (Case G), and a
        // rename that collides is a supported outcome rather than a failure.
        //
        // The repository writes Name and UpdatedAt only, so CreatedAt,
        // SortOrder and IsPinned are preserved by construction rather than by
        // this handler remembering to preserve them.
        _folders.Rename(command.FolderId, command.Name, _clock.UtcNow);

        return CommandResult.Success();
    }
}
