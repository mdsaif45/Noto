using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Printing;
using Windows.Graphics.Printing;
using Windows.System;
using WinRT.Interop;

namespace Noto.Spike.Print;

/// <summary>
/// Opens Windows' print dialog for one note (#246). <c>Noto.Spike.Print.exe [note.md]</c>: a Markdown file whose
/// pictures are <c>attachments/&lt;name&gt;</c> beside it, as Noto's Export as Markdown writes them; with no argument, a
/// sample note with every kind of block, a picture and a second page is written to %TEMP%\noto-print-spike first.
/// The dialog opens on start; the button and Ctrl+P open it again. Needs a person at the dialog: never run unattended.
/// </summary>
public partial class App : Application
{
    // Pages are laid out here, in the window's tree but never seen: a RichTextBlockOverflow only knows what overflows
    // once it has been measured (the WindowsAppSDK-Samples Printing sample's PrintCanvas).
    private readonly Canvas _layout = new() { Opacity = 0, IsHitTestVisible = false };
    private readonly List<UIElement> _pages = [];
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private Window _window = null!;
    private nint _hwnd;
    private NotePrint _note = null!;
    private PrintDocument _document = null!;
    private IPrintDocumentSource _source = null!;

    public App() => InitializeComponent();

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        string[] arguments = Environment.GetCommandLineArgs();
        string path = arguments.Length > 1 ? Path.GetFullPath(arguments[1]) : await NotePrint.WriteSampleAsync();

        var print = new Button { Content = "Print (Ctrl+P)" };
        AutomationProperties.SetName(print, "Print the note");
        print.Click += async (_, _) => await ShowPrintUiAsync();

        var panel = new StackPanel { Padding = new Thickness(24), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = path, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(print);
        panel.Children.Add(_status);

        var root = new Grid();
        root.Children.Add(_layout);
        root.Children.Add(panel);
        var ctrlP = new KeyboardAccelerator { Key = VirtualKey.P, Modifiers = VirtualKeyModifiers.Control };
        ctrlP.Invoked += async (_, e) =>
        {
            e.Handled = true;
            await ShowPrintUiAsync();
        };
        root.KeyboardAccelerators.Add(ctrlP);

        _window = new Window { Title = "Noto spike: print (#246)", Content = root };
        _window.Activate();
        _hwnd = WindowNative.GetWindowHandle(_window);

        // Pictures are decoded to pixels before the dialog asks for a page: microsoft-ui-xaml#11095 (WinAppSDK 2.0.1)
        // prints a BitmapImage that is still decoding as a blank.
        _note = await NotePrint.LoadAsync(path);

        _document = new PrintDocument();
        _source = _document.DocumentSource;
        _document.Paginate += OnPaginate;
        _document.GetPreviewPage += (_, e) => _document.SetPreviewPage(e.PageNumber, _pages[e.PageNumber - 1]);
        _document.AddPages += OnAddPages;

        PrintManagerInterop.GetForWindow(_hwnd).PrintTaskRequested += OnPrintTaskRequested;
        await ShowPrintUiAsync();
    }

    private async Task ShowPrintUiAsync()
    {
        if (!PrintManager.IsSupported())
        {
            _status.Text = "PrintManager.IsSupported() is false on this device.";
            return;
        }

        try
        {
            bool shown = await PrintManagerInterop.ShowPrintUIForWindowAsync(_hwnd);
            _status.Text = $"ShowPrintUIForWindowAsync returned {shown}.";
        }
        catch (Exception ex)
        {
            // Windows 10 before 19045.2788 (KB5023773) throws here (microsoft-ui-xaml#4419).
            _status.Text = $"ShowPrintUIForWindowAsync threw {ex.GetType().Name} 0x{ex.HResult:X8}: {ex.Message}";
        }
    }

    // Raised off the UI thread: only the task is made here.
    private void OnPrintTaskRequested(PrintManager sender, PrintTaskRequestedEventArgs args)
    {
        PrintTask task = args.Request.CreatePrintTask(_note.Title, source => source.SetSource(_source));
        task.Completed += (_, e) => _window.DispatcherQueue.TryEnqueue(() => _status.Text = $"Print task: {e.Completion}, {_pages.Count} page(s).");
    }

    /// <summary>
    /// Lays the note out on pages of the size the dialog chose, again whenever the paper or orientation changes: one
    /// RichTextBlock on the first page, then a RichTextBlockOverflow per page while text is left over.
    /// </summary>
    private void OnPaginate(object sender, PaginateEventArgs e)
    {
        _pages.Clear();
        _layout.Children.Clear();
        PrintPageDescription page = e.PrintTaskOptions.GetPageDescription(0);

        // The printer's unprintable edge, or 7.5% of the paper (the sample's margin), whichever is wider.
        double left = Math.Max(page.ImageableRect.X, page.PageSize.Width * 0.075);
        double top = Math.Max(page.ImageableRect.Y, page.PageSize.Height * 0.075);
        double right = Math.Max(page.PageSize.Width - page.ImageableRect.Right, page.PageSize.Width * 0.075);
        double bottom = Math.Max(page.PageSize.Height - page.ImageableRect.Bottom, page.PageSize.Height * 0.075);
        double width = page.PageSize.Width - left - right;
        double height = page.PageSize.Height - top - bottom;

        RichTextBlock text = _note.Render(width, height);
        FrameworkElement body = text;
        while (true)
        {
            body.Width = width;
            body.Height = height;
            var sheet = new Border
            {
                Width = page.PageSize.Width,
                Height = page.PageSize.Height,
                Padding = new Thickness(left, top, right, bottom),
                Background = new SolidColorBrush(Microsoft.UI.Colors.White),
                RequestedTheme = ElementTheme.Light,
                Child = body,
            };
            _layout.Children.Add(sheet);
            _layout.InvalidateMeasure();
            _layout.UpdateLayout();
            _pages.Add(sheet);

            bool more = body is RichTextBlock first ? first.HasOverflowContent : ((RichTextBlockOverflow)body).HasOverflowContent;
            if (!more || _pages.Count == 200)
            {
                break;
            }

            var next = new RichTextBlockOverflow();
            if (body is RichTextBlock block)
            {
                block.OverflowContentTarget = next;
            }
            else
            {
                ((RichTextBlockOverflow)body).OverflowContentTarget = next;
            }

            body = next;
        }

        _document.SetPreviewPageCount(_pages.Count, PreviewPageCountType.Final);
    }

    private void OnAddPages(object sender, AddPagesEventArgs e)
    {
        foreach (UIElement page in _pages)
        {
            _document.AddPage(page);
        }

        _document.AddPagesComplete();
    }
}
