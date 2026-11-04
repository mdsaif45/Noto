using System.ComponentModel;
using System.Diagnostics;

namespace Noto.Platform.Windows;

/// <summary>
/// Starts Noto again (ADR-020 §3: a restore is applied at the next start, before anything opens the database). It
/// starts this executable only, never the shell and never a path from a note: a link is <see cref="ShellLaunch"/>'s.
/// </summary>
public static class Relaunch
{
    /// <summary>
    /// Starts this process's executable with <paramref name="arguments"/>. Called once everything the process holds is
    /// released, so the new process can claim the data root.
    /// </summary>
    /// <returns>Whether it started.</returns>
    public static bool Start(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (Environment.ProcessPath is not { } executable)
        {
            return false;
        }

        var start = new ProcessStartInfo(executable) { UseShellExecute = false };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using Process? process = Process.Start(start);
            return process is not null;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }
}
