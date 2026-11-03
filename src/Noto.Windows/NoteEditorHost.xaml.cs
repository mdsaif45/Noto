using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Noto.Core.Editing;
using Noto.Core.Markdown;
using Noto.Core.Notes;
using Windows.ApplicationModel.DataTransfer;
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

/// <summary>A formatting command: one per key in the editor's chord map and per item in the card's format menu.</summary>
internal enum EditorCommand
{
    Bold,
    Italic,
    Underline,
    Strikethrough,
    Code,
    Link,
    Heading,
    Bullets,
    Numbers,
    Tasks,
    TaskDone,
    CodeBlock,
    Undo,
    Redo,
    ShowMarkup,
}

/// <summary>
/// The editor host (ADR-016 §3, ADR-017): the one place a note's text is edited. Nothing else references its inner
/// control. The markdown source is the truth: the control shows it with its markup hidden, every change is read back
/// from it as a <see cref="TextEdit"/>, and every command is a Core transform applied to it. The host decides no
/// formatting rule.
/// </summary>
public sealed partial class NoteEditorHost : UserControl
{
    private readonly EditHistory _history = new();

    /// <summary>The source as the control holds it, LF line breaks (ADR-017 §1). Always the control's text.</summary>
    private string _text = string.Empty;

    private MarkdownDocument _document = MarkdownDocument.Parse(string.Empty);

    /// <summary>The selection as last seen, before any change being read back.</summary>
    private TextSelection _selection;

    /// <summary>The selection when the change being read back started.</summary>
    private TextSelection _selectionBefore;

    /// <summary>Noto is changing the document itself: what it raises is not the user's edit.</summary>
    private bool _applying;

    /// <summary>The user changed the text and TextChanged has not been handled yet.</summary>
    private bool _changed;

    /// <summary>The text when an IME composition started; <see langword="null"/> when none is open (ADR-017 §4).</summary>
    private string? _composingFrom;

    private TextSelection _composingSelection;

    private bool _showMarkup;

    public NoteEditorHost()
    {
        InitializeComponent();

        // Ctrl+, opens Settings from the editor too. The edit control marks Ctrl+, handled in its own KeyDown, so no
        // ancestor's accelerator ever sees it: its accelerator pass runs first, so the chord is handed to the window
        // root's accelerators from there.
        NoteEditor.ProcessKeyboardAccelerators += (_, args) =>
        {
            if (args.Key == (VirtualKey)0xBC && args.Modifiers == VirtualKeyModifiers.Control && XamlRoot?.Content is UIElement root)
            {
                root.TryInvokeKeyboardAccelerator(args);
            }
        };


        // RichEdit's undo records Noto's restyles (spike finding 8): Noto owns undo.
        NoteEditor.Document.UndoLimit = 0;
        NoteEditor.ValueSource = () => _text;
        NoteEditor.ValueSink = value => Apply(new TextEdit(0, _text.Length, Normalize(value), TextSelection.Caret(Normalize(value).Length)), record: true);

        NoteEditor.TextChanging += (_, e) =>
        {
            if (!_applying && e.IsContentChanging && !_changed)
            {
                _changed = true;
                _selectionBefore = _selection;
            }
        };
        NoteEditor.TextChanged += (_, _) => OnUserChanged();
        NoteEditor.SelectionChanged += (_, _) =>
        {
            if (!_applying)
            {
                _selection = CurrentSelection();
            }
        };
        NoteEditor.Paste += OnPaste;
        NoteEditor.TextCompositionStarted += (_, _) =>
        {
            _composingFrom = _text;
            _composingSelection = _selection;
        };
        NoteEditor.TextCompositionEnded += (_, _) => OnCompositionEnded();
        NoteEditor.PaletteChanged += (_, _) => RestyleAll();
        ActualThemeChanged += (_, _) => RestyleAll();
        NoteEditor.PreviewKeyDown += OnPreviewKeyDown;
        NoteEditor.SizeChanged += (_, _) => QueueTaskMarks();
        NoteEditor.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnPointerReleased), handledEventsToo: true);

        // Caret keys stay in the editor. RichEdit moves the caret on them but, unlike the TextBox it replaced, leaves
        // some unhandled (measured: Ctrl+End), and an unhandled one reaches the stack's ListView, which moves its
        // selection and so edits another card. This runs after RichEdit's own handling, so the caret has moved.
        NoteEditor.KeyDown += (_, e) =>
        {
            if (e.Key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down
                or VirtualKey.Home or VirtualKey.End or VirtualKey.PageUp or VirtualKey.PageDown)
            {
                e.Handled = true;
            }
        };
    }

    /// <summary>The note shown; <see langword="null"/> before <see cref="Load"/>.</summary>
    internal NoteId? Note { get; private set; }

    /// <summary>The note's markdown source as edited now (ADR-004: markdown is the truth), with LF line breaks.</summary>
    internal string Markdown => _text;

    /// <summary>The source changed.</summary>
    internal event EventHandler? ContentChanged;

    /// <summary>The user asked to leave the editor.</summary>
    internal event EventHandler<EditorLeave>? LeaveRequested;

    /// <summary>Shows <paramref name="markdown"/> as <paramref name="note"/>'s source, styled, with nothing to undo.</summary>
    internal void Load(NoteId note, string markdown)
    {
        Note = note;
        _history.Clear();
        _text = Normalize(markdown);
        _document = MarkdownDocument.Parse(_text);

        long started = Stopwatch.GetTimestamp();
        Set(() => NoteEditor.Document.SetText(TextSetOptions.FormatRtf, MarkdownStyling.Rtf(_document, Palette())));
        Trace.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Noto.Editor loaded chars={_text.Length} spans={_document.Spans.Count} ms={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F2}"));
        QueueTaskMarks();

        // What the user waits for: the first frame drawn after the note was set, RichEdit's layout included. Timing only,
        // on the validation channel, never content.
        EventHandler<object>? rendered = null;
        rendered = (_, _) =>
        {
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= rendered;
            Trace.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Noto.Editor rendered chars={_text.Length} ms={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F2}"));
        };
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += rendered;
    }

    /// <summary>Takes keyboard focus with the caret at <paramref name="placement"/>.</summary>
    internal void PlaceCaret(CaretPlacement placement)
    {
        _ = NoteEditor.Focus(FocusState.Programmatic);
        int at = placement == CaretPlacement.Start ? 0 : _text.Length;
        Set(() => NoteEditor.Document.Selection.SetRange(at, at));
        _selection = TextSelection.Caret(at);
    }

    /// <summary>Gives the text keyboard focus without moving the caret.</summary>
    internal void TakeFocus() => _ = NoteEditor.Focus(FocusState.Programmatic);

    /// <summary>Runs <paramref name="command"/>; <see langword="false"/> when it does not apply here.</summary>
    internal bool Run(EditorCommand command)
    {
        Flush();

        switch (command)
        {
            case EditorCommand.Undo:
                return Replay(_history.Undo());
            case EditorCommand.Redo:
                return Replay(_history.Redo());
            case EditorCommand.ShowMarkup:
                _showMarkup = !_showMarkup;
                RestyleAll();
                return true;
        }

        Func<string, TextSelection, TextEdit?> transform = command switch
        {
            EditorCommand.Bold => MarkdownCommands.ToggleStrong,
            EditorCommand.Italic => MarkdownCommands.ToggleEmphasis,
            EditorCommand.Underline => MarkdownCommands.ToggleUnderline,
            EditorCommand.Strikethrough => MarkdownCommands.ToggleStrikethrough,
            EditorCommand.Code => MarkdownCommands.ToggleCode,
            EditorCommand.Link => MarkdownCommands.InsertLink,
            EditorCommand.Heading => MarkdownCommands.CycleHeading,
            EditorCommand.Bullets => MarkdownCommands.ToggleBullets,
            EditorCommand.Numbers => MarkdownCommands.ToggleNumbers,
            EditorCommand.Tasks => MarkdownCommands.ToggleTasks,
            EditorCommand.TaskDone => MarkdownCommands.ToggleTaskDone,
            EditorCommand.CodeBlock => MarkdownCommands.WrapInCodeBlock,
            _ => throw new ArgumentOutOfRangeException(nameof(command)),
        };

        if (transform(_text, _selection) is not { } edit)
        {
            return false;
        }

        Apply(edit, record: true);
        _ = NoteEditor.Focus(FocusState.Programmatic);
        return true;
    }

    private bool Replay(TextEdit? edit)
    {
        if (edit is not { } step)
        {
            return false;
        }

        Apply(step, record: false);
        return true;
    }

    /// <summary>
    /// Makes <paramref name="edit"/> in the control: the text replaced, then the selection, then the changed region
    /// restyled. Recorded as one undo step unless it is undo or redo itself.
    /// </summary>
    private void Apply(TextEdit edit, bool record)
    {
        string before = _text;
        Set(() =>
        {
            NoteEditor.Document.GetRange(edit.Start, edit.Start + edit.Length).SetText(TextSetOptions.None, edit.Replacement);
            NoteEditor.Document.Selection.SetRange(edit.After.Start, edit.After.End);
        });

        if (record)
        {
            _history.Record(before, edit, EditKind.Other, _selection);
        }

        _selection = edit.After;
        Commit(edit.ApplyTo(before));
    }

    /// <summary>
    /// Reads back a change TextChanged has not delivered yet, and the caret: RichEdit raises TextChanged after the
    /// keystroke, so a command run at once would otherwise see the text and caret from before the last key (measured:
    /// Enter right after typing continued the list one character early).
    /// </summary>
    private void Flush()
    {
        OnUserChanged();
        _selection = CurrentSelection();
    }

    /// <summary>The user changed the text: read it back as an edit, record it, restyle what it touched.</summary>
    private void OnUserChanged()
    {
        if (_applying || !_changed)
        {
            return;
        }

        _changed = false;
        NoteEditor.Document.GetText(TextGetOptions.UseLf, out string now);

        if (string.Equals(now, _text, StringComparison.Ordinal))
        {
            return;
        }

        _selection = CurrentSelection();

        // Mid-composition the text is the IME's, not yet the user's: no undo step and no restyle until it ends.
        if (_composingFrom is not null)
        {
            _text = now;
            _document = MarkdownDocument.Parse(now);
            ContentChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (TextDiff.Between(_text, now, _selection) is { } edit)
        {
            EditKind kind = edit.Length == 0 && edit.Replacement.Length == 1 ? EditKind.Typing
                : edit.Replacement.Length == 0 && edit.Length == 1 ? EditKind.Deletion
                : EditKind.Other;
            _history.Record(_text, edit, kind, _selectionBefore);
        }

        Commit(now);
    }

    private void OnCompositionEnded()
    {
        if (_composingFrom is not { } from)
        {
            return;
        }

        _composingFrom = null;
        NoteEditor.Document.GetText(TextGetOptions.UseLf, out string now);
        _selection = CurrentSelection();

        if (TextDiff.Between(from, now, _selection) is { } edit)
        {
            _history.Record(from, edit, EditKind.Other, _composingSelection);
        }

        MarkdownDocument was = MarkdownDocument.Parse(from);
        _text = now;
        _document = MarkdownDocument.Parse(now);
        Restyle(MarkdownDocument.ChangedRegion(was, _document));
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The text is now <paramref name="now"/>: parse it, restyle only what changed, tell the stack.</summary>
    private void Commit(string now)
    {
        long started = Stopwatch.GetTimestamp();
        MarkdownDocument was = _document;
        _text = now;
        _document = MarkdownDocument.Parse(now);
        (int start, int length) region = MarkdownDocument.ChangedRegion(was, _document);
        Restyle(region);
        QueueTaskMarks();
        Trace.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Noto.Editor change chars={_text.Length} restyled={region.length} ms={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F2}"));
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Restyle((int Start, int Length) region) =>
        Set(() => MarkdownStyling.Restyle(NoteEditor.Document, _document, region.Start, region.Start + region.Length, Palette()));

    /// <summary>A theme switch or Show markup: the whole note again, the selection kept.</summary>
    private void RestyleAll()
    {
        if (Note is null)
        {
            return;
        }

        Restyle((0, _text.Length));
        QueueTaskMarks();
    }

    /// <summary>The most tasks drawn as checkboxes; a note with more keeps its boxes as text (still clickable).</summary>
    private const int TaskMarkLimit = 300;

    private bool _marksQueued;

    /// <summary>Places the checkboxes once the layout that follows this change is done: positions are layout's.</summary>
    private void QueueTaskMarks()
    {
        if (_marksQueued)
        {
            return;
        }

        _marksQueued = true;
        NoteEditor.LayoutUpdated += PlaceTaskMarks;
    }

    /// <summary>
    /// A checkbox over each task's marker and box, from RichEdit's own character rectangles, so it sits exactly where
    /// the (invisible) source characters are. Checked or not is the parse's answer. With markup shown there are none:
    /// the source shows.
    /// </summary>
    private void PlaceTaskMarks(object? sender, object e)
    {
        NoteEditor.LayoutUpdated -= PlaceTaskMarks;
        _marksQueued = false;
        TaskMarks.Children.Clear();

        if (_showMarkup)
        {
            return;
        }

        var tasks = _document.Blocks.Where(b => b.Kind == MarkdownBlockKind.TaskItem).Take(TaskMarkLimit + 1).ToList();
        if (tasks.Count > TaskMarkLimit)
        {
            return;
        }

        // RichEdit measures from inside the control's border and padding (measured: 13 and 9 px off without them).
        var toMarks = NoteEditor.TransformToVisual(TaskMarks);
        double left = NoteEditor.BorderThickness.Left + NoteEditor.Padding.Left;
        double top = NoteEditor.BorderThickness.Top + NoteEditor.Padding.Top;

        foreach (MarkdownBlock task in tasks)
        {
            MarkdownSpan marker = task.Spans.First(s => s.Kind == MarkdownSpanKind.ListMarker);
            MarkdownSpan box = task.Spans.First(s => s.Kind == MarkdownSpanKind.TaskBox);
            NoteEditor.Document.GetRange(marker.Start, box.End).GetRect(PointOptions.ClientCoordinates, out Windows.Foundation.Rect rect, out _);

            if (rect.Height <= 0)
            {
                continue;
            }

            double size = Math.Round(rect.Height * 0.8);
            var mark = new FontIcon
            {
                Glyph = ((char)(task.IsChecked ? 0xE73A : 0xE739)).ToString(), // Segoe Fluent Icons: CheckboxComposite, Checkbox
                FontSize = size,
                Foreground = NoteEditor.Foreground,
            };
            Windows.Foundation.Point at = toMarks.TransformPoint(new Windows.Foundation.Point(rect.X + left, rect.Y + top));
            Canvas.SetLeft(mark, at.X);
            Canvas.SetTop(mark, at.Y + ((rect.Height - size) / 2));
            TaskMarks.Children.Add(mark);
        }
    }

    /// <summary>Changes the document as Noto, never read back as a user edit.</summary>
    private void Set(Action change)
    {
        bool outer = _applying;
        _applying = true;

        try
        {
            change();
        }
        finally
        {
            _applying = outer;
        }
    }

    /// <summary>RichEdit never pastes itself: Ctrl+V and the context menu's Paste are a formatted paste.</summary>
    private void OnPaste(object sender, TextControlPasteEventArgs e)
    {
        e.Handled = true;
        PasteAsync(formatted: true);
    }

    /// <summary>
    /// Pastes the clipboard through the same edit path: one undo step. Formatted (Ctrl+V), HTML from a browser or an
    /// office app arrives as Markdown (HtmlToMarkdown); otherwise, and always for Ctrl+Shift+V, as plain text.
    /// </summary>
    private async void PasteAsync(bool formatted)
    {
        string? pasted = null;

        try
        {
            DataPackageView clipboard = Clipboard.GetContent();

            if (formatted && clipboard.Contains(StandardDataFormats.Html))
            {
                string markdown = HtmlToMarkdown.Convert(HtmlFormatHelper.GetStaticFragment(await clipboard.GetHtmlFormatAsync()));
                pasted = markdown.Length > 0 ? markdown : null;
            }

            if (pasted is null && clipboard.Contains(StandardDataFormats.Text))
            {
                pasted = Normalize(await clipboard.GetTextAsync());
            }
        }
        catch (COMException)
        {
            // Another app holds the clipboard open: the paste does nothing, as in any other Windows editor.
            return;
        }

        if (pasted is null)
        {
            return;
        }

        // Read after the clipboard: a keystroke during the wait has moved the text and the caret.
        Flush();
        TextSelection at = _selection;
        Apply(new TextEdit(at.Start, at.Length, pasted, TextSelection.Caret(at.Start + pasted.Length)), record: true);
    }

    /// <summary>
    /// The editor's own keys, before the control: leaving (ADR-016 §5), undo and redo, the formatting chords
    /// (implementation plan §10), Enter in a list, a soft break that stays a plain line break, and Tab, which moves
    /// focus (the P1 accessibility contract) unless it nests a list item.
    /// </summary>
    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool ctrl = IsDown(VirtualKey.Control);
        bool shift = IsDown(VirtualKey.Shift);
        bool alt = IsDown(VirtualKey.Menu);

        EditorLeave? leave = e.Key switch
        {
            VirtualKey.Escape => EditorLeave.Escape,
            VirtualKey.Up when ctrl && alt => EditorLeave.Previous,
            VirtualKey.Down when ctrl && alt => EditorLeave.Next,
            _ => null,
        };

        if (leave is { } request)
        {
            e.Handled = true;
            LeaveRequested?.Invoke(this, request);
            return;
        }

        // Alt+Up/Down move the selected lines (G38, G39); with Ctrl they leave for another card (above).
        if (alt && !ctrl && e.Key is VirtualKey.Up or VirtualKey.Down)
        {
            e.Handled = true;
            Flush();
            Func<string, TextSelection, TextEdit?> move = e.Key == VirtualKey.Up ? MarkdownCommands.MoveLinesUp : MarkdownCommands.MoveLinesDown;
            if (move(_text, _selection) is { } moved)
            {
                Apply(moved, record: true);
            }

            return;
        }

        if (ctrl && shift && !alt && e.Key == VirtualKey.V)
        {
            e.Handled = true;
            PasteAsync(formatted: false);
            return;
        }

        // Ctrl+Alt is AltGr on many layouts: it types characters, so it is never a formatting chord.
        if (ctrl && !alt && Chord(e.Key, shift) is { } command)
        {
            e.Handled = true;
            _ = Run(command);
            return;
        }

        if (e.Key == VirtualKey.Enter && !ctrl && !alt)
        {
            Flush();
            TextEdit? edit = shift ? null : MarkdownCommands.ContinueList(_text, _selection);
            edit ??= shift ? new TextEdit(_selection.Start, _selection.Length, "\n", TextSelection.Caret(_selection.Start + 1)) : null;

            if (edit is { } enter)
            {
                e.Handled = true;
                Apply(enter, record: true);
            }

            return;
        }

        if (e.Key == VirtualKey.Tab && !ctrl && !alt)
        {
            // In a list Tab nests and Shift+Tab un-nests (G36, G37); anywhere else it moves focus.
            e.Handled = true;
            Flush();
            Func<string, TextSelection, TextEdit?> shiftItems = shift ? MarkdownCommands.OutdentListItems : MarkdownCommands.IndentListItems;
            if (shiftItems(_text, _selection) is { } indented)
            {
                Apply(indented, record: true);
                return;
            }

            _ = FocusManager.TryMoveFocus(shift ? FocusNavigationDirection.Previous : FocusNavigationDirection.Next, new FindNextElementOptions { SearchRoot = XamlRoot?.Content });
        }
    }

    /// <summary>
    /// A click on a task's box checks or unchecks that task, as the box invites. Hit-tested by character, so a click
    /// on the task's text only places the caret; a drag that selects is never a click.
    /// </summary>
    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (e.GetCurrentPoint(NoteEditor).Properties.PointerUpdateKind != Microsoft.UI.Input.PointerUpdateKind.LeftButtonReleased)
        {
            return;
        }

        Flush();
        if (!_selection.IsCaret)
        {
            return;
        }

        // RichEdit's client coordinates start inside the control's border and padding.
        var point = e.GetCurrentPoint(NoteEditor).Position;
        point = new Windows.Foundation.Point(
            point.X - NoteEditor.BorderThickness.Left - NoteEditor.Padding.Left,
            point.Y - NoteEditor.BorderThickness.Top - NoteEditor.Padding.Top);
        int at = NoteEditor.Document.GetRangeFromPoint(point, PointOptions.ClientCoordinates).StartPosition;
        MarkdownBlock? task = _document.Blocks.FirstOrDefault(b => b.Kind == MarkdownBlockKind.TaskItem && b.Start <= at && at <= b.End);

        // The drawn checkbox covers the marker and the box: a click anywhere on it is a caret inside or at either end.
        if (task is null)
        {
            return;
        }

        MarkdownSpan box = task.Spans.First(s => s.Kind == MarkdownSpanKind.TaskBox);
        int from = task.Spans.Where(s => s.Kind == MarkdownSpanKind.ListMarker).Select(s => s.Start).DefaultIfEmpty(box.Start).First();

        if (at >= from && at <= box.End && MarkdownCommands.ToggleTaskDone(_text, TextSelection.Caret(box.Start)) is { } toggled)
        {
            Apply(toggled with { After = _selection }, record: true);
        }
    }

    /// <summary>The editor's chord map (implementation plan §10; Ctrl+E and Ctrl+K for code and link).</summary>
    private static EditorCommand? Chord(VirtualKey key, bool shift) => (key, shift) switch
    {
        (VirtualKey.Z, false) => EditorCommand.Undo,
        (VirtualKey.Y, false) or (VirtualKey.Z, true) => EditorCommand.Redo,
        (VirtualKey.B, false) => EditorCommand.Bold,
        (VirtualKey.I, false) => EditorCommand.Italic,
        (VirtualKey.U, false) => EditorCommand.Underline,
        (VirtualKey.E, true) => EditorCommand.Strikethrough,
        (VirtualKey.E, false) => EditorCommand.Code,
        (VirtualKey.K, false) => EditorCommand.Link,
        (VirtualKey.H, true) => EditorCommand.Heading,
        (VirtualKey.L, false) => EditorCommand.Bullets,
        (VirtualKey.L, true) => EditorCommand.Numbers,
        (VirtualKey.T, false) => EditorCommand.Tasks,
        ((VirtualKey)0xBE, false) => EditorCommand.TaskDone,
        (VirtualKey.R, true) => EditorCommand.ShowMarkup,
        _ => null,
    };

    private TextSelection CurrentSelection()
    {
        ITextSelection selection = NoteEditor.Document.Selection;
        int start = selection.StartPosition;
        int end = Math.Min(selection.EndPosition, _text.Length);
        start = Math.Min(start, end);
        return selection.Options.HasFlag(SelectionOptions.StartActive) ? new TextSelection(end, start) : new TextSelection(start, end);
    }

    /// <summary>The theme's colours and the design system's type, for this host's theme now.</summary>
    private EditorPalette Palette() => new(
        Text: ColorOf(NoteEditor.Foreground),
        Markup: ColorOf(NoteEditor.MarkupBrush),
        Link: ColorOf(NoteEditor.LinkBrush),
        CodeSurface: ColorOf(NoteEditor.CodeSurfaceBrush),
        Surface: ColorOf(NoteEditor.Background),
        BodyFont: FirstFamily("FontFamilyBody"),
        CodeFont: FirstFamily("FontFamilyCode"),
        BodySize: (double)Application.Current.Resources["FontSizeBody"],
        Heading1Size: (double)Application.Current.Resources["FontSizeHeading"],
        Heading2Size: (double)Application.Current.Resources["FontSizeHeading2"],
        Heading3Size: (double)Application.Current.Resources["FontSizeHeading3"],
        ShowMarkup: _showMarkup);

    private static Windows.UI.Color ColorOf(Brush? brush) => brush is SolidColorBrush solid ? solid.Color : Colors.Transparent;

    /// <summary>RTF and TOM name one font: the family list's first.</summary>
    private static string FirstFamily(string key) => ((FontFamily)Application.Current.Resources[key]).Source.Split(',')[0].Trim();

    /// <summary>CRLF and lone CR become LF: one character per break, as the control stores them (ADR-017 §1).</summary>
    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
}
