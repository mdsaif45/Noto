using Noto.Core.Commands;
using Noto.Core.Tags;

namespace Noto.UseCases.Tags;

/// <summary>
/// Renames a tag (design §6, contract §11 row 20).
/// </summary>
public sealed record RenameTag(TagId TagId, string Name);

/// <summary>
/// Handles <see cref="RenameTag"/>.
/// </summary>
/// <remarks>
/// <para>
/// No <see cref="IClock"/> dependency, unlike every other rename in the engine:
/// §11 row 20 says this command "stamps nothing", and <c>Tags</c> has no
/// <c>UpdatedAt</c>. Taking a clock it must not use would only invite someone
/// to use it.
/// </para>
/// <para>
/// A single-row update, so no transaction (design §9).
/// </para>
/// </remarks>
public sealed class RenameTagHandler(ITagRepository tags)
{
    private readonly ITagRepository _tags = tags
        ?? throw new ArgumentNullException(nameof(tags));

    public CommandResult Handle(RenameTag command)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Existence first, matching every other rename in the engine: a
        // missing entity is NotFound whatever the caller passed as a name.
        if (_tags.Find(command.TagId) is null)
        {
            return CommandResult.Failed(CommandFailure.NotFound($"No tag '{command.TagId}'."));
        }

        string? name = TagName.Normalise(command.Name);

        if (name is null)
        {
            return CommandResult.Failed(
                CommandFailure.InvalidInput("A tag name must not be empty."));
        }

        // The collision check compares the NORMALISED name, and excludes the
        // tag itself: renaming "Work" to "Work" — or to " Work ", which
        // normalises to the same thing — is a legal no-collision rename, not a
        // DuplicateName. Comparing by id rather than by string is what makes
        // that distinction exact.
        if (_tags.FindIdByName(name) is { } owner && owner != command.TagId)
        {
            return CommandResult.Failed(
                CommandFailure.DuplicateName($"A tag named '{name}' already exists."));
        }

        _tags.Rename(command.TagId, name);

        return CommandResult.Success();
    }
}
