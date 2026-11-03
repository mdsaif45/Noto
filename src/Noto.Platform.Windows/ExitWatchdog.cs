namespace Noto.Platform.Windows;

/// <summary>
/// Bounds how long the process may take to end once Noto is done with it (ADR-014). Armed when the window has closed,
/// after every note was saved and every resource released (the mutex, the pipe, the hotkey, the tray icon): from
/// then on nothing of the user's is at stake, so a framework teardown that never returns cannot hold the process,
/// and a later start, open.
/// </summary>
/// <remarks>
/// Measured once in development: Noto traced its whole shutdown and was still alive 10 s later, after its last trace,
/// in framework teardown; it did not recur in 40 timed close cycles (Invoke-ExitCycles.ps1). The watchdog makes the
/// bound hold whatever the cause, and <c>onOverrun</c> records each time it was needed, so a recurrence is evidence.
/// </remarks>
public static class ExitWatchdog
{
    /// <summary>
    /// Starts the clock on a background thread, which ends with the process if it ends in time. After
    /// <paramref name="grace"/>, <paramref name="onOverrun"/> runs and the process ends with exit code 0.
    /// </summary>
    public static void Arm(TimeSpan grace, Action? onOverrun)
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
                // Code 0: everything was saved and released before the clock started; this ends an idle process.
                _ = NativeMethods.TerminateProcess(NativeMethods.GetCurrentProcess(), 0);
            }
        })
        {
            IsBackground = true,
            Name = "Noto exit watchdog",
        };

        clock.Start();
    }
}
