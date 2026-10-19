using System.ComponentModel;
using System.Diagnostics;

namespace Noto.Platform.Windows;

/// <summary>
/// Hands something to the Windows shell to open (Slice 1): the data folder in Explorer, and the Startup apps page of
/// Windows Settings.
/// </summary>
/// <remarks>
/// The shell opens it, not Noto: no process is waited on and nothing is read back. A path is never written to a
/// trace or a log (quality gates: logs never contain user paths); a failure reports only that it failed.
/// </remarks>
public static class ShellLaunch
{
    /// <summary>Windows Settings' Startup apps page, where a user can turn off an app's start at sign-in.</summary>
    public const string StartupAppsSettings = "ms-settings:startupapps";

    /// <summary>Opens <paramref name="folder"/> in Explorer.</summary>
    /// <returns>Whether the shell accepted it. A folder that does not exist is refused, never created.</returns>
    public static bool OpenFolder(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        return Directory.Exists(folder) && Open(folder);
    }

    /// <summary>Opens Windows Settings at the Startup apps page.</summary>
    /// <returns>Whether the shell accepted it.</returns>
    public static bool OpenStartupAppsSettings() => Open(StartupAppsSettings);

    private static bool Open(string target)
    {
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            Trace.WriteLine($"Noto.Shell open failed error={ex.GetType().Name}");
            return false;
        }
    }
}
