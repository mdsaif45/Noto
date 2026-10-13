using Noto.Core.Commands;

namespace Noto.UseCases.Commands;

/// <summary>
/// A command: a record describing one intended mutation, and the result type its handler returns (ADR-010, #21).
/// </summary>
/// <typeparam name="TResult">
/// <see cref="CommandResult"/> or <see cref="CommandResult{T}"/>. Declared on the command so that
/// <see cref="CommandDispatcher.Dispatch{TResult}"/> returns the handler's own result type, checked by the compiler.
/// </typeparam>
/// <remarks>A marker only. Queries are not commands and never implement it: reading is a direct call (ADR-010).</remarks>
public interface ICommand<TResult>
    where TResult : ICommandResult
{
}
