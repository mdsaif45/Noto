using Noto.Core.Identifiers;

namespace Noto.Core.Tags;

/// <summary>
/// Identifies a tag (ADR-012).
/// </summary>
/// <remarks>
/// Distinct from <see cref="Notes.NoteId"/> and <see cref="Folders.FolderId"/>
/// so the three cannot be interchanged (core-note-engine-design.md §5).
/// </remarks>
public readonly record struct TagId
{
    private TagId(string value) => Value = value;

    /// <summary>The canonical 26-character ULID.</summary>
    public string Value { get; }

    /// <summary>Creates an id for a new tag.</summary>
    public static TagId New() => new(Ulid.NewId());

    /// <summary>Wraps an existing id.</summary>
    /// <exception cref="ArgumentException">The value is not a valid ULID.</exception>
    public static TagId From(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (!Ulid.IsValid(value))
        {
            throw new ArgumentException($"'{value}' is not a valid ULID.", nameof(value));
        }

        return new TagId(value);
    }

    /// <summary>Whether a string could be parsed by <see cref="From"/>.</summary>
    public static bool IsValid(string? value) => Ulid.IsValid(value);

    public override string ToString() => Value;
}
