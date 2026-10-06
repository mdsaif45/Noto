using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace Noto.Platform.Windows;

/// <summary>
/// Who a process runs as: its user, its Windows session, and whether it is
/// elevated (A17, ADR-013).
/// </summary>
/// <remarks>
/// Read from the process token each time, never cached: the answers decide
/// who may own a data root and which running Noto a second launch may trust.
/// </remarks>
public static class ProcessIdentity
{
    /// <summary>The current process's user, as a SID string (<c>S-1-5-21-…</c>).</summary>
    /// <exception cref="InvalidOperationException">The token names no user.</exception>
    public static string CurrentUserSid()
    {
        using var identity = WindowsIdentity.GetCurrent();

        return identity.User?.Value
            ?? throw new InvalidOperationException("The current process token names no user.");
    }

    /// <summary>The Windows session the current process runs in.</summary>
    public static int CurrentSession() => SessionOf(Environment.ProcessId);

    /// <summary>
    /// Whether the current process runs elevated — with the full
    /// administrator token rather than the filtered one UAC gives a normal
    /// launch (<c>TokenElevation</c>).
    /// </summary>
    /// <exception cref="Win32Exception">The token could not be read.</exception>
    public static unsafe bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        uint elevation = 0;

        if (!NativeMethods.GetTokenInformation(identity.AccessToken, NativeMethods.TokenElevation, &elevation, sizeof(uint), out _))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        return elevation != 0;
    }

    /// <summary>The session a process runs in.</summary>
    /// <exception cref="Win32Exception">Windows could not say.</exception>
    internal static int SessionOf(int processId)
    {
        if (!NativeMethods.ProcessIdToSessionId((uint)processId, out uint session))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        return (int)session;
    }

    /// <summary>
    /// The user another process runs as, or <see langword="null"/> when its
    /// token cannot be read — which callers treat as "not the same user".
    /// </summary>
    internal static unsafe string? UserSidOf(int processId)
    {
        nint process = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)processId);

        if (process == 0)
        {
            return null;
        }

        using var processHandle = new SafeProcessHandle(process, ownsHandle: true);

        if (!NativeMethods.OpenProcessToken(processHandle, NativeMethods.TOKEN_QUERY, out nint token))
        {
            return null;
        }

        using var tokenHandle = new SafeAccessTokenHandle(token);

        // TOKEN_USER is a SID_AND_ATTRIBUTES whose SID points into the same
        // buffer; 256 bytes holds the largest SID with room to spare.
        byte* buffer = stackalloc byte[256];

        if (!NativeMethods.GetTokenInformation(tokenHandle, NativeMethods.TokenUser, buffer, 256, out _))
        {
            return null;
        }

        return new SecurityIdentifier(*(nint*)buffer).Value;
    }
}
