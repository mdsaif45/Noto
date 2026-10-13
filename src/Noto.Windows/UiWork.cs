using Microsoft.UI.Dispatching;

namespace Noto;

/// <summary>
/// UI-thread work Noto queues itself (#10).
/// </summary>
/// <remarks>
/// <para>
/// An exception thrown by a <see cref="DispatcherQueue"/> callback reaches neither
/// <c>Application.UnhandledException</c> nor <c>AppDomain.UnhandledException</c>: WinUI fails the process fast
/// (measured: exit <c>0xC000027B</c>, nothing recorded, the open note not saved). Every callback Noto queues goes
/// through here instead, so its failure reaches the same failure boundary as every other.
/// </para>
/// <para>
/// Until <see cref="OnFailure"/> is set, an exception propagates exactly as before.
/// </para>
/// </remarks>
internal static class UiWork
{
    /// <summary>Where a queued callback's exception goes: set once, by the composition root.</summary>
    internal static Action<Exception>? OnFailure { get; set; }

    internal static bool TryEnqueueGuarded(this DispatcherQueue queue, DispatcherQueueHandler work) =>
        queue.TryEnqueue(() => Run(work));

    internal static bool TryEnqueueGuarded(this DispatcherQueue queue, DispatcherQueuePriority priority, DispatcherQueueHandler work) =>
        queue.TryEnqueue(priority, () => Run(work));

    private static void Run(DispatcherQueueHandler work)
    {
        try
        {
            work();
        }
        catch (Exception ex) when (OnFailure is not null)
        {
            OnFailure(ex);
        }
    }
}
