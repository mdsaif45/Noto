using System.ComponentModel;
using System.Diagnostics;
using Noto.Core.Links;

namespace Noto.Platform.Windows;

/// <summary>
/// Hands something to the Windows shell to open: the data folder in Explorer, the Startup apps page of Windows Settings
/// (Slice 1), and a note's links (ADR-019 §2), only through <see cref="OpenLink"/>.
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

    /// <summary>What opening <paramref name="destination"/> would do, asked before anything is opened.</summary>
    public static LinkCheck Inspect(string destination)
    {
        (LinkKind kind, string? address) = LinkTarget.Resolve(destination);

        if (kind != LinkKind.Local || address is null)
        {
            return new LinkCheck(kind, address, IsFolder: false, Exists: kind != LinkKind.Refused, AsksFirst: false);
        }

        bool folder = Directory.Exists(address);
        bool exists = folder || File.Exists(address);
        return new LinkCheck(kind, address, folder, exists, AsksFirst: exists && !folder && LinkTarget.NeedsConfirmation(address));
    }

    /// <summary>
    /// Opens <paramref name="destination"/>, checked again here so no caller can skip ADR-019's allow-list: a refused
    /// link, a local target that is gone, or one that asks first and was not confirmed, is never opened.
    /// </summary>
    /// <param name="destination">The link's destination as the note writes it.</param>
    /// <param name="confirmed">The user agreed to open a program, script or unknown type.</param>
    public static LinkOpening OpenLink(string destination, bool confirmed = false)
    {
        LinkCheck check = Inspect(destination);

        LinkOpening outcome = check switch
        {
            { Kind: LinkKind.Refused } or { Address: null } => LinkOpening.Refused,
            { Exists: false } => LinkOpening.Missing,
            { AsksFirst: true } when !confirmed => LinkOpening.NotConfirmed,
            _ => Open(check.Address) ? LinkOpening.Opened : LinkOpening.Failed,
        };

        // The kind and the outcome only: never the address (quality gates: no user paths or content in traces).
        Trace.WriteLine($"Noto.Link open kind={check.Kind} folder={check.IsFolder} outcome={outcome}");
        return outcome;
    }

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

/// <summary>What a link is and what opening it would do (<see cref="ShellLaunch.Inspect"/>).</summary>
/// <param name="Kind">Its kind by ADR-019's allow-list.</param>
/// <param name="Address">What would be opened; <see langword="null"/> when refused.</param>
/// <param name="IsFolder">A local folder.</param>
/// <param name="Exists">A local target is there (always true for a web or mail address).</param>
/// <param name="AsksFirst">A local program, script, shortcut or unknown type: the user is asked first.</param>
public readonly record struct LinkCheck(LinkKind Kind, string? Address, bool IsFolder, bool Exists, bool AsksFirst);

/// <summary>What <see cref="ShellLaunch.OpenLink"/> did.</summary>
public enum LinkOpening
{
    /// <summary>The shell accepted it.</summary>
    Opened,

    /// <summary>Not a link Noto opens.</summary>
    Refused,

    /// <summary>A local file or folder that is no longer there.</summary>
    Missing,

    /// <summary>A target that asks first, opened without the user's agreement: nothing was opened.</summary>
    NotConfirmed,

    /// <summary>The shell refused it.</summary>
    Failed,
}
