using Noto.Core.Identifiers;

namespace Noto.Core.Notes;

/// <summary>
/// Identifies a note (ADR-012).
/// </summary>
/// <remarks>
/// A wrapper over a ULID string rather than the string itself, so that
/// <c>MoveNoteToFolder(noteId, folderId)</c> cannot be called with its
/// arguments swapped — the compiler rejects it rather than the database
/// silently storing nonsense (core-note-engine-design.md §5).
/// </remarks>
public readonly record struct NoteId
{
    private NoteId(string value) => Value = value;

    /// <summary>The canonical 26-character ULID.</summary>
    public string Value { get; }

    /// <summary>Creates an id for a new note.</summary>
    public static NoteId New() => new(Ulid.NewId());

    /// <summary>
    /// Wraps an existing id — reading a row, or accepting one from a caller.
    /// </summary>
    /// <exception cref="ArgumentException">The value is not a valid ULID.</exception>
    public static NoteId From(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (!Ulid.IsValid(value))
        {
            // A malformed id is a programming error, not a business outcome:
            // no user action produces one, so it must fail loudly rather than
            // become a NotFound the caller would misread as "no such note".
            throw new ArgumentException($"'{value}' is not a valid ULID.", nameof(value));
        }

        return new NoteId(value);
    }

    /// <summary>Whether a string could be parsed by <see cref="From"/>.</summary>
    public static bool IsValid(string? value) => Ulid.IsValid(value);

    public override string ToString() => Value;
}
