namespace Noto.Platform.Windows;

/// <summary>
/// At most one launch request waiting for the UI thread (A17, ADR-013).
/// </summary>
/// <remarks>
/// <para>
/// The activation pipe receives requests on a pool thread; the window may
/// only be touched on the UI thread. A request is offered here and the UI
/// thread is asked to run once; requests that arrive before it runs fold
/// into the one already waiting. Launch requests are idempotent — "make sure
/// Noto is shown" — so the newest request time is the only one that matters,
/// and nothing can pile up however many launches arrive.
/// </para>
/// <para>
/// Nothing is lost: a request offered while the UI thread is taking the
/// previous one either is taken with it or schedules another run.
/// </para>
/// </remarks>
public sealed class PendingLaunch
{
    private int _waiting;
    private int _requestTime;

    /// <summary>Records a request made at <paramref name="requestTime"/>.</summary>
    /// <returns>
    /// <see langword="true"/> when nothing was waiting, so the caller must ask
    /// the UI thread to run <see cref="Take"/>; <see langword="false"/> when the
    /// request joined one already waiting.
    /// </returns>
    public bool Offer(int requestTime)
    {
        Volatile.Write(ref _requestTime, requestTime);
        return Interlocked.Exchange(ref _waiting, 1) == 0;
    }

    /// <summary>Takes the waiting request; its time is the newest offered.</summary>
    public int Take()
    {
        _ = Interlocked.Exchange(ref _waiting, 0);
        return Volatile.Read(ref _requestTime);
    }
}
