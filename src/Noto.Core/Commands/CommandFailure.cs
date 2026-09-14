namespace Noto.Core.Commands;

/// <summary>
/// Why an operation did not succeed — an expected business outcome, not a fault
/// (core-note-engine-contract.md §6).
/// </summary>
/// <remarks>
/// <para>
/// Three kinds of failure are kept apart, because collapsing them makes every
/// caller guess:
/// </para>
/// <code>
///   business / application   -> returned as CommandResult
///   infrastructure / storage -> thrown as StorageException
///   programming / invariant  -> thrown as an ordinary argument exception
/// </code>
/// <para>
/// A caller distinguishes the first from the second by return value versus
/// exception. "The disk is full" and "that folder was already deleted" are not
/// the same kind of thing and must not share a channel.
/// </para>
/// <para>
/// A single type with a <see cref="Reason"/> rather than a hierarchy, matching
/// <see cref="Storage.StorageException"/>: callers branch on a handful of
/// outcomes, and a class per failure mode would be machinery without a second
/// implementation (principle 9).
/// </para>
/// </remarks>
public sealed record CommandFailure
{
    private CommandFailure(CommandFailureReason reason, string message)
    {
        Reason = reason;
        Message = message;
    }

    public CommandFailureReason Reason { get; }

    /// <summary>
    /// A short explanation for diagnostics.
    /// </summary>
    /// <remarks>
    /// <b>Never contains note content</b> (principle 10). Ids and field names
    /// are safe; what the user wrote is not.
    /// </remarks>
    public string Message { get; }

    /// <summary>The entity does not exist.</summary>
    public static CommandFailure NotFound(string message) =>
        new(CommandFailureReason.NotFound, message);

    /// <summary>
    /// A name collides with an existing one where uniqueness is a business
    /// constraint.
    /// </summary>
    /// <remarks>
    /// Reserved for entity <i>names</i>. It is deliberately <b>not</b> used for
    /// an existing note-tag relationship: assigning a tag a note already has is
    /// the requested outcome, not a collision (contract §7).
    /// </remarks>
    public static CommandFailure DuplicateName(string message) =>
        new(CommandFailureReason.DuplicateName, message);

    /// <summary>The input is not acceptable — an empty name, an unknown colour key.</summary>
    public static CommandFailure InvalidInput(string message) =>
        new(CommandFailureReason.InvalidInput, message);

    /// <summary>
    /// The entity exists but is in the wrong state for this operation.
    /// </summary>
    /// <remarks>
    /// Covers both directions of the lifecycle precondition: mutating a deleted
    /// entity (invariant I5) and restoring one that is not deleted. They share
    /// one caller behaviour — refresh, the bin changed — so they share one
    /// reason.
    /// </remarks>
    public static CommandFailure InvalidState(string message) =>
        new(CommandFailureReason.InvalidState, message);

    public override string ToString() => $"{Reason}: {Message}";
}

/// <summary>
/// The business failure reasons. Exactly four, each required by an accepted
/// document (contract §6).
/// </summary>
public enum CommandFailureReason
{
    /// <summary>The entity does not exist.</summary>
    NotFound = 0,

    /// <summary>A name that must be unique already exists.</summary>
    DuplicateName,

    /// <summary>The input failed validation.</summary>
    InvalidInput,

    /// <summary>The entity is in the wrong state for this operation.</summary>
    InvalidState,
}
