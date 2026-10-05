using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Noto.Platform.Windows;

/// <summary>
/// Which running Noto owns a data root: the current user plus the root's
/// physical location (A17, ADR-013).
/// </summary>
/// <remarks>
/// <para>
/// <b>Keyed by the data root, not the executable.</b> One database, one
/// process — whichever build launched it. Two paths that reach the same
/// folder through a junction, a symbolic link, a <c>subst</c> drive, a short
/// name or a different case are the same root, so they produce the same key.
/// </para>
/// <para>
/// The path is resolved by Windows (<c>GetFinalPathNameByHandle</c> on the
/// open folder), normalised — no <c>\\?\</c> prefix, no trailing separator,
/// upper case — and hashed with SHA-256, keeping 32 hex characters. The
/// hash keeps the names short and the folder path out of the object
/// namespace.
/// </para>
/// </remarks>
public sealed class InstanceKey
{
    private const string Prefix = "Noto.Instance";

    /// <summary>How many hex characters of the SHA-256 are kept.</summary>
    internal const int HashLength = 32;

    private InstanceKey(string userSid, string pathHash)
    {
        UserSid = userSid;
        PathHash = pathHash;
    }

    /// <summary>The user the key belongs to, as a SID string.</summary>
    public string UserSid { get; }

    /// <summary>The data root's normalised physical path, hashed.</summary>
    public string PathHash { get; }

    /// <summary>The ownership mutex: <c>Global\Noto.Instance.&lt;SID&gt;.&lt;hash&gt;</c>.</summary>
    public string MutexName => $@"Global\{Prefix}.{UserSid}.{PathHash}";

    /// <summary>The activation pipe, under <c>\\.\pipe\</c>: <c>Noto.Instance.&lt;SID&gt;.&lt;hash&gt;</c>.</summary>
    public string PipeName => $"{Prefix}.{UserSid}.{PathHash}";

    /// <summary>The key for an existing data-root folder, for the current user.</summary>
    /// <exception cref="Win32Exception">The folder could not be opened or resolved.</exception>
    public static InstanceKey ForDataRoot(string dataRoot) =>
        From(ProcessIdentity.CurrentUserSid(), PhysicalPathOf(dataRoot));

    /// <summary>The key for a user and an already-resolved physical path.</summary>
    internal static InstanceKey From(string userSid, string physicalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userSid);

        return new InstanceKey(userSid, HashOf(Normalize(physicalPath)));
    }

    /// <summary>
    /// Where a folder really is: junctions, symbolic links and <c>subst</c>
    /// drives followed, short names expanded, the case as stored.
    /// </summary>
    /// <exception cref="Win32Exception">The folder could not be opened or resolved.</exception>
    internal static unsafe string PhysicalPathOf(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        // No access rights are needed to ask a handle its name;
        // FILE_FLAG_BACKUP_SEMANTICS is what lets CreateFile open a folder.
        nint raw = NativeMethods.CreateFile(
            Path.GetFullPath(directory),
            0,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE | NativeMethods.FILE_SHARE_DELETE,
            0,
            NativeMethods.OPEN_EXISTING,
            NativeMethods.FILE_FLAG_BACKUP_SEMANTICS,
            0);

        if (raw == NativeMethods.INVALID_HANDLE_VALUE)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        using var handle = new SafeFileHandle(raw, ownsHandle: true);

        const int Capacity = 32_768;
        char[] buffer = new char[Capacity];
        uint length;

        fixed (char* chars = buffer)
        {
            length = NativeMethods.GetFinalPathNameByHandle(handle, chars, Capacity, NativeMethods.FILE_NAME_NORMALIZED | NativeMethods.VOLUME_NAME_DOS);
        }

        if (length == 0 || length >= Capacity)
        {
            throw new Win32Exception(length == 0 ? Marshal.GetLastPInvokeError() : NativeMethods.ERROR_INSUFFICIENT_BUFFER);
        }

        return new string(buffer, 0, (int)length);
    }

    /// <summary>
    /// The form two equal roots share: no <c>\\?\</c> prefix, back slashes,
    /// no trailing separator (a drive root keeps its own), invariant upper case.
    /// </summary>
    internal static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string normalized = path.Replace('/', '\\');

        if (normalized.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            normalized = @"\\" + normalized[@"\\?\UNC\".Length..];
        }
        else if (normalized.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            normalized = normalized[@"\\?\".Length..];
        }

        string trimmed;

        while ((trimmed = Path.TrimEndingDirectorySeparator(normalized)) != normalized)
        {
            normalized = trimmed;
        }

        return normalized.ToUpperInvariant();
    }

    /// <summary>SHA-256 of the normalised path's UTF-8, as the first 32 upper-case hex characters.</summary>
    internal static string HashOf(string normalizedPath) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath)))[..HashLength];

    public override string ToString() => PipeName;
}
