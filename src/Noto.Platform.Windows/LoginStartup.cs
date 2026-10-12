using System.Security;
using Microsoft.Win32;

namespace Noto.Platform.Windows;

/// <summary>Whether Noto starts when the user signs in (parity A16, ADR-015), as Windows has it.</summary>
public enum LoginStartupState
{
    /// <summary>No registration for this Noto: absent, unreadable, or for another executable.</summary>
    Off,

    /// <summary>Registered for this Noto, and Windows has not turned it off.</summary>
    On,

    /// <summary>
    /// Registered for this Noto, but Windows' Startup apps (or Task Manager) has
    /// turned it off — or that setting could not be read. Never reported as on,
    /// and never changed by Noto.
    /// </summary>
    WindowsDisabled,
}

/// <summary>How Windows' Startup apps setting reads for one entry.</summary>
internal enum StartupApproval
{
    /// <summary>Not turned off: no entry, or an entry marked enabled.</summary>
    Enabled,

    /// <summary>Turned off by the user in Windows.</summary>
    Disabled,

    /// <summary>An entry in a form not recognised. Treated as turned off, never as on.</summary>
    Unreadable,
}

/// <summary>The tray row for <see cref="LoginStartupState"/>: whether it can be chosen, and whether it is checked.</summary>
/// <param name="Enabled">It can be chosen: off or on.</param>
/// <param name="Checked">On.</param>
/// <param name="TurnedOffInWindows">Windows has turned it off; the row says where to change that.</param>
public readonly record struct LoginStartupRow(bool Enabled, bool Checked, bool TurnedOffInWindows)
{
    /// <summary>The row for a state. Windows-disabled is grayed: only Windows can turn it back on.</summary>
    public static LoginStartupRow For(LoginStartupState state) => state switch
    {
        LoginStartupState.On => new(Enabled: true, Checked: true, TurnedOffInWindows: false),
        LoginStartupState.WindowsDisabled => new(Enabled: false, Checked: false, TurnedOffInWindows: true),
        _ => new(Enabled: true, Checked: false, TurnedOffInWindows: false),
    };
}

/// <summary>
/// Noto's registration to start at sign-in, for the unpackaged product
/// (ADR-015): the value <c>Noto</c> under the current user's <c>Run</c> key.
/// </summary>
/// <remarks>
/// <para>
/// <b>Windows' registration is the only truth.</b> Nothing is stored by Noto:
/// every read asks Windows, so a registration removed or turned off elsewhere
/// is reported as it is, and is never put back.
/// </para>
/// <para>
/// <b>Startup apps is read, never written.</b> Windows records a user's
/// "off" under <c>Explorer\StartupApproved\Run</c>, in an undocumented binary
/// form. This class alone interprets it: a 12-byte value whose first byte is
/// <c>0x02</c> is enabled, <c>0x03</c> disabled (measured, #94 Phase 0); no
/// value is enabled; anything else is unreadable and treated as off. That key
/// is only ever opened read-only.
/// </para>
/// <para>
/// <b>Writes only when asked.</b> <see cref="Enable"/> and <see cref="Disable"/>
/// are the only writes, made when the user chooses; nothing is registered or
/// corrected at startup, so another copy of Noto (a debug build) never takes
/// the registration over by running.
/// </para>
/// </remarks>
public sealed class LoginRegistration
{
    /// <summary>The value name under both keys. Noto's own.</summary>
    internal const string ValueName = "Noto";

    /// <summary>The switch the registered command carries: a hidden start (ADR-015).</summary>
    public const string LoginSwitch = "--login";

    internal const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private readonly string _executable;
    private readonly string _runKey;
    private readonly string _approvedKey;

    internal LoginRegistration(string executable, string runKey, string approvedKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        _executable = Path.GetFullPath(executable);
        _runKey = runKey;
        _approvedKey = approvedKey;
    }

    /// <summary>The registration for this executable, in the current user's real keys.</summary>
    public static LoginRegistration ForCurrentUser(string executable) => new(executable, RunKey, ApprovedKey);

    /// <summary>The state now, read from Windows. Never throws: what cannot be read is not on.</summary>
    public LoginStartupState State()
    {
        try
        {
            using RegistryKey? run = Registry.CurrentUser.OpenSubKey(_runKey, writable: false);
            string? command = run is not null
                && Array.IndexOf(run.GetValueNames(), ValueName) >= 0
                && run.GetValueKind(ValueName) == RegistryValueKind.String
                ? run.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string
                : null;

            if (!PointsAt(command, _executable))
            {
                return LoginStartupState.Off;
            }

            using RegistryKey? approved = Registry.CurrentUser.OpenSubKey(_approvedKey, writable: false);

            return Classify(ReadApproval(approved));
        }
        catch (Exception ex) when (IsAccessFailure(ex))
        {
            return LoginStartupState.Off;
        }
    }

    /// <summary>Registers this executable to start hidden at sign-in, then reads the state back.</summary>
    /// <returns>The state after the write: on, or Windows-disabled if Windows has turned it off.</returns>
    public LoginStartupState Enable()
    {
        try
        {
            using RegistryKey run = Registry.CurrentUser.CreateSubKey(_runKey, writable: true);
            run.SetValue(ValueName, CommandFor(_executable), RegistryValueKind.String);
        }
        catch (Exception ex) when (IsAccessFailure(ex))
        {
            // The state read below says what is actually there.
        }

        return State();
    }

    /// <summary>Removes the registration, then reads the state back.</summary>
    public LoginStartupState Disable()
    {
        try
        {
            using RegistryKey? run = Registry.CurrentUser.OpenSubKey(_runKey, writable: true);
            run?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception ex) when (IsAccessFailure(ex))
        {
            // As above.
        }

        return State();
    }

    /// <summary>The registered command: the quoted full path and the login switch. Never a data root.</summary>
    internal static string CommandFor(string executable) => $"\"{executable}\" {LoginSwitch}";

    /// <summary>Whether a registered command starts exactly this executable with the login switch.</summary>
    internal static bool PointsAt(string? command, string executable)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        string text = command.Trim();
        string path;
        string rest;

        if (text.StartsWith('"'))
        {
            int close = text.IndexOf('"', 1);

            if (close < 0)
            {
                return false;
            }

            path = text[1..close];
            rest = text[(close + 1)..];
        }
        else
        {
            int space = text.IndexOf(' ', StringComparison.Ordinal);
            path = space < 0 ? text : text[..space];
            rest = space < 0 ? string.Empty : text[space..];
        }

        if (!string.Equals(rest.Trim(), LoginSwitch, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            return string.Equals(Path.GetFullPath(path), Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>Windows' Startup apps setting for Noto's entry, from a key opened read-only.</summary>
    internal static StartupApproval ReadApproval(RegistryKey? approved)
    {
        if (approved is null || Array.IndexOf(approved.GetValueNames(), ValueName) < 0)
        {
            return StartupApproval.Enabled;
        }

        return approved.GetValueKind(ValueName) == RegistryValueKind.Binary
            ? Interpret(approved.GetValue(ValueName) as byte[])
            : StartupApproval.Unreadable;
    }

    /// <summary>The undocumented form, as measured: 12 bytes, the first <c>0x02</c> enabled or <c>0x03</c> disabled.</summary>
    internal static StartupApproval Interpret(byte[]? data) => data switch
    {
        { Length: 12 } when data[0] == 0x02 => StartupApproval.Enabled,
        { Length: 12 } when data[0] == 0x03 => StartupApproval.Disabled,
        _ => StartupApproval.Unreadable,
    };

    /// <summary>The state of a registration that points at this Noto, given Windows' setting.</summary>
    internal static LoginStartupState Classify(StartupApproval approval) =>
        approval == StartupApproval.Enabled ? LoginStartupState.On : LoginStartupState.WindowsDisabled;

    private static bool IsAccessFailure(Exception ex) =>
        ex is SecurityException or UnauthorizedAccessException or IOException;
}
