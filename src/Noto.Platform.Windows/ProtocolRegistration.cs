using System.Security;
using Microsoft.Win32;

namespace Noto.Platform.Windows;

/// <summary>
/// The current user's registry, the only one <see cref="ProtocolRegistration"/> writes (#192): the seam a test
/// replaces, so no test touches the real registry. Keys are relative to <c>HKEY_CURRENT_USER</c>; there is no way to
/// name <c>HKEY_LOCAL_MACHINE</c> through it.
/// </summary>
internal interface IUserRegistry
{
    /// <summary>A string value; <see langword="null"/> when the key or value is missing or not a string.</summary>
    /// <param name="key">The key, under <c>HKEY_CURRENT_USER</c>.</param>
    /// <param name="name">The value's name; empty for the key's default value.</param>
    string? Read(string key, string name);

    /// <summary>Writes a string value, creating the key.</summary>
    void Write(string key, string name, string value);

    /// <summary>Deletes a key and everything under it; nothing when it is missing.</summary>
    void DeleteTree(string key);
}

/// <summary>
/// The <c>noto://</c> scheme's registration for an installed Noto (#192, ADR-022): <c>HKCU\Software\Classes\noto</c>,
/// marked <c>URL Protocol</c>, opening the installed <c>Noto.exe "%1"</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Only Velopack's hooks call it</b> (ADR-008): install and update register this copy, uninstall removes the
/// registration only when it opens this copy. A build or a portable copy runs no hook, so it never registers, and
/// its command line still works. Per user, never machine-wide: nothing here reaches <c>HKEY_LOCAL_MACHINE</c>.
/// </para>
/// <para>
/// <b>Never fatal.</b> A hook that cannot write the registry leaves Noto installed without the scheme; every failure
/// reads as "not done".
/// </para>
/// </remarks>
public sealed class ProtocolRegistration
{
    /// <summary>The scheme's key, under <c>HKEY_CURRENT_USER</c>.</summary>
    internal const string SchemeKey = @"Software\Classes\noto";

    internal const string CommandKey = SchemeKey + @"\shell\open\command";

    private readonly string _executable;
    private readonly IUserRegistry _registry;

    internal ProtocolRegistration(string executable, IUserRegistry registry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        _executable = Path.GetFullPath(executable);
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <summary>The registration for this executable, in the current user's real registry.</summary>
    public static ProtocolRegistration ForCurrentUser(string executable) => new(executable, new CurrentUserRegistry());

    /// <summary>Whether the scheme opens this executable now.</summary>
    public bool IsThis()
    {
        try
        {
            return string.Equals(_registry.Read(CommandKey, string.Empty), CommandFor(_executable), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (IsAccessFailure(ex))
        {
            return false;
        }
    }

    /// <summary>Makes the scheme open this executable: what an install and an update do.</summary>
    /// <returns>Whether it opens this executable afterwards.</returns>
    public bool Register()
    {
        try
        {
            _registry.Write(SchemeKey, string.Empty, "URL:Noto");
            _registry.Write(SchemeKey, "URL Protocol", string.Empty);
            _registry.Write(CommandKey, string.Empty, CommandFor(_executable));
        }
        catch (Exception ex) when (IsAccessFailure(ex))
        {
            // Read back below: what is there is the answer.
        }

        return IsThis();
    }

    /// <summary>
    /// Removes the scheme only when it opens this executable: what an uninstall does. One that opens another copy of
    /// Noto, or another program, is left alone.
    /// </summary>
    /// <returns>Whether a registration of this executable was removed.</returns>
    public bool RemoveIfThis()
    {
        if (!IsThis())
        {
            return false;
        }

        try
        {
            _registry.DeleteTree(SchemeKey);
            return true;
        }
        catch (Exception ex) when (IsAccessFailure(ex))
        {
            return false;
        }
    }

    /// <summary>The registered command: the quoted full path and the quoted link. Nothing else is ever passed.</summary>
    internal static string CommandFor(string executable) => $"\"{executable}\" \"%1\"";

    private static bool IsAccessFailure(Exception ex) =>
        ex is UnauthorizedAccessException or SecurityException or IOException;

    /// <summary>The real one: <see cref="Registry.CurrentUser"/>, and nothing else.</summary>
    private sealed class CurrentUserRegistry : IUserRegistry
    {
        public string? Read(string key, string name)
        {
            using RegistryKey? opened = Registry.CurrentUser.OpenSubKey(key, writable: false);
            return opened?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        }

        public void Write(string key, string name, string value)
        {
            using RegistryKey created = Registry.CurrentUser.CreateSubKey(key, writable: true);
            created.SetValue(name, value, RegistryValueKind.String);
        }

        public void DeleteTree(string key) => Registry.CurrentUser.DeleteSubKeyTree(key, throwOnMissingSubKey: false);
    }
}
