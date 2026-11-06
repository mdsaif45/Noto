namespace Noto.Platform.Windows;

/// <summary>
/// What an installed Noto is to Windows (ADR-008): one app with the Start menu shortcut the installer made.
/// </summary>
/// <remarks>
/// Velopack gives that shortcut the identity <c>velopack.&lt;packId&gt;</c>, and its own start-up code would give it to
/// the process too. Noto runs that code only for the installer's hooks (it writes a log line on every start), so it
/// takes the same identity here; the taskbar then treats the window and the shortcut as one app, pinned or not. A copy
/// that was not installed (a build, a harness run) has no <c>sq.version</c> beside it and keeps Windows' default.
/// </remarks>
public static class InstalledCopy
{
    /// <summary><c>velopack.</c> and the pack id, <c>tools/packaging/New-Release.ps1</c>'s <c>--packId</c>.</summary>
    public const string AppUserModelId = "velopack.Noto.App";

    /// <summary>The first argument Velopack starts the app with to run one of its hooks.</summary>
    public const string HookPrefix = "--veloapp-";

    /// <summary>Whether Velopack started this process to run a hook (install, update, uninstall).</summary>
    public static bool IsHook(string[] commandLine) =>
        commandLine.Length > 1 && commandLine[1].StartsWith(HookPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the executable is an installed copy: Velopack's manifest is beside it.</summary>
    public static bool IsInstalled(string? executable) =>
        Path.GetDirectoryName(executable) is { } directory && File.Exists(Path.Join(directory, "sq.version"));

    /// <summary>Gives an installed copy its shortcut's identity. Before any window exists.</summary>
    /// <returns>Whether the identity was set.</returns>
    public static bool TakeShortcutIdentity(string? executable) =>
        IsInstalled(executable) && NativeMethods.SetCurrentProcessExplicitAppUserModelID(AppUserModelId) >= 0;
}
