using Noto.Core.Commands;
using Noto.Core.Tags;

namespace Noto.UseCases.Tags;

/// <summary>
/// Deletes a tag (design §6, contract §11 row 21).
/// </summary>
public sealed record DeleteTag(TagId TagId);

/// <summary>
/// Handles <see cref="DeleteTag"/> — the engine's <b>only hard delete</b>.
/// </summary>
/// <remarks>
/// <para>
/// The row is removed, not flagged. Every other delete in Noto is soft, because
/// losing a note or a folder loses content the user cannot recover (ADR-002);
/// design §8 draws the line here — *"a tag is a label, not content. Losing one
/// loses nothing recoverable."*
/// </para>
/// <para>
/// There is consequently <b>no <c>RestoreTag</c></b>, no tag recycle bin, and
/// no `InvalidState` for this command: a tag exists or it does not, so the only
/// failure is <see cref="CommandFailureReason.NotFound"/> (§11 row 21).
/// </para>
/// <para>
/// The relationships go with it and <b>the notes do not</b>. That cascade is
/// atomic (§10).
/// </para>
/// </remarks>
public sealed class DeleteTagHandler(ITagRepository tags)
{
    private readonly ITagRepository _tags = tags
        ?? throw new ArgumentNullException(nameof(tags));

    public CommandResult Handle(DeleteTag command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (_tags.Find(command.TagId) is null)
        {
            return CommandResult.Failed(CommandFailure.NotFound($"No tag '{command.TagId}'."));
        }

        _tags.Delete(command.TagId);

        return CommandResult.Success();
    }
}
