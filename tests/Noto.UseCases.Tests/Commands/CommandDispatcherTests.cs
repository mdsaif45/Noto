using Noto.Core.Commands;
using Noto.Core.Diagnostics;
using Noto.UseCases.Commands;
using Xunit;

namespace Noto.UseCases.Tests.Commands;

/// <summary>
/// #21: the registry, the dispatcher and the event hub ADR-010 describes. A command runs through its one registered
/// handler; its event is published only after it succeeds; a subscriber that throws is isolated and reported; nesting
/// is allowed and bounded; everything else is a programming error that fails loudly.
/// </summary>
public sealed class CommandDispatcherTests
{
    private readonly List<string> _trail = [];
    private readonly List<ExceptionSummary> _subscriberFailures = [];
    private readonly EventHub _events;

    public CommandDispatcherTests() => _events = new EventHub(_subscriberFailures.Add);

    [Fact]
    public void A_command_runs_its_registered_handler_and_returns_its_result()
    {
        var dispatcher = Dispatcher(Registry());

        CommandResult<int> result = dispatcher.Dispatch(new Add(2, 3));

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value);
        Assert.Equal(["handled Add"], _trail);
    }

    [Fact]
    public void An_unregistered_command_fails_and_names_itself()
    {
        var dispatcher = Dispatcher(new CommandRegistry());

        var error = Assert.Throws<InvalidOperationException>(() => dispatcher.Dispatch(new Add(1, 1)));

        Assert.Contains(nameof(Add), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_command_cannot_be_registered_twice()
    {
        var registry = new CommandRegistry().Register<Refuse, CommandResult>(_ => CommandResult.Success());

        var error = Assert.Throws<InvalidOperationException>(() => registry.Register<Refuse, CommandResult>(_ => CommandResult.Success()));

        Assert.Contains(nameof(Refuse), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Success_publishes_the_event_once_after_the_handler_has_run_to_every_subscriber_in_order()
    {
        var dispatcher = Dispatcher(Registry());
        using var first = _events.Subscribe<Added>(e => _trail.Add($"first {e.Sum}"));
        using var second = _events.Subscribe<Added>(e => _trail.Add($"second {e.Sum}"));

        dispatcher.Dispatch(new Add(2, 3));

        Assert.Equal(["handled Add", "first 5", "second 5"], _trail);
    }

    [Fact]
    public void A_failed_command_publishes_nothing()
    {
        var dispatcher = Dispatcher(Registry());
        using var subscription = _events.Subscribe<Refused>(_ => _trail.Add("published"));

        CommandResult result = dispatcher.Dispatch(new Refuse());

        Assert.False(result.IsSuccess);
        Assert.Equal(["handled Refuse"], _trail);
    }

    [Fact]
    public void A_handler_that_throws_publishes_nothing_and_its_exception_reaches_the_caller()
    {
        var dispatcher = Dispatcher(Registry());
        using var subscription = _events.Subscribe<Exploded>(_ => _trail.Add("published"));

        Assert.Throws<TimeoutException>(() => dispatcher.Dispatch(new Explode()));

        Assert.Empty(_trail);

        // The depth is released on the way out, every time: after as many throws as the bound, a command still runs.
        for (int i = 1; i < CommandDispatcher.MaxDepth; i++)
        {
            Assert.Throws<TimeoutException>(() => dispatcher.Dispatch(new Explode()));
        }

        Assert.True(dispatcher.Dispatch(new Add(1, 1)).IsSuccess);
    }

    [Fact]
    public void A_subscriber_that_throws_is_reported_once_and_neither_stops_the_others_nor_fails_the_command()
    {
        var dispatcher = Dispatcher(Registry());
        using var failing = _events.Subscribe<Added>(_ => throw new InvalidOperationException("SECRET-4711"));
        using var after = _events.Subscribe<Added>(e => _trail.Add($"after {e.Sum}"));

        CommandResult<int> result = dispatcher.Dispatch(new Add(2, 3));

        Assert.True(result.IsSuccess);
        Assert.Equal(["handled Add", "after 5"], _trail);
        var failure = Assert.Single(_subscriberFailures);
        Assert.Equal(["System.InvalidOperationException"], failure.Types);
    }

    [Fact]
    public void A_subscriber_may_dispatch_a_command_and_that_commands_event_follows()
    {
        var dispatcher = Dispatcher(Registry());
        using var cascade = _events.Subscribe<Added>(e =>
        {
            if (e.Sum < 10)
            {
                dispatcher.Dispatch(new Add(e.Sum, 10));
            }
        });
        using var record = _events.Subscribe<Added>(e => _trail.Add($"added {e.Sum}"));

        dispatcher.Dispatch(new Add(1, 2));

        // Depth first: the nested command and its event complete inside the outer delivery.
        Assert.Equal(["handled Add", "handled Add", "added 13", "added 3"], _trail);
        Assert.Empty(_subscriberFailures);
    }

    [Fact]
    public void A_chain_of_commands_deeper_than_the_bound_is_refused_reported_and_leaves_the_outer_results_intact()
    {
        var dispatcher = Dispatcher(Registry());
        using var loop = _events.Subscribe<Added>(e => dispatcher.Dispatch(new Add(e.Sum, 1)));

        CommandResult<int> result = dispatcher.Dispatch(new Add(0, 1));

        Assert.True(result.IsSuccess);
        Assert.Equal(CommandDispatcher.MaxDepth, _trail.Count(t => t == "handled Add"));
        var failure = Assert.Single(_subscriberFailures);
        Assert.Equal(["System.InvalidOperationException"], failure.Types);

        // The depth unwound: a fresh command still runs once the loop is gone.
        loop.Dispose();
        _trail.Clear();
        Assert.True(dispatcher.Dispatch(new Add(1, 1)).IsSuccess);
        Assert.Equal(["handled Add"], _trail);
    }

    [Fact]
    public void A_disposed_subscription_is_told_nothing_more_and_the_others_still_are()
    {
        var dispatcher = Dispatcher(Registry());
        using var gone = _events.Subscribe<Added>(_ => _trail.Add("gone"));
        using var stays = _events.Subscribe<Added>(_ => _trail.Add("stays"));

        gone.Dispose();
        gone.Dispose();
        dispatcher.Dispatch(new Add(1, 1));

        Assert.Equal(["handled Add", "stays"], _trail);
    }

    [Fact]
    public void Disposing_one_of_two_identical_subscriptions_removes_only_one()
    {
        var dispatcher = Dispatcher(Registry());
        Action<Added> twice = _ => _trail.Add("told");
        using var one = _events.Subscribe(twice);
        using var other = _events.Subscribe(twice);

        one.Dispose();
        dispatcher.Dispatch(new Add(1, 1));

        Assert.Equal(["handled Add", "told"], _trail);
    }

    [Fact]
    public void The_depth_belongs_to_the_chain_so_a_dispatch_starting_elsewhere_is_not_counted_against_it()
    {
        var dispatcher = Dispatcher(Registry());
        Exception? inThisChain = null;
        Exception? inAnotherChain = null;
        CommandResult? fromAnotherChain = null;
        using var deepen = _events.Subscribe<Added>(e =>
        {
            if (e.Sum < CommandDispatcher.MaxDepth)
            {
                dispatcher.Dispatch(new Add(e.Sum, 1));
                return;
            }

            // At the bound: one more command in this chain is a loop; a dispatch that starts on another thread is a
            // chain of its own and runs.
            inThisChain = Record.Exception(() => dispatcher.Dispatch(new Refuse()));
            var elsewhere = new Thread(() => inAnotherChain = Record.Exception(() => fromAnotherChain = dispatcher.Dispatch(new Refuse())));
            elsewhere.Start();
            elsewhere.Join();
        });

        dispatcher.Dispatch(new Add(0, 1));

        Assert.IsType<InvalidOperationException>(inThisChain);
        Assert.Null(inAnotherChain);
        Assert.NotNull(fromAnotherChain);
        Assert.Equal(1, _trail.Count(t => t == "handled Refuse"));
        Assert.Equal(CommandDispatcher.MaxDepth, _trail.Count(t => t == "handled Add"));
    }

    private CommandDispatcher Dispatcher(CommandRegistry registry) => new(registry, _events);

    private CommandRegistry Registry() => new CommandRegistry()
        .Register<Add, CommandResult<int>, Added>(
            command =>
            {
                _trail.Add("handled Add");
                return CommandResult.Success(command.A + command.B);
            },
            static (_, result) => new Added(result.Value))
        .Register<Refuse, CommandResult, Refused>(
            _ =>
            {
                _trail.Add("handled Refuse");
                return CommandResult.Failed(CommandFailure.InvalidState("refused"));
            },
            static (_, _) => new Refused())
        .Register<Explode, CommandResult, Exploded>(_ => throw new TimeoutException(), static (_, _) => new Exploded());

    private sealed record Add(int A, int B) : ICommand<CommandResult<int>>;

    private sealed record Added(int Sum) : IEvent;

    private sealed record Refuse : ICommand<CommandResult>;

    private sealed record Refused : IEvent;

    private sealed record Explode : ICommand<CommandResult>;

    private sealed record Exploded : IEvent;
}
