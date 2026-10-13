using Noto.Core.Commands;

namespace Noto.UseCases.Commands;

/// <summary>
/// The explicit map from command type to handler that ADR-010 requires, filled in the composition root (#21).
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing is discovered.</b> Every command is registered by a line of code naming it, its handler and, if it has
/// one, its event: no reflection, no assembly scanning, no startup cost beyond a dictionary entry per command.
/// </para>
/// <para>
/// <b>An event is typed.</b> A command that announces its success names the event type and builds it from the command
/// and its successful result; the event must be an <see cref="IEvent"/>. A command cannot publish anything else, and
/// it cannot publish on failure: the registry, not each handler, decides that (see <see cref="Entry{TResult}"/>).
/// </para>
/// </remarks>
public sealed class CommandRegistry
{
    private readonly Dictionary<Type, object> _entries = [];

    /// <summary>Registers a command whose success announces nothing.</summary>
    /// <exception cref="InvalidOperationException"><typeparamref name="TCommand"/> is already registered.</exception>
    public CommandRegistry Register<TCommand, TResult>(Func<TCommand, TResult> handle)
        where TCommand : ICommand<TResult>
        where TResult : ICommandResult
    {
        ArgumentNullException.ThrowIfNull(handle);

        return Add(typeof(TCommand), new Handled<TCommand, TResult>(handle, null));
    }

    /// <summary>Registers a command whose success is announced as <typeparamref name="TEvent"/>.</summary>
    /// <param name="handle">The handler's <c>Handle</c>.</param>
    /// <param name="describe">Builds the event from the command and its successful result. Never called on failure.</param>
    /// <exception cref="InvalidOperationException"><typeparamref name="TCommand"/> is already registered.</exception>
    public CommandRegistry Register<TCommand, TResult, TEvent>(Func<TCommand, TResult> handle, Func<TCommand, TResult, TEvent> describe)
        where TCommand : ICommand<TResult>
        where TResult : ICommandResult
        where TEvent : IEvent
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentNullException.ThrowIfNull(describe);

        return Add(typeof(TCommand), new Handled<TCommand, TResult>(handle, (command, result, events) => events.Publish(describe(command, result))));
    }

    internal Entry<TResult>? Find<TResult>(ICommand<TResult> command)
        where TResult : ICommandResult =>
        _entries.TryGetValue(command.GetType(), out object? entry) ? (Entry<TResult>)entry : null;

    private CommandRegistry Add(Type command, object entry)
    {
        if (!_entries.TryAdd(command, entry))
        {
            throw new InvalidOperationException($"{command.Name} is already registered; a command has exactly one handler.");
        }

        return this;
    }

    /// <summary>One registered command, as the dispatcher sees it: run it, and on success publish its event.</summary>
    internal abstract class Entry<TResult>
        where TResult : ICommandResult
    {
        public abstract TResult Run(ICommand<TResult> command, EventHub events);
    }

    private sealed class Handled<TCommand, TResult>(Func<TCommand, TResult> handle, Action<TCommand, TResult, EventHub>? publish)
        : Entry<TResult>
        where TCommand : ICommand<TResult>
        where TResult : ICommandResult
    {
        public override TResult Run(ICommand<TResult> command, EventHub events)
        {
            var typed = (TCommand)command;
            TResult result = handle(typed);

            // The mutation is written by the time the handler returns; only then, and only on success, is it announced.
            if (result.IsSuccess && publish is not null)
            {
                publish(typed, result, events);
            }

            return result;
        }
    }
}
