using System.Diagnostics;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Noto.Core.Images;
using Windows.System;

namespace Noto;

/// <summary>
/// The picture preview's surface (D35, G44, #268): one element in UI Automation, PicturePreview, named for the picture
/// it shows. A UserControl has no automation peer of its own (as SettingsPage), and a focusable one is what the keyboard
/// moves into.
/// </summary>
internal sealed partial class PicturePreview : UserControl
{
    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);
}

/// <summary>
/// Space previews a note's pictures in Noto's own window (D35, G44, #268), where SideNotes uses Quick Look: from a card
/// with pictures, its first; from a picture in the editor, that one. Left and Right move through the note's pictures
/// and stop at the ends; Escape or Space closes, with focus back exactly where it was.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// The widest a preview's picture is decoded, in pixels: a huge picture is read at the size it is shown, never at
    /// its own, so it neither stalls nor holds its full size in memory. The drawer is narrower than this anyway.
    /// </summary>
    internal const int PreviewDecodeCap = 4096;

    // The pictures being previewed, the one shown among them; null while the preview is closed.
    private NotePictures? _previewed;

    // What had focus when the preview opened: the card, or the editor with its caret, where closing puts it back.
    private Control? _previewReturnFocus;

    // Counts pictures shown, so a picture still being read when the next is shown, or the preview closed, is dropped.
    private int _previewShown;

    // Escape or Space was pressed in the preview: it closes when the key comes up (OnPictureKeyUp).
    private bool _previewClosing;

    /// <summary>
    /// Opens the preview on <paramref name="pictures"/>' current picture, over the whole drawer, and moves focus into it.
    /// </summary>
    private void OpenPreview(NotePictures pictures)
    {
        if (_previewed is not null)
        {
            return;
        }

        _previewReturnFocus = Content?.XamlRoot is { } root ? FocusManager.GetFocusedElement(root) as Control : null;
        _previewed = pictures;
        _previewClosing = false;
        PreviewSurface.Visibility = Visibility.Visible;
        ShowPicture();

        // After layout: an element made visible this frame cannot take focus until it has been measured.
        _ = DispatcherQueue.TryEnqueueGuarded(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (_previewed is not null)
            {
                _ = PreviewSurface.Focus(FocusState.Programmatic);
            }
        });
    }

    /// <summary>
    /// Closes the preview: focus goes back where it was first, so it never passes through anything behind; then the
    /// picture is released.
    /// </summary>
    private void ClosePreview()
    {
        if (_previewed is null)
        {
            return;
        }

        _previewed = null;
        _previewClosing = false;
        _previewShown++;

        Control? back = _previewReturnFocus;
        _previewReturnFocus = null;
        if (back is not { IsLoaded: true, Visibility: Visibility.Visible } || !back.Focus(FocusState.Programmatic))
        {
            MoveFocusToNotes();
        }

        PreviewSurface.Visibility = Visibility.Collapsed;
        PreviewImage.Source = null;
        Trace.WriteLine("Noto.Preview closed");
    }

    /// <summary>
    /// Shows the current picture: its caption as the surface's name, where it is among the note's pictures, then the
    /// picture itself once read, or a notice when it can't be.
    /// </summary>
    /// <remarks>
    /// Only a file Noto keeps resolves (<see cref="IImageStore.PathOf"/>): a picture written as a web address never does,
    /// so it is never fetched (ADR-019, principle 4) and shows the notice. Its size is read from the file's header off
    /// the UI thread; the bitmap is decoded off it too, by the framework, at the width it is shown and at most
    /// <see cref="PreviewDecodeCap"/> pixels.
    /// </remarks>
    private async void ShowPicture()
    {
        if (_previewed is not { } pictures)
        {
            return;
        }

        int shown = ++_previewShown;
        NotePicture picture = pictures.Current;
        AutomationProperties.SetName(PreviewSurface, picture.Name);
        AutomationProperties.SetHelpText(PreviewSurface, pictures.Position);
        AutomationProperties.SetName(PreviewImage, picture.Name);
        PreviewStatus.Text = pictures.Position;
        PreviewImage.Source = null;
        PreviewNotice.Visibility = Visibility.Collapsed;
        PreviewNoticeText.Text = string.Empty;

        // Where, never what: no path, no caption.
        Trace.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Noto.Preview shown index={pictures.Index + 1} count={pictures.All.Count}"));

        string? path = Images?.PathOf(picture.Source);
        (int Width, int Height)? size = path is null ? null : await ImageFiles.SizeOf(path);
        if (shown != _previewShown)
        {
            return;
        }

        if (path is null || size is not { } pixels)
        {
            ShowPictureNotice();
            return;
        }

        BitmapImage bitmap = ImageFiles.Bitmap(path, PreviewWidth(pixels.Width, pixels.Height));
        bitmap.ImageFailed += (_, _) =>
        {
            if (shown == _previewShown)
            {
                PreviewImage.Source = null;
                ShowPictureNotice();
            }
        };
        PreviewImage.Source = bitmap;
    }

    /// <summary>
    /// The width, in view pixels, a picture of <paramref name="width"/> × <paramref name="height"/> is decoded at: fitted
    /// to the drawer, never enlarged (<see cref="ImageSize.Fit"/>), and never more than <see cref="PreviewDecodeCap"/>
    /// screen pixels wide.
    /// </summary>
    private double PreviewWidth(int width, int height)
    {
        double scale = Content?.XamlRoot?.RasterizationScale ?? 1;
        (double fitted, _) = ImageSize.Fit(width, height, WorkspaceContent.ActualWidth, WorkspaceContent.ActualHeight);
        return Math.Min(fitted, PreviewDecodeCap / scale);
    }

    /// <summary>A missing or unreadable picture says so, in place of an empty frame; the arrows still move on.</summary>
    private void ShowPictureNotice()
    {
        PreviewNoticeText.Text = "This picture can't be shown";
        PreviewNotice.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// The preview has the keyboard while it is open: every key is its own, so nothing behind it acts and Tab never
    /// leaves it. Its keys are acted on in <see cref="OnPictureAccelerators"/>, which runs first for every key: handling
    /// it there is what keeps Noto's chords off, and WinUI then raises no KeyDown. This only marks anything that does
    /// arrive as handled.
    /// </summary>
    private void OnPictureKeyDown(object sender, KeyRoutedEventArgs e) => e.Handled = true;

    /// <summary>Left and Right move through the pictures and stop at the ends; Escape and Space close it when they come up.</summary>
    private void PreviewKey(VirtualKey key)
    {
        if (_previewed is not { } pictures)
        {
            return;
        }

        switch (key)
        {
            case VirtualKey.Escape or VirtualKey.Space:
                _previewClosing = true;
                break;

            case VirtualKey.Left or VirtualKey.Right:
                NotePictures next = pictures.Step(key == VirtualKey.Left ? -1 : 1);
                if (next.Index != pictures.Index)
                {
                    _previewed = next;
                    ShowPicture();
                }

                break;
        }
    }

    /// <summary>
    /// Escape or Space closes the preview when it comes up, not down: the key's character, and its release, have then
    /// both reached the preview, so neither types a space in the editor focus returns to, nor presses a button. A key
    /// that opened the preview comes up here without having gone down here, and leaves it open.
    /// </summary>
    private void OnPictureKeyUp(object sender, KeyRoutedEventArgs e)
    {
        e.Handled = true;
        if (_previewClosing && e.Key is VirtualKey.Escape or VirtualKey.Space)
        {
            ClosePreview();
        }
    }

    /// <summary>
    /// Runs before any accelerator, for every key pressed while focus is in the preview. Noto's own chords (Ctrl+W,
    /// Ctrl+N, Ctrl+F…) would act on what is behind it, so every key is handled here, and a key without modifiers is
    /// the preview's own (<see cref="PreviewKey"/>).
    /// </summary>
    private void OnPictureAccelerators(UIElement sender, ProcessKeyboardAcceleratorEventArgs args)
    {
        if (_previewed is null)
        {
            return;
        }

        args.Handled = true;
        if (args.Modifiers == VirtualKeyModifiers.None)
        {
            PreviewKey(args.Key);
        }
    }

    private void OnEditorPictureRequested(object? sender, NotePictures pictures) => OpenPreview(pictures);
}
