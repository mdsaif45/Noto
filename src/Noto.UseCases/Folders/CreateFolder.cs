using Noto.Core;
using Noto.Core.Commands;
using Noto.Core.Folders;
using Noto.UseCases.Commands;

namespace Noto.UseCases.Folders;

/// <summary>
/// Creates a folder (design §6, parity C2).
/// </summary>
/// <param name="Name">The folder name, normalised before it is stored (<see cref="FolderName"/>). Must not be empty.</param>
public sealed record CreateFolder(string Name) : ICommand<CommandResult<FolderId>>;

/// <summary>A folder was created (create-and-rename §2.4). Raised only after the row is written.</summary>
public sealed record FolderCreated(FolderId FolderId) : IEvent;

/// <summary>
/// Handles <see cref="CreateFolder"/>.
/// </summary>
/// <remarks>
/// One handler per command, mirroring the note commands: no <c>FolderService</c>
/// gathering seven methods, because that god-object is what makes ADR-010's
/// "every mutation goes through a command" unenforceable (design §6).
/// </remarks>
public sealed class CreateFolderHandler(IFolderRepository folders, IClock clock)
{
    /// <summary>
    /// Where the first folder sits.
    /// </summary>
    /// <remarks>
    /// The schema defaults <c>SortOrder</c> to 0, so starting there keeps the
    /// first folder consistent with a row written by any other path. The value
    /// carries no meaning — only relative order does (design §7).
    /// </remarks>
    private const double InitialSortOrder = 0;

    private readonly IFolderRepository _folders = folders
        ?? throw new ArgumentNullException(nameof(folders));

    private readonly IClock _clock = clock
        ?? throw new ArgumentNullException(nameof(clock));

    /// <summary>
    /// Creates the folder.
    /// </summary>
    /// <returns>
    /// The new <see cref="FolderId"/>, or why it could not be created.
    /// </returns>
    public CommandResult<FolderId> Handle(CreateFolder command)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Design §14: an empty folder name is rejected. The name is normalised
        // first (create-and-rename §2.1): trimmed, and its line breaks and tabs
        // made spaces, so whitespace alone is empty too — a folder named " "
        // is indistinguishable from an unnamed one in the list.
        if (FolderName.Normalise(command.Name) is not { } name)
        {
            return CommandResult.Failed<FolderId>(
                CommandFailure.InvalidInput("A folder name must not be empty."));
        }

        // Deliberately no duplicate-name check. Folder names are not unique
        // (Case G); DuplicateName exists in the failure model for entities that
        // do require uniqueness, and using it here would make a legal state an
        // error.

        // One timestamp for both fields: a folder that reports being modified a
        // tick after it was created is a lie the UI would surface under "sort
        // by modified" (contract §4, §11 row 12).
        DateTimeOffset now = _clock.UtcNow;

        // O3: a new folder goes at the END of the collection, which is max + 1.
        // An empty collection starts at the documented base rather than at
        // max+1 of nothing.
        double? highest = _folders.MaxSortOrder();
        double sortOrder = highest is { } max ? max + 1 : InitialSortOrder;

        var folder = new Folder
        {
            Id = FolderId.New(),
            Name = name,
            SortOrder = sortOrder,
            CreatedAt = now,
            UpdatedAt = now,

            // Remaining fields take their documented defaults: no colour, not
            // pinned, not collapsed, not deleted.
        };

        _folders.Add(folder);

        return CommandResult.Success(folder.Id);
    }
}
