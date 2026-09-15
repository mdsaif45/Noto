using Noto.Core.Commands;
using Noto.Core.Folders;

namespace Noto.UseCases.Folders;

/// <summary>
/// The lifecycle precondition shared by every folder mutation.
/// </summary>
/// <remarks>
/// <para>
/// Written once rather than five times. Rename, pin, unpin, delete and reorder
/// all open the same way — the folder must exist and must be active — and
/// repeating that is how five handlers slowly stop agreeing with each other.
/// </para>
/// <para>
/// Invariant I5: a deleted folder is inert, so mutating one is
/// <see cref="CommandFailureReason.InvalidState"/> and not
/// <see cref="CommandFailureReason.NotFound"/>. The two are different
/// instructions to the caller — "it never existed" versus "check the bin" —
/// and collapsing them makes the second unrecoverable.
/// </para>
/// </remarks>
internal static class FolderMutationGuard
{
    /// <summary>
    /// Checks that a folder may legally be mutated.
    /// </summary>
    /// <param name="verb">
    /// How the folder could not be treated, for the failure message — "renamed",
    /// "pinned". Never contains user content (principle 10).
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the folder is active; otherwise
    /// <see langword="false"/>, with <paramref name="failure"/> set to the
    /// failure the contract requires.
    /// </returns>
    public static bool TryEnsureActive(
        IFolderRepository folders,
        FolderId id,
        string verb,
        out CommandResult failure)
    {
        switch (folders.GetLifecycle(id))
        {
            case FolderLifecycle.Active:
                failure = default;
                return true;

            case FolderLifecycle.Deleted:
                failure = CommandResult.Failed(
                    CommandFailure.InvalidState($"Folder '{id}' is deleted and cannot be {verb}."));
                return false;

            case FolderLifecycle.Missing:
            default:
                failure = CommandResult.Failed(CommandFailure.NotFound($"No folder '{id}'."));
                return false;
        }
    }

    /// <summary>
    /// Loads a folder that may legally be mutated.
    /// </summary>
    /// <returns>
    /// The active folder, or the failure the contract requires: `NotFound` when
    /// no such folder exists, `InvalidState` when it is in the recycle bin and
    /// therefore inert (I5).
    /// </returns>
    /// <remarks>
    /// The same shape as <c>NoteMutationGuard.TryLoadActive</c>. Used by the
    /// commands that must compare against the folder's <i>current</i> state to
    /// decide whether anything changes at all — §4's no-op rule — rather than
    /// only whether the folder may be touched.
    /// </remarks>
    public static bool TryLoadActive(
        IFolderRepository folders,
        FolderId id,
        string verb,
        out Folder folder,
        out CommandResult failure)
    {
        Folder? found = folders.FindActive(id);

        if (found is not null)
        {
            folder = found;
            failure = default;
            return true;
        }

        folder = null!;

        // FindActive applies I1, so it cannot distinguish "missing" from
        // "deleted" — and the contract maps those to different failures.
        failure = folders.GetLifecycle(id) == FolderLifecycle.Deleted
            ? CommandResult.Failed(
                CommandFailure.InvalidState($"Folder '{id}' is deleted and cannot be {verb}."))
            : CommandResult.Failed(CommandFailure.NotFound($"No folder '{id}'."));

        return false;
    }
}
