using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Controls;
using Noto.Core.Links;
using Noto.Platform.Windows;
using Windows.ApplicationModel.DataTransfer;

namespace Noto;

/// <summary>
/// Opening a note's links (ADR-019 §2): checked, asked about when it must be, reported when it cannot be. And a card's
/// colour swatch, which copies its code (#257).
/// </summary>
public sealed partial class MainWindow
{
    private void OnEditorLinkOpenRequested(object? sender, string destination) => QueueOpenLink(destination);

    /// <summary>
    /// A card's swatch was pressed (#257; parity J17): its colour, already in the chosen format, goes to the clipboard and
    /// the stack's status line says what was copied, politely, as Copy link does. A clipboard another app holds open
    /// copies nothing, and says so.
    /// </summary>
    private void CopyColor(string value)
    {
        try
        {
            var package = new DataPackage();
            package.SetText(value);
            Clipboard.SetContent(package);
        }
        catch (COMException)
        {
            ShowNoteNotice("Nothing was copied: another app is using the clipboard. Try again.");
            return;
        }

        ShowNoteStatus($"Copied {value}");
    }

    /// <summary>How file links are drawn (J16, #196): every editor from now, and the one open now at once.</summary>
    internal void ApplyFileLinks(FileLinkStyle style)
    {
        NoteEditorHost.FileLinks = style;
        _editor?.ShowFileLinks();
    }

    /// <summary>
    /// Opens a link once the input that asked for it is done. Measured (Invoke-Links.ps1 case 2): the shell launch
    /// run inside the editor's key handler left the UI thread stuck, the window half hidden and the hotkey unanswered;
    /// queued, it runs between inputs, as a card's link click already did.
    /// </summary>
    internal void QueueOpenLink(string destination) => _ = DispatcherQueue.TryEnqueueGuarded(() => OpenLink(destination));

    /// <summary>
    /// Opens <paramref name="destination"/> from the editor or a card. A program, script, shortcut or unknown type asks
    /// first, Cancel being the default; a refused link or a target that is gone says so and opens nothing.
    /// </summary>
    private async void OpenLink(string destination)
    {
        LinkCheck check = ShellLaunch.Inspect(destination);

        if (check.Kind == LinkKind.Refused)
        {
            ShowNoteNotice("Noto doesn't open this kind of link.");
            return;
        }

        if (!check.Exists)
        {
            ShowNoteNotice(check.IsFolder ? "That folder is no longer there." : "That file or folder is no longer there.");
            return;
        }

        bool confirmed = false;
        if (check.AsksFirst)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,

                // A dialog takes the window content's theme, which is pinned Dark (F2): it takes the drawer's instead.
                RequestedTheme = Drawer.ActualTheme,
                Title = $"Open {Path.GetFileName(check.Address)}?",
                Content = "It is a program, a script or a shortcut, or a kind of file Noto doesn't know. Open it only if you trust it.",
                PrimaryButtonText = "Open",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
            };

            confirmed = await dialog.ShowAsync() == ContentDialogResult.Primary;
            if (!confirmed)
            {
                return;
            }
        }

        switch (ShellLaunch.OpenLink(destination, confirmed))
        {
            case LinkOpening.Missing:
                ShowNoteNotice("That file or folder is no longer there.");
                break;
            case LinkOpening.Failed:
                ShowNoteNotice("Windows couldn't open that link.");
                break;
        }
    }
}
