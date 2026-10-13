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
/// that is recorded and the shutdown goes ahead. If the log itself throws, that write is skipped: neither the save
/// nor the shutdown depends on the log. Nothing here is retried, and nothing here throws.
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

        // Once claimed, the application ends whatever else fails here: the shutdown is in a finally, and a log
        // that throws is skipped rather than allowed to stop the save or the end.
        try
        {
            Record(() => _log.UnhandledException(source, ExceptionSummary.From(exception)));

            bool saved;

            try
            {
                saved = _trySave();
            }
            catch (Exception ex)
            {
                // The process is already failing: a save that throws is recorded, never handled again or rethrown.
                Record(() => _log.EmergencySaveFailed(ExceptionSummary.From(ex)));
                return true;
            }

            Record(() => _log.EmergencySave(saved));
            return true;
        }
        finally
        {
            _shutdown();
        }
    }

    private static void Record(Action write)
    {
        try
        {
            write();
        }
        catch (Exception)
        {
            // Best effort: the log's own failure is the one thing that cannot be recorded.
        }
    }
}
