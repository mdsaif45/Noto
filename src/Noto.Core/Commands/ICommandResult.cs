namespace Noto.Core.Commands;

/// <summary>
/// What every command result can say: whether the command succeeded (#21).
/// </summary>
/// <remarks>
/// Implemented by <see cref="CommandResult"/> and <see cref="CommandResult{T}"/>, so the dispatcher can decide whether
/// a command's event is published without knowing which of the two a command returns. Constrained generics read it
/// from the struct without boxing.
/// </remarks>
public interface ICommandResult
{
    bool IsSuccess { get; }
}
