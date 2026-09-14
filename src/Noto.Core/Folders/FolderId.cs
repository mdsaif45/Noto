using Noto.Core.Identifiers;

namespace Noto.Core.Folders;

/// <summary>
/// Identifies a folder (ADR-012).
/// </summary>
/// <remarks>
/// Distinct from <see cref="Notes.NoteId"/> and <see cref="Tags.TagId"/> so the
/// three cannot be interchanged (core-note-engine-design.md §5).
/// </remarks>
public readonly record struct FolderId
{
    private FolderId(string value) => Value = value;

    /// <summary>The canonical 26-character ULID.</summary>
    public string Value { get; }

    /// <summary>Creates an id for a new folder.</summary>
    public static FolderId New() => new(Ulid.NewId());

    /// <summary>Wraps an existing id.</summary>
    /// <exception cref="ArgumentException">The value is not a valid ULID.</exception>
    public static FolderId From(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (!Ulid.IsValid(value))
        {
            throw new ArgumentException($"'{value}' is not a valid ULID.", nameof(value));
        }

        return new FolderId(value);
    }

    /// <summary>Whether a string could be parsed by <see cref="From"/>.</summary>
    public static bool IsValid(string? value) => Ulid.IsValid(value);

    public override string ToString() => Value;
}
