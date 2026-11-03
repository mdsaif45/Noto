namespace Noto.Platform.Windows;

/// <summary>
/// A bounded-shutdown safeguard (ADR-017 §10), not a fix: it limits how long the process may take to end once Noto is
/// done with it. Armed when the window has closed, after every note was saved and every resource released (the
/// database, the mutex, the pipe, the hotkey, the tray icon): from then on nothing of the user's is at stake, so a
/// framework teardown that never returns cannot hold the process, and a later start, open.
/// </summary>
/// <remarks>
/// Measured once in development: Noto traced its whole shutdown and was still alive 10 s later, after its last trace,
/// in Windows App SDK teardown. The cause is unknown and stays an RC item (implementation plan R17); it did not recur
/// in 60 timed close cycles (Invoke-ExitCycles.ps1). <c>onOverrun</c> records each time the bound was needed, so a
/// recurrence is evidence.
/// </remarks>
public static class ExitWatchdog
{
    /// <summary>
    /// Starts the clock on a background thread, which ends with the process if it ends in time. After
    /// <paramref name="grace"/>, <paramref name="onOverrun"/> runs and the process ends with exit code 0.
    /// </summary>
    // Code 0: everything was saved and released before the clock started; this ends an idle process.
    public static void Arm(TimeSpan grace, Action? onOverrun) =>
        Arm(grace, onOverrun, () => NativeMethods.TerminateProcess(NativeMethods.GetCurrentProcess(), 0));

    /// <summary>The clock itself, with what ends the process given: the tests' seam.</summary>
    internal static void Arm(TimeSpan grace, Action? onOverrun, Action end)
    {
        var clock = new Thread(() =>
        {
            Thread.Sleep(grace);

            try
            {
                onOverrun?.Invoke();
            }
            finally
            {
                end();
            }
        })
        {
            IsBackground = true,
            Name = "Noto exit watchdog",
        };

        clock.Start();
    }
}
