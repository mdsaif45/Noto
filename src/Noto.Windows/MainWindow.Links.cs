using Microsoft.UI.Xaml.Controls;
using Noto.Core.Links;
using Noto.Platform.Windows;

namespace Noto;

/// <summary>Opening a note's links (ADR-019 §2): checked, asked about when it must be, reported when it cannot be.</summary>
public sealed partial class MainWindow
{
    private void OnEditorLinkOpenRequested(object? sender, string destination) => QueueOpenLink(destination);

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
