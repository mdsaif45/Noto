using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Noto.Core.Editing;
using Noto.Core.Links;
using Noto.Infrastructure.Portability;
using Noto.Platform.Windows;
using Windows.System;

namespace Noto;

/// <summary>
/// File and folder shortcuts in the editor (D34, I3, J16; #196). A line that is one file link keeps its source, as an
/// image's line does (ADR-017 §1, ADR-019 §4): RichEdit cannot hold an object, so the line is indented to make room and
/// an overlay draws the shell's own icon before the name and, in the Standard style, the type and size in the space
/// given below it. A double-click on the line opens it through the same checked path as Ctrl+Click.
/// </summary>
public sealed partial class NoteEditorHost
{
    /// <summary>More shortcuts than this in one note are left as links: the overlay is per shortcut.</summary>
    private const int ShortcutLimit = 50;

    /// <summary>A Standard shortcut's icon: as tall as its name and its details line together.</summary>
    private const double StandardIcon = 32;

    /// <summary>A Compact shortcut's icon: as tall as its name.</summary>
    private const double CompactIcon = 16;

    /// <summary>The space between a shortcut's icon and its name.</summary>
    private const double ShortcutGap = 6;

    /// <summary>The height of a Standard shortcut's details line, given below its name.</summary>
    private const double DetailsHeight = 16;

    /// <summary>Each target's icon and details, by path and icon size: <see langword="null"/> while they are read.</summary>
    private readonly Dictionary<(string Path, bool Large), Shortcut?> _shortcuts = [];

    /// <summary>How file links are drawn (the setting <c>editor.file-links</c>); set by the window.</summary>
    internal static FileLinkStyle FileLinks { get; set; }

    /// <summary>What a picture dropped or pasted becomes (the setting <c>importing.picture</c>); set by the app.</summary>
    internal static PictureDrop PictureDrops { get; set; }

    /// <summary>What a text file dropped or pasted becomes (the setting <c>importing.text-file</c>); set by the app.</summary>
    internal static TextFileDrop TextFileDrops { get; set; }

    /// <summary>The file links' style changed: drawn again.</summary>
    internal void ShowFileLinks() => QueueTaskMarks();

    /// <summary>
    /// Each shortcut line given room for its icon (an indent) and, Standard, for its details (space below), then drawn.
    /// Where a line's room changes, they are placed again once that is laid out, as the images are.
    /// </summary>
    private void PlaceShortcuts()
    {
        var shortcuts = FileShortcuts.In(_document).Take(ShortcutLimit + 1).ToList();
        if (shortcuts.Count == 0 || shortcuts.Count > ShortcutLimit)
        {
            return;
        }

        bool large = FileLinks == FileLinkStyle.Standard;
        double size = large ? StandardIcon : CompactIcon;
        float indent = (float)((size + ShortcutGap) * 0.75);
        // The paragraph spacing below the line (#234) stays below the details: the line is given both.
        double after = NoteSpacing.SpaceAfter(FontSize);
        float below = (float)(((large ? DetailsHeight : 0) + after) * 0.75);
        bool roomChanged = false;

        foreach (FileShortcut shortcut in shortcuts)
        {
            ITextParagraphFormat line = NoteEditor.Document.GetRange(shortcut.Line.Start, shortcut.Line.End).ParagraphFormat;
            if (Math.Abs(line.LeftIndent - indent) > 0.5f || Math.Abs(line.SpaceAfter - below) > 0.5f)
            {
                Set(() =>
                {
                    line.SetIndents(0, indent, 0);
                    line.SpaceAfter = below;
                });
                roomChanged = true;
            }
        }

        if (roomChanged)
        {
            QueueTaskMarks();
            return;
        }

        // RichEdit measures from inside the control's border and padding, as the checkbox overlay does.
        var toMarks = NoteEditor.TransformToVisual(EditorRoot);
        double left = NoteEditor.BorderThickness.Left + NoteEditor.Padding.Left;
        double top = NoteEditor.BorderThickness.Top + NoteEditor.Padding.Top;
        var caption = (double)Application.Current.Resources["FontSizeCaption"];

        foreach (FileShortcut shortcut in shortcuts)
        {
            NoteEditor.Document.GetRange(shortcut.Name.Start, shortcut.Name.Start + 1).GetRect(PointOptions.ClientCoordinates, out Windows.Foundation.Rect name, out _);
            if (name.Height <= 0 || Described(shortcut.Path, large) is not { } described)
            {
                continue;
            }

            // The icon in the indent, at the top of a Standard line, centred on a Compact one's text.
            FrameworkElement icon = described.Icon is { } picture
                ? new Image { Source = picture, Width = size, Height = size }
                : new FontIcon { Glyph = described.Folder ? "" : "", FontSize = Math.Round(size * 0.8), Width = size, Height = size, Foreground = NoteEditor.MarkupBrush }; // Segoe Fluent Icons: Folder, Page
            Place(icon, toMarks.TransformPoint(new Windows.Foundation.Point(left, name.Y + top + (large ? 0 : (TextHeight(name, shortcut.Line.End, after) - size) / 2))));

            if (large)
            {
                // The details in the space below the name's last line: that line's rectangle takes the space in (measured
                // for images), so they sit at its bottom.
                NoteEditor.Document.GetRange(shortcut.Line.End - 1, shortcut.Line.End).GetRect(PointOptions.ClientCoordinates, out Windows.Foundation.Rect last, out _);
                var details = new TextBlock
                {
                    Text = described.Details,
                    FontSize = caption,
                    Foreground = NoteEditor.MarkupBrush,
                    Height = DetailsHeight,
                    MaxWidth = Math.Max(0, NoteEditor.ActualWidth - name.X - (2 * left)),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                Place(details, toMarks.TransformPoint(new Windows.Foundation.Point(name.X + left, last.Bottom - after - DetailsHeight + top)));
            }
        }

        void Place(FrameworkElement element, Windows.Foundation.Point at)
        {
            AutomationProperties.SetAccessibilityView(element, AccessibilityView.Raw);
            Canvas.SetLeft(element, at.X);
            Canvas.SetTop(element, at.Y);
            TaskMarks.Children.Add(element);
        }
    }

    /// <summary>The target's icon and details if they have been read; otherwise they are read, and placed again after.</summary>
    private Shortcut? Described(string path, bool large)
    {
        if (_shortcuts.TryGetValue((path, large), out Shortcut? known))
        {
            return known;
        }

        _shortcuts[(path, large)] = null;
        _ = Describe(path, large);
        return null;
    }

    /// <summary>
    /// Reads the shell's icon and type name, whether the target is there and a file's size, off the UI thread: the shell
    /// may be slow, a drive may be asleep. The file itself is never opened.
    /// </summary>
    private async Task Describe(string path, bool large)
    {
        (ShellIcon? icon, bool folder, string details) = await Task.Run(() =>
        {
            ShellIcon? shell = ShellIcons.Of(path, large);
            bool folder = Directory.Exists(path);
            bool exists = folder || File.Exists(path);
            long? bytes = null;
            try
            {
                bytes = exists && !folder ? new FileInfo(path).Length : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Its size is left out; its type still shows.
            }

            return (shell, folder, FileShortcuts.Details(path, exists, folder, shell?.TypeName, bytes));
        });

        _shortcuts[(path, large)] = new Shortcut(icon is null ? null : Bitmap(icon), folder, details);
        QueueTaskMarks();
    }

    private static WriteableBitmap Bitmap(ShellIcon icon)
    {
        var bitmap = new WriteableBitmap(icon.Width, icon.Height);
        icon.Pixels.CopyTo(bitmap.PixelBuffer);
        bitmap.Invalidate();
        return bitmap;
    }

    /// <summary>A shortcut's target as the editor draws it: the shell's icon (none: a glyph), and its details line.</summary>
    private sealed record Shortcut(ImageSource? Icon, bool Folder, string Details);

    /// <summary>
    /// A double-click on a shortcut's line, its icon included, opens it (D34, #196) through the same checked path as
    /// Ctrl+Click (ADR-019 §2): the window checks it is still there and asks before a program, after the input is done.
    /// The word the double-click selected is let go.
    /// </summary>
    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        // With Ctrl, the first click already opened it.
        if (_showMarkup || IsDown(VirtualKey.Control))
        {
            return;
        }

        var point = e.GetPosition(NoteEditor);
        point = new Windows.Foundation.Point(
            point.X - NoteEditor.BorderThickness.Left - NoteEditor.Padding.Left,
            point.Y - NoteEditor.BorderThickness.Top - NoteEditor.Padding.Top + ScrolledBy);
        int at = NoteEditor.Document.GetRangeFromPoint(point, PointOptions.ClientCoordinates).StartPosition;

        if (FileShortcuts.In(_document).Where(s => s.Line.Start <= at && at <= s.Line.End).Select(s => (FileShortcut?)s).FirstOrDefault() is not { } shortcut)
        {
            return;
        }

        e.Handled = true;
        Set(() => NoteEditor.Document.Selection.SetRange(at, at));
        _selection = TextSelection.Caret(at);
        HideToolbar();
        LinkOpenRequested?.Invoke(this, shortcut.Destination);
    }

    /// <summary>
    /// Inserts a text file's text at the caret, one undo step (J30, #196).
    /// </summary>
    /// <returns>Whether it was inserted: a file that is not UTF-8 text, is over 25 MB or can't be read is not.</returns>
    // ponytail: read on the UI thread, bounded by the importer's 25 MB; read it on the pool if a drop of large files stalls.
    private bool InsertTextOf(string path)
    {
        string? text;
        try
        {
            text = MarkdownImporter.Text(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            text = null;
        }

        if (text is null)
        {
            return false;
        }

        string inserted = Normalize(text);
        Apply(new TextEdit(_selection.Start, _selection.Length, inserted, TextSelection.Caret(_selection.Start + inserted.Length)), record: true);
        return true;
    }
}
