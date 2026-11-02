using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Noto.Core.Notes;
using Windows.System;
using Windows.UI.Core;

namespace Noto;

/// <summary>Why the editor asks to be left (ADR-016 §5).</summary>
internal enum EditorLeave
{
    /// <summary>Escape: the stored Escape behaviour decides.</summary>
    Escape,

    /// <summary>Ctrl+Alt+Down: edit the next card (G12).</summary>
    Next,

    /// <summary>Ctrl+Alt+Up: edit the previous card (G11).</summary>
    Previous,
}

/// <summary>Where the caret goes when editing starts.</summary>
internal enum CaretPlacement
{
    Start,
    End,
}

/// <summary>
/// The editor host (ADR-016 §3): the one place a note's text is edited. Nothing else references its inner control.
/// </summary>
public sealed partial class NoteEditorHost : UserControl
{
    public NoteEditorHost()
    {
        InitializeComponent();

        // Ctrl+, opens Settings from the editor too. Measured: the TextBox marks Ctrl+, handled in its own KeyDown, so
        // no ancestor's accelerator ever saw it. Its accelerator pass runs first, so the chord is handed to the window
        // root's accelerators from there: the same accelerator, the same Settings. Nothing else is touched.
        NoteEditor.ProcessKeyboardAccelerators += (_, args) =>
        {
            if (args.Key == (VirtualKey)0xBC && args.Modifiers == VirtualKeyModifiers.Control && XamlRoot?.Content is UIElement root)
            {
                root.TryInvokeKeyboardAccelerator(args);
            }
        };
    }

    /// <summary>The note shown; <see langword="null"/> before <see cref="Load"/>.</summary>
    internal NoteId? Note { get; private set; }

    /// <summary>The note's markdown source as edited now (ADR-004: markdown is the truth).</summary>
    internal string Markdown => NoteEditor.Text;

    /// <summary>The source changed.</summary>
    internal event EventHandler? ContentChanged;

    /// <summary>The user asked to leave the editor.</summary>
    internal event EventHandler<EditorLeave>? LeaveRequested;

    /// <summary>Shows <paramref name="markdown"/> as <paramref name="note"/>'s source.</summary>
    internal void Load(NoteId note, string markdown)
    {
        Note = note;
        NoteEditor.Text = markdown;
    }

    /// <summary>Takes keyboard focus with the caret at <paramref name="placement"/>.</summary>
    internal void PlaceCaret(CaretPlacement placement)
    {
        _ = NoteEditor.Focus(FocusState.Programmatic);
        NoteEditor.Select(placement == CaretPlacement.Start ? 0 : NoteEditor.Text.Length, 0);
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e) => ContentChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Escape and Ctrl+Alt+Up/Down, before the TextBox: it claims the arrows for the caret, so a later handler never
    /// sees them.
    /// </summary>
    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        EditorLeave? leave = e.Key switch
        {
            VirtualKey.Escape => EditorLeave.Escape,
            VirtualKey.Up when IsDown(VirtualKey.Control) && IsDown(VirtualKey.Menu) => EditorLeave.Previous,
            VirtualKey.Down when IsDown(VirtualKey.Control) && IsDown(VirtualKey.Menu) => EditorLeave.Next,
            _ => null,
        };

        if (leave is { } request)
        {
            e.Handled = true;
            LeaveRequested?.Invoke(this, request);
        }
    }

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
}
