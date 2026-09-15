namespace Noto.Core.Folders;

/// <summary>
/// Persistence for folders.
/// </summary>
/// <remarks>
/// <para>
/// Its own repository rather than a method on <see cref="Notes.INoteRepository"/>:
/// design §11 calls for three repositories because there are three aggregates,
/// and contract §11 Q7 keeps notes and folders separate in the bin for the same
/// reason — they are different entities and a union return would serve neither.
/// </para>
/// <para>
/// <b>Slice 3 introduces it read-only.</b> Only the recycle-bin query exists,
/// because that is the only folder behaviour this slice implements. The folder
/// commands arrive in Slice 4; a method written before its caller is a guess.
/// </para>
/// </remarks>
public interface IFolderRepository
{
    /// <summary>
    /// Every folder in the recycle bin.
    /// </summary>
    /// <remarks>
    /// The I2 explicit-bin method for folders. As with notes, there is no
    /// <c>includeDeleted</c> flag anywhere on the ordinary queries.
    /// </remarks>
    IReadOnlyList<Folder> ListDeleted();
}
