using Noto.Core;
using Noto.Core.Commands;
using Noto.Core.Tags;

namespace Noto.UseCases.Tags;

/// <summary>
/// Creates a tag (design §6, contract §11 row 19).
/// </summary>
public sealed record CreateTag(string Name);

/// <summary>
/// Handles <see cref="CreateTag"/>.
/// </summary>
/// <remarks>
/// <b>Sets <c>CreatedAt</c> and nothing else.</b> There is no <c>UpdatedAt</c>
/// on a tag to stamp (contract §4, §11 row 19).
/// </remarks>
public sealed class CreateTagHandler(ITagRepository tags, IClock clock)
{
    private readonly ITagRepository _tags = tags
        ?? throw new ArgumentNullException(nameof(tags));

    private readonly IClock _clock = clock
        ?? throw new ArgumentNullException(nameof(clock));

    /// <summary>
    /// Creates the tag.
    /// </summary>
    /// <returns>The new <see cref="TagId"/>, or why it could not be created.</returns>
    public CommandResult<TagId> Handle(CreateTag command)
    {
        ArgumentNullException.ThrowIfNull(command);

        // §7a, step one: normalise BEFORE anything else looks at the name.
        // Doing it later would validate and compare a value different from the
        // one persisted, which is how " Work" slips past a check on "Work".
        string? name = TagName.Normalise(command.Name);

        if (name is null)
        {
            return CommandResult.Failed<TagId>(
                CommandFailure.InvalidInput("A tag name must not be empty."));
        }

        // The uniqueness comparison uses the NORMALISED name, so " Work"
        // collides with an existing "Work". Case-insensitivity comes from the
        // column's NOCASE collation; the trim is this layer's job, because no
        // collation removes whitespace.
        if (_tags.FindIdByName(name) is not null)
        {
            return CommandResult.Failed<TagId>(
                CommandFailure.DuplicateName($"A tag named '{name}' already exists."));
        }

        var tag = new Tag
        {
            Id = TagId.New(),
            Name = name,
            CreatedAt = _clock.UtcNow,
        };

        _tags.Add(tag);

        return CommandResult.Success(tag.Id);
    }
}
