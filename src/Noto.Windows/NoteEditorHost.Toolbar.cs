using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

namespace Noto;

/// <summary>
/// The quick-formatting toolbar (F8, #151): shown above selected text, once a selection is made with the pointer or
/// the keys, and gone when the selection is. Its buttons run the same commands as the keys and the Format menu
/// (ADR-017 §3), and never take focus from the text. Settings can turn it off.
/// </summary>
public sealed partial class NoteEditorHost
{
    /// <summary>The space between the toolbar and the selected text.</summary>
    private const double ToolbarGap = 4;

    // The left button is down in the text: a selection is still being made, and the toolbar waits for it.
    private bool _selecting;

    /// <summary>Whether the toolbar is shown on a selection (the setting <c>editor.format-toolbar</c>); set by the app.</summary>
    internal static bool ShowsFormatToolbar { get; set; } = true;

    /// <summary>Called once the editor's events are wired.</summary>
    private void AttachToolbar()
    {
        NoteEditor.AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) =>
        {
            _selecting = true;
            HideToolbar();
        }), handledEventsToo: true);
        NoteEditor.LostFocus += (_, _) => HideToolbar();
        NoteEditor.EffectiveViewportChanged += (_, _) => PlaceToolbar();
        Unloaded += (_, _) => HideToolbar();
    }

    /// <summary>The selection changed by the keys, or by a command: the toolbar follows it, or goes with it.</summary>
    private void OnSelectionForToolbar()
    {
        if (!_selecting)
        {
            ShowToolbar();
        }
    }

    private void ShowToolbar()
    {
        if (!ShowsFormatToolbar || _selection.IsCaret || NoteEditor.FocusState == FocusState.Unfocused)
        {
            HideToolbar();
            return;
        }

        FormatBar.IsOpen = true;
        PlaceToolbar();
    }

    private void HideToolbar() => FormatBar.IsOpen = false;

    /// <summary>
    /// Above the selection's first line, inside the editor's width; below its last line when there is no room above.
    /// RichEdit's client coordinates start inside the border and padding, and scroll with the note.
    /// </summary>
    private void PlaceToolbar()
    {
        if (!FormatBar.IsOpen)
        {
            return;
        }

        NoteEditor.Document.Selection.GetRect(PointOptions.ClientCoordinates, out Rect selected, out _);
        FormatToolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Size bar = FormatToolbar.DesiredSize;

        double left = selected.X + NoteEditor.BorderThickness.Left + NoteEditor.Padding.Left;
        double top = selected.Y - ScrolledBy + NoteEditor.BorderThickness.Top + NoteEditor.Padding.Top;
        double above = top - bar.Height - ToolbarGap;
        bool roomAbove = TransformToVisual(null).TransformPoint(new Point(0, above)).Y >= 0;

        FormatBar.HorizontalOffset = Math.Max(0, Math.Min(left, ActualWidth - bar.Width));
        FormatBar.VerticalOffset = roomAbove ? above : top + selected.Height + ToolbarGap;
    }

    private void OnToolbarClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string name } && Enum.TryParse(name, out EditorCommand command))
        {
            _ = Run(command);
            ShowToolbar();
        }
    }
}
