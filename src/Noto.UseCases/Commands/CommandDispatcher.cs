using Noto.Core.Commands;

namespace Noto.UseCases.Commands;

/// <summary>
/// Runs a command through its registered handler and, when it succeeds, publishes its event (ADR-010, #21).
/// </summary>
/// <remarks>
/// <para>
/// <b>One way in.</b> Every mutation an entry point makes (a window, the tray, later <c>noto://</c>) is a
/// <see cref="Dispatch{TResult}"/>, so validation, diagnostics and undo have one place to be added, and a mutation made
/// anywhere is announced to every surface that shows it.
/// </para>
/// <para>
/// <b>Nesting is allowed and bounded.</b> ADR-010 says a subscriber that needs a change dispatches a command; that
/// command's own event can cause another, so a chain deeper than <see cref="MaxDepth"/> is refused as a loop. The
/// refusal is an exception inside the subscriber that dispatched, so the event hub reports it and the outer commands
/// keep their results.
/// </para>
/// <para>
/// <b>The depth belongs to the chain, not to the dispatcher.</b> A chain is synchronous (a handler, then its event's
/// subscribers, then any command they dispatch), so it runs on one thread from start to finish, and the depth is kept
/// per thread. A dispatch that starts anywhere else starts its own chain at zero. Which thread an entry point dispatches
/// on is the entry point's concern, not this class's.
/// </para>
/// </remarks>
public sealed class CommandDispatcher
{
    /// <summary>The deepest chain of commands dispatched from subscribers that is not a loop.</summary>
    public const int MaxDepth = 8;

    // How deep the chain currently running on this thread is. A synchronous chain never leaves its thread.
    [ThreadStatic]
    private static int _chainDepth;

    private readonly CommandRegistry _registry;
    private readonly EventHub _events;

    public CommandDispatcher(CommandRegistry registry, EventHub events)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _events = events ?? throw new ArgumentNullException(nameof(events));
    }

    /// <summary>Runs <paramref name="command"/>; on success, publishes its registered event, if any.</summary>
    /// <returns>The handler's result. A failed result publishes nothing.</returns>
    /// <exception cref="InvalidOperationException">
    /// The command is not registered, or commands are nested deeper than <see cref="MaxDepth"/> in one chain. Each is
    /// a programming error, never a business outcome (contract §6).
    /// </exception>
    public TResult Dispatch<TResult>(ICommand<TResult> command)
        where TResult : ICommandResult
    {
        ArgumentNullException.ThrowIfNull(command);

        CommandRegistry.Entry<TResult> entry = _registry.Find(command)
            ?? throw new InvalidOperationException($"No handler is registered for {command.GetType().Name}.");

        return RunInChain(entry, command, _events);
    }

    // The chain's depth is the thread's, not this instance's, so it is counted here, in static code.
    private static TResult RunInChain<TResult>(CommandRegistry.Entry<TResult> entry, ICommand<TResult> command, EventHub events)
        where TResult : ICommandResult
    {
        if (_chainDepth >= MaxDepth)
        {
            throw new InvalidOperationException($"{command.GetType().Name} was dispatched {MaxDepth} commands deep: events are causing commands in a loop.");
        }

        _chainDepth++;

        try
        {
            return entry.Run(command, events);
        }
        finally
        {
            _chainDepth--;
        }
    }
}
