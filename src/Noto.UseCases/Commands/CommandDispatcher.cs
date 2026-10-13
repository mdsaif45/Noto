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
/// <b>One thread.</b> Dispatch happens on the thread that created the dispatcher, the UI thread, which is what makes
/// the depth count true. An entry point on another thread hands its work to the UI thread first.
/// </para>
/// </remarks>
public sealed class CommandDispatcher
{
    /// <summary>The deepest chain of commands dispatched from subscribers that is not a loop.</summary>
    public const int MaxDepth = 8;

    private readonly CommandRegistry _registry;
    private readonly EventHub _events;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private int _depth;

    public CommandDispatcher(CommandRegistry registry, EventHub events)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _events = events ?? throw new ArgumentNullException(nameof(events));
    }

    /// <summary>Runs <paramref name="command"/>; on success, publishes its registered event, if any.</summary>
    /// <returns>The handler's result. A failed result publishes nothing.</returns>
    /// <exception cref="InvalidOperationException">
    /// The command is not registered, the call is not on the dispatcher's thread, or commands are nested deeper than
    /// <see cref="MaxDepth"/>. Each is a programming error, never a business outcome (contract §6).
    /// </exception>
    public TResult Dispatch<TResult>(ICommand<TResult> command)
        where TResult : ICommandResult
    {
        ArgumentNullException.ThrowIfNull(command);

        if (Environment.CurrentManagedThreadId != _thread)
        {
            throw new InvalidOperationException($"{command.GetType().Name} was dispatched off the UI thread; hand it to the UI thread first.");
        }

        CommandRegistry.Entry<TResult> entry = _registry.Find(command)
            ?? throw new InvalidOperationException($"No handler is registered for {command.GetType().Name}.");

        if (_depth >= MaxDepth)
        {
            throw new InvalidOperationException($"{command.GetType().Name} was dispatched {MaxDepth} commands deep: events are causing commands in a loop.");
        }

        _depth++;

        try
        {
            return entry.Run(command, _events);
        }
        finally
        {
            _depth--;
        }
    }
}
