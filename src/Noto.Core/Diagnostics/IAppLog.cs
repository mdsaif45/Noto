using Noto.Core.Storage;

namespace Noto.Core.Diagnostics;

/// <summary>
/// The application's failure events (#10): what is recorded when Noto cannot start, or fails while running.
/// </summary>
/// <remarks>
/// Typed like <see cref="IStorageLog"/>, and for the same reason (principle 10): a failure is an
/// <see cref="ExceptionSummary"/>, never a message or a stack trace, and nothing here takes free text. The database's
/// location is told to the user on screen; the log records only why it failed.
/// </remarks>
public interface IAppLog
{
    /// <summary>The database could not be opened or brought up to date, so Noto did not start.</summary>
    void StartupFailed(StorageFailure reason, ExceptionSummary failure);

    /// <summary>An exception reached one of the application's last-chance boundaries.</summary>
    void UnhandledException(UnhandledSource source, ExceptionSummary failure);

    /// <summary>After a failure, the open note's unsaved text was saved, or could not be.</summary>
    void EmergencySave(bool saved);

    /// <summary>After a failure, the attempt to save the open note itself threw.</summary>
    void EmergencySaveFailed(ExceptionSummary failure);

    /// <summary>A task faulted and nothing observed it. Not fatal: .NET does not end the process for it.</summary>
    void UnobservedTaskException(ExceptionSummary failure);

    /// <summary>
    /// A subscriber to an event threw (#21). Not fatal: the command that raised the event had already succeeded,
    /// and the other subscribers were still told (ADR-010).
    /// </summary>
    void EventSubscriberFailed(ExceptionSummary failure);
}

/// <summary>Which last-chance boundary saw an exception.</summary>
public enum UnhandledSource
{
    /// <summary>The UI thread: an exception a XAML event or callback did not handle.</summary>
    UiThread,

    /// <summary>Any other thread. The runtime ends the process once the boundary returns.</summary>
    OtherThread,
}
