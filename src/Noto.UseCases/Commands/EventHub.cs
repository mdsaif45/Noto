using Noto.Core.Diagnostics;

namespace Noto.UseCases.Commands;

/// <summary>
/// The in-process event aggregator ADR-010 calls for: subscribe, publish, unsubscribe. Nothing more (#21).
/// </summary>
/// <remarks>
/// <para>
/// <b>Synchronous, on the publishing thread.</b> No queue, no bus, nothing across processes (ADR-010). Subscribing,
/// unsubscribing and publishing are safe from any thread; delivery runs on whichever thread published.
/// </para>
/// <para>
/// <b>A subscriber that throws is isolated.</b> The event is a fact about a mutation that has already succeeded, so a
/// subscriber's failure cannot undo it and must not stop the other subscribers: it is reported, as types only, and the
/// next subscriber is told. As everywhere in Noto, running out of memory is never swallowed.
/// </para>
/// <para>
/// Subscribers are kept per exact event type, in subscription order. Each subscribe or unsubscribe replaces the list, so
/// a subscriber that subscribes or unsubscribes while an event is being delivered changes only the next delivery.
/// </para>
/// </remarks>
public sealed class EventHub
{
    private readonly Action<ExceptionSummary> _subscriberFailed;
    private readonly Dictionary<Type, Delegate[]> _subscribers = [];
    private readonly Lock _gate = new();

    /// <summary>Creates an event hub with no subscribers.</summary>
    /// <param name="subscriberFailed">Where a subscriber's failure is reported: the application log.</param>
    public EventHub(Action<ExceptionSummary> subscriberFailed)
    {
        _subscriberFailed = subscriberFailed ?? throw new ArgumentNullException(nameof(subscriberFailed));
    }

    /// <summary>Tells <paramref name="subscriber"/> of every <typeparamref name="TEvent"/> until the returned handle is disposed.</summary>
    public IDisposable Subscribe<TEvent>(Action<TEvent> subscriber)
        where TEvent : IEvent
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        lock (_gate)
        {
            _subscribers[typeof(TEvent)] = [.. Subscribers(typeof(TEvent)), subscriber];
        }

        return new Subscription(() =>
        {
            lock (_gate)
            {
                _subscribers[typeof(TEvent)] = Without(Subscribers(typeof(TEvent)), subscriber);
            }
        });
    }

    /// <summary>Tells every subscriber of <typeparamref name="TEvent"/>, in subscription order.</summary>
    public void Publish<TEvent>(TEvent fact)
        where TEvent : IEvent
    {
        ArgumentNullException.ThrowIfNull(fact);

        Delegate[] subscribers;

        // The list is replaced, never changed in place, so delivery runs over a snapshot outside the lock.
        lock (_gate)
        {
            subscribers = Subscribers(typeof(TEvent));
        }

        foreach (Delegate subscriber in subscribers)
        {
            try
            {
                ((Action<TEvent>)subscriber)(fact);
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
            {
                _subscriberFailed(ExceptionSummary.From(ex));
            }
        }
    }

    // Called under _gate.
    private Delegate[] Subscribers(Type eventType) => _subscribers.TryGetValue(eventType, out Delegate[]? list) ? list : [];

    // One subscription removes one entry, even when the same delegate was subscribed twice.
    private static Delegate[] Without(Delegate[] list, Delegate subscriber)
    {
        int index = Array.FindIndex(list, s => ReferenceEquals(s, subscriber));
        return index < 0 ? list : [.. list[..index], .. list[(index + 1)..]];
    }

    private sealed class Subscription(Action unsubscribe) : IDisposable
    {
        private Action? _unsubscribe = unsubscribe;

        // Taken atomically, so of any number of disposals, even concurrent ones, exactly one unsubscribes.
        public void Dispose() => Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
    }
}
