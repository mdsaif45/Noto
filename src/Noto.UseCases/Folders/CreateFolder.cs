using Noto.Core;
using Noto.Core.Commands;
using Noto.Core.Folders;

namespace Noto.UseCases.Folders;

/// <summary>
/// Creates a folder (design §6, parity C2).
/// </summary>
/// <param name="Name">The folder name. Must not be empty or whitespace.</param>
public sealed record CreateFolder(string Name);

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

        // Design §14: an empty folder name is rejected. Whitespace counts as
        // empty — a folder named " " is indistinguishable from an unnamed one
        // in the list, so accepting it would create a row the user cannot
        // identify.
        if (string.IsNullOrWhiteSpace(command.Name))
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
            Name = command.Name,
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
