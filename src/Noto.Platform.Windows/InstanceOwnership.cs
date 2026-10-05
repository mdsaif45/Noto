using System.Diagnostics;

namespace Noto.Platform.Windows;

/// <summary>How a launch's claim on its data root turned out.</summary>
public enum ClaimOutcome
{
    /// <summary>This process owns the data root and starts normally.</summary>
    Owner,

    /// <summary>The running owner took the request; this process exits (code 0).</summary>
    HandedOff,

    /// <summary>An owner holds the data root but could not be reached in time (code 2).</summary>
    Unreachable,

    /// <summary>Refused: see <see cref="InstanceClaim.Refusal"/> (code 3).</summary>
    Refused,
}

/// <summary>Why a claim was refused.</summary>
public enum ClaimRefusal
{
    /// <summary>Not refused.</summary>
    None,

    /// <summary>This process is elevated and no normal Noto is running to hand off to.</summary>
    Elevated,

    /// <summary>The data root is owned by Noto in another Windows session.</summary>
    OtherSession,

    /// <summary>The pipe is served by a process running as another user.</summary>
    WrongUser,

    /// <summary>The running Noto rejected the request — a protocol it does not speak.</summary>
    Rejected,

    /// <summary>The ownership mutex's name is held by something this process may not open.</summary>
    Denied,
}

/// <summary>The result of <see cref="InstanceOwnership.Claim"/>; the ownership itself is passed separately.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Refusal">Why, when <see cref="ClaimOutcome.Refused"/>.</param>
/// <param name="TookOver">The owner was found closing and this process took its place.</param>
public readonly record struct InstanceClaim(ClaimOutcome Outcome, ClaimRefusal Refusal, bool TookOver);

/// <summary>The timeouts <see cref="InstanceOwnership.Claim"/> works to.</summary>
/// <param name="Unreachable">How long an owner may stay silent before the launch gives up.</param>
/// <param name="Takeover">How long to wait for a closing owner to let go.</param>
/// <param name="Retry">The pause between attempts.</param>
internal readonly record struct ClaimTimings(TimeSpan Unreachable, TimeSpan Takeover, TimeSpan Retry)
{
    public static ClaimTimings Default { get; } = new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(100));
}

/// <summary>
/// Ownership of one data root: a named mutex held for the life of the
/// owning process (A17, ADR-013).
/// </summary>
/// <remarks>
/// <para>
/// <b>Atomic.</b> Ownership is the mutex's own: the one wait that acquires it
/// is the check, so two launches can never both own a root.
/// </para>
/// <para>
/// <b>Crash recovery.</b> When the owner dies without letting go, Windows
/// marks the mutex abandoned and hands it to the next waiter — which is
/// ownership, reported as <see cref="Recovered"/>. A crash never locks a
/// root.
/// </para>
/// <para>
/// <b>Thread.</b> A mutex belongs to the thread that acquired it. Acquire and
/// dispose on the same thread — the UI thread. Disposed elsewhere, the
/// handle is closed without releasing, and the mutex is freed as abandoned
/// when the owning thread or process ends.
/// </para>
/// </remarks>
public sealed class InstanceOwnership : IDisposable
{
    private readonly Mutex _mutex;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private bool _owned;
    private bool _disposed;

    /// <summary>Opens (or creates) the mutex; it is not owned until <see cref="Take"/> succeeds.</summary>
    private InstanceOwnership(string mutexName) => _mutex = new Mutex(initiallyOwned: false, mutexName);

    /// <summary>The previous owner died holding the root; this one recovered it.</summary>
    public bool Recovered { get; private set; }

    /// <summary>
    /// Takes ownership if no one holds it, without waiting.
    /// </summary>
    /// <returns>The ownership, or <see langword="null"/> if another holds it.</returns>
    /// <exception cref="UnauthorizedAccessException">The name is held by an object this process may not open.</exception>
    /// <exception cref="WaitHandleCannotBeOpenedException">The name is held by an object of another kind.</exception>
    public static InstanceOwnership? TryAcquire(InstanceKey key) => Acquire(key, TimeSpan.Zero);

    /// <summary>Waits up to <paramref name="timeout"/> for ownership.</summary>
    /// <returns>The ownership, or <see langword="null"/> if the wait ran out.</returns>
    public static InstanceOwnership? Acquire(InstanceKey key, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(key);

        var candidate = new InstanceOwnership(key.MutexName);

        return candidate.TakeOrLetGo(timeout) ? candidate : null;
    }

    /// <summary>
    /// Waits for the mutex. Not taken — or the wait failed — the handle is
    /// closed at once and this instance is spent.
    /// </summary>
    private bool TakeOrLetGo(TimeSpan timeout)
    {
        try
        {
            _owned = _mutex.WaitOne(timeout);
        }
        catch (AbandonedMutexException)
        {
            // Acquired: the previous owner died holding it.
            _owned = true;
            Recovered = true;
        }
        catch
        {
            Dispose();
            throw;
        }

        if (!_owned)
        {
            Dispose();
        }

        return _owned;
    }

    /// <summary>
    /// Whether anything holds the root, without taking it — what an
    /// elevated launch, which must never own, asks instead.
    /// </summary>
    public static bool IsHeld(InstanceKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (!Mutex.TryOpenExisting(key.MutexName, out Mutex? existing))
        {
            return false;
        }

        existing.Dispose();
        return true;
    }

    /// <summary>
    /// Decides what this launch is: the owner, or a second launch that hands
    /// its request to the owner and exits.
    /// </summary>
    /// <remarks>
    /// <list type="number">
    /// <item>Take the mutex without waiting. Taken (or abandoned): owner.</item>
    /// <item>Held: send <see cref="ActivationRequest.Activate"/> over the pipe.
    /// Accepted: handed off. Rejected, another user or another session: refused.</item>
    /// <item>Shutting down: wait up to 10 s for the mutex and take over, or
    /// give up as unreachable.</item>
    /// <item>No answer: try the mutex again (the owner may just have died),
    /// then the pipe, every 100 ms, for up to 5 s; then unreachable.</item>
    /// </list>
    /// An elevated launch never takes the mutex (ADR-007 §8): it hands off
    /// to a running owner, or is refused when there is none.
    /// </remarks>
    /// <param name="key">The data root's key.</param>
    /// <param name="elevated">Whether this process is elevated.</param>
    /// <param name="ownership">Held for the life of the process when the outcome is <see cref="ClaimOutcome.Owner"/>.</param>
    public static InstanceClaim Claim(InstanceKey key, bool elevated, out InstanceOwnership? ownership) =>
        Claim(key, elevated, ClaimTimings.Default, out ownership);

    internal static InstanceClaim Claim(InstanceKey key, bool elevated, ClaimTimings timings, out InstanceOwnership? ownership)
    {
        ArgumentNullException.ThrowIfNull(key);

        ownership = null;

        try
        {
            if (TryOwn(key, elevated, out ownership) is InstanceClaim first)
            {
                return first;
            }

            var clock = Stopwatch.StartNew();

            while (true)
            {
                TimeSpan remaining = timings.Unreachable - clock.Elapsed;
                TimeSpan connect = remaining < ActivationPipeClient.ConnectTimeout ? remaining : ActivationPipeClient.ConnectTimeout;

                switch (ActivationPipeClient.Send(key, ActivationRequest.Activate, connect))
                {
                    case HandoffStatus.Accepted:
                        return new InstanceClaim(ClaimOutcome.HandedOff, ClaimRefusal.None, TookOver: false);

                    case HandoffStatus.Rejected:
                        return Refused(ClaimRefusal.Rejected);

                    case HandoffStatus.WrongUser:
                        return Refused(ClaimRefusal.WrongUser);

                    case HandoffStatus.OtherSession:
                        return Refused(ClaimRefusal.OtherSession);

                    case HandoffStatus.ShuttingDown:
                        if (elevated)
                        {
                            return Refused(ClaimRefusal.Elevated);
                        }

                        ownership = Acquire(key, timings.Takeover);

                        return ownership is null
                            ? new InstanceClaim(ClaimOutcome.Unreachable, ClaimRefusal.None, TookOver: false)
                            : new InstanceClaim(ClaimOutcome.Owner, ClaimRefusal.None, TookOver: true);
                }

                // No answer. The owner may be starting, hung, or just gone.
                if (TryOwn(key, elevated, out ownership) is InstanceClaim later)
                {
                    return later;
                }

                if (clock.Elapsed >= timings.Unreachable)
                {
                    return new InstanceClaim(ClaimOutcome.Unreachable, ClaimRefusal.None, TookOver: false);
                }

                Thread.Sleep(timings.Retry);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or WaitHandleCannotBeOpenedException)
        {
            return Refused(ClaimRefusal.Denied);
        }
    }

    /// <summary>
    /// Ownership if it can be had now; a refusal for an elevated launch with
    /// no owner; <see langword="null"/> when an owner exists to hand off to.
    /// </summary>
    private static InstanceClaim? TryOwn(InstanceKey key, bool elevated, out InstanceOwnership? ownership)
    {
        ownership = null;

        if (elevated)
        {
            return IsHeld(key) ? null : Refused(ClaimRefusal.Elevated);
        }

        ownership = TryAcquire(key);

        return ownership is null ? null : new InstanceClaim(ClaimOutcome.Owner, ClaimRefusal.None, TookOver: false);
    }

    private static InstanceClaim Refused(ClaimRefusal refusal) =>
        new(ClaimOutcome.Refused, refusal, TookOver: false);

    /// <summary>
    /// Lets go of the root. On the acquiring thread the mutex is released, so
    /// the next launch owns it at once.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_owned && Environment.CurrentManagedThreadId == _thread)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
    }
}
