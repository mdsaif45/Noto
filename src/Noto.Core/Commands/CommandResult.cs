namespace Noto.Core.Commands;

/// <summary>
/// The outcome of an operation that produces a value: the value, or a
/// <see cref="CommandFailure"/>.
/// </summary>
/// <typeparam name="T">What success produces — a new id, an entity.</typeparam>
/// <remarks>
/// <para>
/// A returned result rather than a thrown exception, because these outcomes are
/// <b>expected</b>: a folder deleted in another window is not exceptional, and
/// a caller that must handle it should not be able to forget (contract §6).
/// </para>
/// <para>
/// Generic so that <c>CreateNote</c> can hand back the created
/// <see cref="Notes.NoteId"/> through the same channel as its failures, instead
/// of an <c>out</c> parameter that success and failure would both have to
/// straddle.
/// </para>
/// </remarks>
public readonly struct CommandResult<T>
{
    private readonly T? _value;

    private CommandResult(T value)
    {
        _value = value;
        Failure = null;
    }

    private CommandResult(CommandFailure failure)
    {
        _value = default;
        Failure = failure;
    }

    /// <summary>Whether the operation succeeded.</summary>
    public bool IsSuccess => Failure is null;

    /// <summary>Why it failed, or <see langword="null"/> when it succeeded.</summary>
    public CommandFailure? Failure { get; }

    /// <summary>
    /// The produced value.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The operation failed. Reading the value of a failed result is a
    /// programming error — check <see cref="IsSuccess"/> first.
    /// </exception>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException(
            $"The operation failed ({Failure!.Reason}); it has no value.");

    /// <summary>
    /// Reads the outcome without risking <see cref="Value"/> throwing.
    /// </summary>
    public bool TryGetValue(out T value, out CommandFailure? failure)
    {
        value = _value!;
        failure = Failure;
        return IsSuccess;
    }

    // Constructed through the non-generic CommandResult factories rather than
    // static members here: CA1000 forbids those on a generic type, because
    // CommandResult<Note>.Success and CommandResult<NoteId>.Success would read
    // as two different methods while being the same operation.
    internal static CommandResult<T> FromValue(T value) => new(value);

    internal static CommandResult<T> FromFailure(CommandFailure failure) => new(failure);
}

/// <summary>
/// The outcome of an operation that produces no value.
/// </summary>
/// <remarks>
/// Most commands change state and return nothing; this is their result type.
/// Kept as its own struct rather than <c>CommandResult&lt;Unit&gt;</c>, which
/// would introduce a placeholder type that exists only to be ignored.
/// </remarks>
public readonly struct CommandResult
{
    private CommandResult(CommandFailure? failure) => Failure = failure;

    public bool IsSuccess => Failure is null;

    public CommandFailure? Failure { get; }

    public static CommandResult Success() => new(null);

    public static CommandResult Failed(CommandFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new CommandResult(failure);
    }

    /// <summary>Succeeds with a value.</summary>
    public static CommandResult<T> Success<T>(T value) => CommandResult<T>.FromValue(value);

    /// <summary>Fails an operation that would otherwise produce a value.</summary>
    public static CommandResult<T> Failed<T>(CommandFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return CommandResult<T>.FromFailure(failure);
    }
}
