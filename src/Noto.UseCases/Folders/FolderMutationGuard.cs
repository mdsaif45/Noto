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
}
