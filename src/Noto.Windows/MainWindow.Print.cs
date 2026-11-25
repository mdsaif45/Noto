using Microsoft.UI.Xaml.Input;
using Windows.Graphics.Printing;

namespace Noto;

/// <summary>
/// Printing a note (#248; B21, G25, I6): Ctrl+P on the card being edited or the focused card, and Print… on the card's
/// menu, open Windows' print dialog (<see cref="NotePrinter"/>). The drawer stays open while the dialog is up.
/// </summary>
public sealed partial class MainWindow
{
    private NotePrinter? _printer;

    /// <summary>
    /// True while Windows' print dialog is up, false when its task completes or it never opened: the composition root
    /// holds the drawer open against losing activation for that long (WindowCoordinator.HeldOpen).
    /// </summary>
    public event EventHandler<bool>? HoldOpenChanged;

    /// <summary>Ctrl+P (#248): the current card, from the card or its editor, as Ctrl+Shift+M moves it.</summary>
    private void OnPrintAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;

        if (CurrentCard() is { } id)
        {
            QueuePrint(id);
        }
    }

    /// <summary>
    /// Prints once the input that asked for it is done: a dialog opened inside a key handler can leave the UI thread
    /// stuck, as a link's launch did (Invoke-Links.ps1 case 2).
    /// </summary>
    private void QueuePrint(string id) => _ = DispatcherQueue.TryEnqueueGuarded(() => PrintCard(id));

    /// <summary>
    /// Opens the print dialog for the card's note as it is on screen: the editor's text when it is the card being edited,
    /// unsaved or not, since printing stores nothing. Where printing isn't available, says so and does nothing else.
    /// </summary>
    private async void PrintCard(string id)
    {
        if (_noteState != NoteState.Loaded || Notes.FirstOrDefault(n => n.Id == id) is not { } card || _printer is { Busy: true })
        {
            return;
        }

        string source = card.Content;
        if (_stack.Editing?.Value == id)
        {
            _editor?.ReadBack();
            source = _stack.Draft;
        }

        if (_printer is null)
        {
            _printer = new NotePrinter(this, WorkspaceRoot);
            _printer.Completed += OnPrinted;
        }

        // Held before the dialog opens: it may take activation the moment it shows.
        HoldOpenChanged?.Invoke(this, true);

        if (!await _printer.Print(card.DisplayTitle, source, card.Mode))
        {
            HoldOpenChanged?.Invoke(this, false);
            ShowNoteStatus("Printing isn't available on this PC.");
        }
    }

    /// <summary>The dialog closed: the drawer may hide again, a failure is said, and focus goes back to the note.</summary>
    private void OnPrinted(object? sender, PrintTaskCompletion completion)
    {
        HoldOpenChanged?.Invoke(this, false);

        if (completion == PrintTaskCompletion.Failed)
        {
            ShowNoteNotice("The note couldn't be printed.");
        }

        if (_noteState != NoteState.Loaded)
        {
            return;
        }

        if (_editor is not null)
        {
            _editor.TakeFocus();
        }
        else if (CurrentCard() is { } id)
        {
            FocusNoteRow(id);
        }
    }
}
