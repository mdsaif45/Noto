using Noto.Core.Diagnostics;

namespace Noto.UseCases.Lifetime;

/// <summary>
/// What Noto does when an exception reaches a last-chance boundary (#10): record it, save the open note once, and
/// end once.
/// </summary>
/// <remarks>
/// <para>
/// <b>The first failure is the only one handled.</b> One fault can reach more than one boundary — the UI thread's,
/// then the runtime's as the process goes down — and a failure during the handling itself would otherwise start the
/// handling again. Whichever boundary arrives first records the failure, saves and ends the application; every later
/// call, from any thread, returns at once.
/// </para>
/// <para>
/// <b>The save is the editor's own</b> — the same one a hide or the session's end uses — tried once. If it throws,
/// that is recorded and the shutdown goes ahead: nothing here is retried, and nothing here throws.
/// </para>
/// <para>
/// <b>The shutdown is the application's own one transition</b> (ADR-014), passed in; this class adds none.
/// </para>
/// </remarks>
public sealed class FailureBoundary
{
    private readonly IAppLog _log;
    private readonly Func<bool> _trySave;
    private readonly Action _shutdown;
    private int _handled;

    public FailureBoundary(IAppLog log, Func<bool> trySave, Action shutdown)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _trySave = trySave ?? throw new ArgumentNullException(nameof(trySave));
        _shutdown = shutdown ?? throw new ArgumentNullException(nameof(shutdown));
    }

    /// <summary>Whether a failure has already been handled: the application is ending.</summary>
    public bool Handled => Volatile.Read(ref _handled) != 0;

    /// <summary>Handles the first failure; ignores any after it. Returns whether this call handled it.</summary>
    public bool Fail(UnhandledSource source, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (Interlocked.Exchange(ref _handled, 1) != 0)
        {
            return false;
        }

        _log.UnhandledException(source, ExceptionSummary.From(exception));

        try
        {
            _log.EmergencySave(_trySave());
        }
        catch (Exception ex)
        {
            // The process is already failing: a save that throws is recorded, never handled again or rethrown.
            _log.EmergencySaveFailed(ExceptionSummary.From(ex));
        }

        _shutdown();
        return true;
    }
}
