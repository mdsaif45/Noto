using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;

namespace Noto.Spike.HiddenMarkdown;

/// <summary>
/// Runs every probe once, in order, writes spike-results.txt and exits. <c>--out &lt;file&gt;</c> moves the results;
/// <c>--hold &lt;dir&gt;</c> instead loads the UI Automation sample, writes what a reader should get into that folder and
/// stays open for Probe-Uia.ps1.
/// </summary>
public partial class App : Application
{
    private const float BaseSize = 14;
    private static readonly float[] HeadingSize = [0, 24, 20, 18, 16, 15];

    private readonly StringBuilder _log = new();
    private Window _window = null!;
    private RichEditBox _box = null!;
    private ScrollViewer _scroller = null!;
    private Grid _root = null!;
    private bool _grow;
    private bool _started;
    private bool _fonts = true;
    private IntPtr _hwnd;
    private bool _showMarkup;
    private string? _output;
    private int _contentChanging;
    private int _formatChanging;
    private int _compositions;

    private static readonly Color Foreground = Color.FromArgb(255, 0x1B, 0x1B, 0x1B);
    private static readonly Color Dim = Color.FromArgb(255, 0x80, 0x80, 0x80);
    private static readonly Color Accent = Color.FromArgb(255, 0x00, 0x5F, 0xB8);
    private static readonly Color Highlight = Color.FromArgb(255, 0xFF, 0xF1, 0x9C);

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new Window { Title = "Noto spike — hidden markdown" };
        _box = new RichEditBox
        {
            TextWrapping = TextWrapping.Wrap,
            DisabledFormattingAccelerators = DisabledFormattingAccelerators.All,
            IsSpellCheckEnabled = true,
            FontSize = BaseSize,
        };
        AutomationProperties.SetName(_box, "Spike editor");
        AutomationProperties.SetAutomationId(_box, "SpikeEditor");
        _box.TextChanging += (_, e) =>
        {
            if (e.IsContentChanging)
            {
                _contentChanging++;
            }
            else
            {
                _formatChanging++;
            }
        };
        _box.TextCompositionStarted += (_, _) => _compositions++;

        // --grow: the editor grows with its text and the surrounding stack scrolls, as a card's editor does today
        // (ADR-016), so the whole document is laid out. Otherwise the editor has the window's height and scrolls
        // inside itself, so only what is seen is laid out.
        _grow = Environment.GetCommandLineArgs().Contains("--grow");
        _root = new Grid();
        _scroller = new ScrollViewer();
        if (_grow)
        {
            _scroller.Content = _box;
            _root.Children.Add(_scroller);
        }
        else
        {
            _root.Children.Add(_box);
        }

        _window.Content = _root;

        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(_hwnd)).Resize(new SizeInt32(440, 900));
        _window.Activate();

        _box.Loaded += async (_, _) =>
        {
            // Loaded fires again when the read-view probe puts the editor back.
            if (_started)
            {
                return;
            }

            _started = true;
            string[] argv = Environment.GetCommandLineArgs();
            string? hold = After(argv, "--hold");
            _output = After(argv, "--out") ?? Path.Combine(AppContext.BaseDirectory, "spike-results.txt");
            try
            {
                if (Environment.GetCommandLineArgs().Contains("--matrix"))
                {
                    await MatrixAsync();
                }
                else if (Environment.GetCommandLineArgs().Contains("--chunk"))
                {
                    await ChunkAsync();
                }
                else if (Environment.GetCommandLineArgs().Contains("--styled"))
                {
                    await StyledAsync();
                }
                else if (Environment.GetCommandLineArgs().Contains("--ops"))
                {
                    await OpsAsync();
                }
                else if (hold is not null)
                {
                    Hold(hold);
                    return;
                }

                else
                {
                    await RunAsync();
                }
            }
            catch (Exception ex)
            {
                Log("FATAL", ex.ToString());
            }

            Log("done", "exiting");
            Exit();
        };
    }

    private RichEditTextDocument Doc => _box.Document;

    private async Task RunAsync()
    {
        Log("environment", $"{Environment.OSVersion.VersionString}, .NET {Environment.Version}, {Environment.ProcessorCount} logical CPUs");
        Log("layout mode", _grow ? "grow (editor sized to its text, outer scroll)" : "fixed (editor sized to the window, inner scroll)");
        Fidelity();
        CaretByTom();
        UndoByTom();
        FormatOnlyChange();
        await KeysAsync();
        await PerformanceAsync();
        await ReadViewAsync();
        Log("compositions seen", _compositions.ToString(CultureInfo.InvariantCulture));
    }

    // ---------------------------------------------------------------- fidelity

    /// <summary>Does the document text stay the markdown source, character for character, with markup hidden?</summary>
    private void Fidelity()
    {
        foreach ((string name, string text) in Corpus.Fidelity)
        {
            string lf = SpikeMarkdown.NormaliseLineEnds(text);
            Doc.SetText(TextSetOptions.None, text);

            Doc.GetText(TextGetOptions.None, out string none);
            Doc.GetText(TextGetOptions.UseLf, out string useLf);
            Doc.GetText(TextGetOptions.AllowFinalEop | TextGetOptions.UseLf, out string finalEop);
            int length = Doc.GetRange(0, int.MaxValue).EndPosition;

            List<Span> spans = SpikeMarkdown.Parse(lf);
            Style(0, lf.Length, spans);

            Doc.GetText(TextGetOptions.UseLf, out string styled);
            Doc.GetText(TextGetOptions.UseLf | TextGetOptions.NoHidden, out string visible);
            int offsetMisses = spans.Count(s => Text(s.Start, s.End) != lf.Substring(s.Start, s.Length));
            int notHidden = spans.Count(s => s.Kind == SpanKind.Markup && Doc.GetRange(s.Start, s.End).CharacterFormat.Hidden != FormatEffect.On);

            Log($"fidelity {name}",
                $"chars={lf.Length} docEnd={length} " +
                $"None==CR-form:{none == lf.Replace('\n', '\r')} UseLf==LF:{useLf == lf} finalEop='{Escape(finalEop[Math.Max(0, finalEop.Length - 1)..])}' " +
                $"styled UseLf==LF:{styled == lf} NoHidden==visible:{visible == SpikeMarkdown.Visible(lf, spans)} " +
                $"spans={spans.Count} offsetMisses={offsetMisses} markupNotHidden={notHidden}");

            Doc.SetText(TextSetOptions.FormatRtf, Rtf.FromSpans(lf, spans, _showMarkup));
            Doc.GetText(TextGetOptions.UseLf, out string fromRtf);
            Doc.GetText(TextGetOptions.UseLf | TextGetOptions.NoHidden, out string rtfVisible);
            int rtfNotHidden = spans.Count(s => s.Kind == SpanKind.Markup && Doc.GetRange(s.Start, s.End).CharacterFormat.Hidden != FormatEffect.On);
            int rtfMisses = spans.Count(s => Text(s.Start, s.End) != lf.Substring(s.Start, s.Length));
            Log($"fidelity {name} via RTF", $"UseLf==LF:{fromRtf == lf} NoHidden==visible:{rtfVisible == SpikeMarkdown.Visible(lf, spans)} offsetMisses={rtfMisses} markupNotHidden={rtfNotHidden}");
            if (fromRtf != lf)
            {
                Log($"  {name} via RTF UseLf", Escape(fromRtf));
            }

            if (useLf != lf)
            {
                Log($"  {name} UseLf", Escape(useLf));
                Log($"  {name} expected", Escape(lf));
            }
        }
    }

    // ---------------------------------------------------------------- caret, undo (no keys)

    private void CaretByTom()
    {
        Load(Corpus.Caret);
        // "a **bold** b": a0 _1 *2 *3 b4 o5 l6 d7 *8 *9 _10 b11
        foreach (int at in new[] { 2, 3, 4, 8, 9 })
        {
            Doc.Selection.SetRange(at, at);
            int placed = Doc.Selection.StartPosition;
            Doc.Selection.MoveRight(TextRangeUnit.Character, 1, false);
            int right = Doc.Selection.StartPosition;
            Doc.Selection.SetRange(at, at);
            Doc.Selection.MoveLeft(TextRangeUnit.Character, 1, false);
            Log($"tom caret set {at}", $"lands {placed}, MoveRight -> {right}, MoveLeft -> {Doc.Selection.StartPosition}");
        }
    }

    /// <summary>Does a style applied from code enter RichEdit's own undo stack?</summary>
    private void UndoByTom()
    {
        Doc.UndoLimit = 100;
        Doc.SetText(TextSetOptions.None, "abc");
        Doc.Selection.SetRange(3, 3);
        Doc.Selection.TypeText("d");
        Doc.GetRange(0, 4).CharacterFormat.Bold = FormatEffect.On;
        Doc.Undo();
        Log("tom undo after a coded style", $"text='{Text(0, 10)}' bold={Doc.GetRange(0, 1).CharacterFormat.Bold}");
        Doc.Undo();
        Log("tom undo again", $"text='{Text(0, 10)}'");

        Doc.UndoLimit = 0;
        Doc.Selection.SetRange(0, 0);
        Doc.Selection.TypeText("z");
        Log("tom UndoLimit=0", $"canUndo={Doc.CanUndo()} text='{Text(0, 10)}'");
        Doc.UndoLimit = 100;
    }

    private void FormatOnlyChange()
    {
        Load("plain words");
        _contentChanging = _formatChanging = 0;
        Doc.GetRange(0, 5).CharacterFormat.Bold = FormatEffect.On;
        Log("format-only change raises TextChanging", $"content={_contentChanging} formatOnly={_formatChanging}");
        _contentChanging = _formatChanging = 0;
        Doc.Selection.SetRange(5, 5);
        Doc.Selection.TypeText("!");
        Log("typed change raises TextChanging", $"content={_contentChanging} formatOnly={_formatChanging}");
    }

    // ---------------------------------------------------------------- real keys

    private async Task KeysAsync()
    {
        _box.Focus(FocusState.Programmatic);
        await Task.Delay(300);
        if (!Keys.Foreground(_hwnd))
        {
            Log("keys", "BLOCKED: the spike is not the foreground window; no key was sent");
            return;
        }

        Load(Corpus.Caret);
        await KeyFrom(8, "Right at 8 (after 'bold', before hidden '**')", Keys.Right);
        await KeyFrom(4, "Left at 4 (after hidden '**', before 'bold')", Keys.Left);
        await KeyFrom(0, "End on the line", Keys.End);
        await KeyFrom(12, "Home on the line", Keys.Home);

        Load(Corpus.Caret);
        await KeyFrom(4, "Backspace at 4", Keys.Back);
        Log("  after Backspace", $"text='{Text(0, 20)}'");

        // Typing beside hidden markup, with no restyle afterwards: what does the new character inherit?
        Load(Corpus.Caret);
        Doc.Selection.SetRange(4, 4);
        await Press(Keys.X);
        Log("typed 'x' at 4 (no restyle)", $"text='{Text(0, 20)}' hidden={Doc.GetRange(4, 5).CharacterFormat.Hidden} bold={Doc.GetRange(4, 5).CharacterFormat.Bold}");
        Load(Corpus.Caret);
        Doc.Selection.SetRange(8, 8);
        await Press(Keys.X);
        Log("typed 'x' at 8 (no restyle)", $"text='{Text(0, 20)}' hidden={Doc.GetRange(8, 9).CharacterFormat.Hidden} bold={Doc.GetRange(8, 9).CharacterFormat.Bold}");

        // DisabledFormattingAccelerators = All: RichEdit's own Ctrl+B must do nothing.
        Doc.SetText(TextSetOptions.None, "plain");
        Doc.Selection.SetRange(0, 5);
        await Press(Keys.B, Keys.Control);
        Log("Ctrl+B with formatting accelerators disabled", $"bold={Doc.GetRange(0, 5).CharacterFormat.Bold} text='{Text(0, 10)}'");

        // Undo: does a coded restyle sit between the user's typing and Ctrl+Z?
        Doc.UndoLimit = 100;
        Doc.SetText(TextSetOptions.None, "abc");
        Doc.Selection.SetRange(3, 3);
        await Press(Keys.X);
        Doc.GetRange(0, 4).CharacterFormat.Bold = FormatEffect.On;
        await Press(Keys.Z, Keys.Control);
        Log("Ctrl+Z after typing then a coded style", $"text='{Text(0, 10)}' bold={Doc.GetRange(0, 1).CharacterFormat.Bold}");

        Doc.UndoLimit = 0;
        Doc.SetText(TextSetOptions.None, "abc");
        Doc.Selection.SetRange(3, 3);
        _contentChanging = 0;
        await Press(Keys.X);
        int typed = _contentChanging;
        await Press(Keys.Z, Keys.Control);
        Log("UndoLimit=0: typing then Ctrl+Z", $"typing raised content changes={typed}; after Ctrl+Z text='{Text(0, 10)}'");
        Doc.UndoLimit = 100;
    }

    private async Task KeyFrom(int at, string name, ushort key)
    {
        Doc.Selection.SetRange(at, at);
        await Press(key);
        Log($"key {name}", $"caret {at} -> {Doc.Selection.StartPosition}");
    }

    private async Task Press(ushort key, params ushort[] modifiers)
    {
        Keys.Press(_hwnd, key, modifiers);
        await Task.Delay(150);
    }

    // ---------------------------------------------------------------- performance

    private async Task PerformanceAsync()
    {
        string small = Corpus.Small();
        string large = Corpus.Large(100_636);
        Log("perf corpus", $"small={small.Length} chars, large={large.Length} chars, {large.Count(c => c == '\n')} lines, {SpikeMarkdown.Parse(large).Count} spans");

        // How span-by-span TOM styling scales with the note, batched and not, before risking it on 100 KB.
        var sw = new Stopwatch();
        foreach (int size in new[] { 2_500, 5_000, 10_000, 20_000 })
        {
            string text = Corpus.Large(size);
            List<Span> spans = SpikeMarkdown.Parse(text);
            Doc.SetText(TextSetOptions.None, text);
            sw.Restart();
            Style(0, text.Length, spans);
            double batched = sw.Elapsed.TotalMilliseconds;
            Doc.SetText(TextSetOptions.None, text);
            sw.Restart();
            Style(0, text.Length, spans, batch: false);
            Log($"perf TOM style {text.Length} chars, {spans.Count} spans", $"batched {batched:F1} · unbatched {sw.Elapsed.TotalMilliseconds:F1} ms");
        }

        sw.Restart();
        Doc.SetText(TextSetOptions.None, large);
        Log("perf large plain SetText (unstyled)", Ms(sw));

        await LoadRuns("small TOM", small, rtf: false);
        await LoadRuns("small RTF", small, rtf: true);
        await LoadRuns("large RTF", large, rtf: true);
        if (!_grow)
        {
            await LoadRuns("large TOM", large, rtf: false);
        }

        Keystrokes(large);

        sw.Restart();
        for (int i = 0; i < 10; i++)
        {
            Doc.GetText(TextGetOptions.UseLf, out _);
        }

        Log("perf large GetText(UseLf)", $"{sw.Elapsed.TotalMilliseconds / 10:F2} ms each");

        sw.Restart();
        for (int i = 0; i < 10; i++)
        {
            SpikeMarkdown.Parse(large);
        }

        Log("perf large full parse", $"{sw.Elapsed.TotalMilliseconds / 10:F2} ms each");
    }

    private async Task LoadRuns(string name, string text, bool rtf)
    {
        var set = new List<double>();
        var parse = new List<double>();
        var style = new List<double>();
        var layout = new List<double>();
        var frame = new List<double>();

        for (int run = 0; run < 5; run++)
        {
            Doc.SetText(TextSetOptions.None, string.Empty);
            await NextFrame();

            var sw = Stopwatch.StartNew();
            List<Span> spans = SpikeMarkdown.Parse(text);
            parse.Add(sw.Elapsed.TotalMilliseconds);
            double before = sw.Elapsed.TotalMilliseconds;
            if (rtf)
            {
                // Build the RTF (counted as style) and load it (counted as SetText): one call styles everything.
                string doc = Rtf.FromSpans(text, spans, _showMarkup);
                style.Add(sw.Elapsed.TotalMilliseconds - before);
                before = sw.Elapsed.TotalMilliseconds;
                Doc.SetText(TextSetOptions.FormatRtf, doc);
                set.Add(sw.Elapsed.TotalMilliseconds - before);
            }
            else
            {
                Doc.SetText(TextSetOptions.None, text);
                set.Add(sw.Elapsed.TotalMilliseconds - before);
                before = sw.Elapsed.TotalMilliseconds;
                Style(0, text.Length, spans);
                style.Add(sw.Elapsed.TotalMilliseconds - before);
            }

            before = sw.Elapsed.TotalMilliseconds;
            _root.UpdateLayout();
            layout.Add(sw.Elapsed.TotalMilliseconds - before);
            await NextFrame();
            frame.Add(sw.Elapsed.TotalMilliseconds);
        }

        Log($"perf {name} load (median of 5)",
            $"parse {Median(parse)} · style {Median(style)} · SetText {Median(set)} · layout {Median(layout)} · first frame {Median(frame)} ms (max frame {frame.Max():F1})");
    }

    /// <summary>
    /// A character typed mid-document, and what the host must then do: read the text back, re-parse the edited line,
    /// restyle that line. Through TOM, not real keys, so the input path itself is not in the number.
    /// </summary>
    private void Keystrokes(string large)
    {
        Doc.SetText(TextSetOptions.FormatRtf, Rtf.FromSpans(large, SpikeMarkdown.Parse(large), _showMarkup));

        var type = new List<double>();
        var read = new List<double>();
        var parse = new List<double>();
        var style = new List<double>();
        var total = new List<double>();
        var rng = new Random(11);
        string[] lines = large.Split('\n');

        for (int i = 0; i < 50; i++)
        {
            // A line that is plain prose or inline markup, never a fence (the one cross-line state).
            int n;
            do
            {
                n = rng.Next(lines.Length / 4, 3 * lines.Length / 4);
            }
            while (!lines[n].StartsWith("Some ", StringComparison.Ordinal));

            int lineStart = lines.Take(n).Sum(l => l.Length + 1);
            int at = lineStart + 5;

            var sw = Stopwatch.StartNew();
            Doc.Selection.SetRange(at, at);
            Doc.Selection.TypeText("x");
            type.Add(sw.Elapsed.TotalMilliseconds);
            double t = sw.Elapsed.TotalMilliseconds;
            Doc.GetText(TextGetOptions.UseLf, out string now);
            read.Add(sw.Elapsed.TotalMilliseconds - t);
            t = sw.Elapsed.TotalMilliseconds;
            int end = now.IndexOf('\n', at);
            var spans = new List<Span>();
            bool fence = false;
            SpikeMarkdown.ParseLine(now, lineStart, end, ref fence, spans);
            parse.Add(sw.Elapsed.TotalMilliseconds - t);
            t = sw.Elapsed.TotalMilliseconds;
            Style(lineStart, end, spans);
            style.Add(sw.Elapsed.TotalMilliseconds - t);
            total.Add(sw.Elapsed.TotalMilliseconds);

            Doc.GetRange(at, at + 1).Text = string.Empty;
        }

        Log("perf large keystroke (median / max of 50)",
            $"type {Median(type)}/{type.Max():F2} · read back {Median(read)}/{read.Max():F2} · parse line {Median(parse)}/{parse.Max():F2} · " +
            $"style line {Median(style)}/{style.Max():F2} · total {Median(total)}/{total.Max():F2} ms");
    }

    // ---------------------------------------------------------------- what makes styling slow

    /// <summary>One 10 KB note, styled span by span and loaded as RTF, under one changed condition at a time.</summary>
    private async Task MatrixAsync()
    {
        string text = Corpus.Large(10_000);
        List<Span> spans = SpikeMarkdown.Parse(text);
        string rtf = Rtf.FromSpans(text, spans, showMarkup: false);
        Log("matrix note", $"{text.Length} chars, {spans.Count} spans");
        RichEditBox original = _box;

        var variants = new (string Name, bool Spell, bool Show, bool Fonts, bool Handlers, bool Attached)[]
        {
            ("baseline (spell on, hidden, fonts, handlers, on screen)", true, false, true, true, true),
            ("spell check off", false, false, true, true, true),
            ("markup shown, not hidden", true, true, true, true, true),
            ("no font changes", true, false, false, true, true),
            ("no TextChanging handler", true, false, true, false, true),
            ("not in the visual tree", true, false, true, true, false),
            ("all off: spell, hidden, fonts, handlers", false, true, false, false, true),
        };

        foreach (var v in variants)
        {
            var box = new RichEditBox { TextWrapping = TextWrapping.Wrap, IsSpellCheckEnabled = v.Spell, FontSize = BaseSize };
            if (v.Handlers)
            {
                box.TextChanging += (_, e) => _formatChanging += e.IsContentChanging ? 0 : 1;
            }

            _root.Children.Clear();
            if (v.Attached)
            {
                _root.Children.Add(box);
            }

            await NextFrame();
            await NextFrame();
            _box = box;
            _showMarkup = v.Show;
            _fonts = v.Fonts;

            var tom = new List<double>();
            var load = new List<double>();
            for (int run = 0; run < 3; run++)
            {
                Doc.SetText(TextSetOptions.None, text);
                var sw = Stopwatch.StartNew();
                Style(0, text.Length, spans);
                tom.Add(sw.Elapsed.TotalMilliseconds);
                sw.Restart();
                Doc.SetText(TextSetOptions.FormatRtf, v.Show ? Rtf.FromSpans(text, spans, showMarkup: true) : rtf);
                load.Add(sw.Elapsed.TotalMilliseconds);
                await NextFrame();
            }

            Log($"matrix {v.Name}", $"TOM style {Median(tom)} · RTF load {Median(load)} ms");
        }

        _box = original;
        _showMarkup = false;
        _fonts = true;
        _root.Children.Clear();
        _root.Children.Add(_grow ? _scroller : _box);
    }

    /// <summary>
    /// The cost of one formatting call against the note's size, a whole-format clone against property-by-property,
    /// RTF load against its run count, and the platform's own insert cost in a TextBox and a RichEditBox.
    /// </summary>
    private async Task OpsAsync()
    {
        foreach (int size in new[] { 1_000, 10_000, 50_000, 100_000 })
        {
            string text = Corpus.Large(size);
            Doc.SetText(TextSetOptions.None, text);
            await NextFrame();
            var rng = new Random(3);
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 100; i++)
            {
                int at = rng.Next(0, text.Length - 10);
                Doc.GetRange(at, at + 5).CharacterFormat.Bold = (i & 1) == 0 ? FormatEffect.On : FormatEffect.Off;
            }

            double one = sw.Elapsed.TotalMilliseconds / 100;

            ITextCharacterFormat clone = Doc.GetRange(0, 1).CharacterFormat.GetClone();
            clone.Bold = FormatEffect.On;
            clone.Italic = FormatEffect.On;
            clone.Hidden = FormatEffect.Off;
            clone.ForegroundColor = Accent;
            sw.Restart();
            for (int i = 0; i < 100; i++)
            {
                int at = rng.Next(0, text.Length - 10);
                Doc.GetRange(at, at + 5).CharacterFormat = clone;
            }

            double cloned = sw.Elapsed.TotalMilliseconds / 100;

            sw.Restart();
            for (int i = 0; i < 100; i++)
            {
                int at = rng.Next(0, text.Length - 10);
                Doc.GetRange(at, at + 5);
            }

            double range = sw.Elapsed.TotalMilliseconds / 100;

            sw.Restart();
            for (int i = 0; i < 20; i++)
            {
                int at = rng.Next(0, text.Length - 10);
                Doc.Selection.SetRange(at, at);
                Doc.Selection.TypeText("x");
                Doc.GetRange(at, at + 1).Text = string.Empty;
            }

            double insert = sw.Elapsed.TotalMilliseconds / 20;

            Log($"ops {text.Length} chars", $"one property {one:F3} · whole-format clone {cloned:F3} · GetRange only {range:F3} · insert+delete {insert:F2} ms");
        }

        // RTF load: the same 100 KB, plain, styled only in its first 5 KB, and styled throughout.
        string large = Corpus.Large(100_636);
        List<Span> all = SpikeMarkdown.Parse(large);
        int head = large.IndexOf('\n', 5_000);
        var sw2 = Stopwatch.StartNew();
        Doc.SetText(TextSetOptions.FormatRtf, Rtf.FromSpans(large, [], showMarkup: false));
        double plain = sw2.Elapsed.TotalMilliseconds;
        sw2.Restart();
        Doc.SetText(TextSetOptions.FormatRtf, Rtf.FromSpans(large, all.Where(s => s.End <= head).ToList(), showMarkup: false));
        double headOnly = sw2.Elapsed.TotalMilliseconds;
        Log("rtf 100 KB load", $"no runs {plain:F1} · first 5 KB styled {headOnly:F1} ms");

        // The platform's own cost: a character inserted mid-way through 100 KB, TextBox (today's editor) and RichEditBox.
        var textBox = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Text = large };
        _root.Children.Clear();
        _root.Children.Add(textBox);
        await NextFrame();
        await NextFrame();
        var rng2 = new Random(5);
        var times = new List<double>();
        for (int i = 0; i < 20; i++)
        {
            int at = rng2.Next(0, large.Length - 10);
            textBox.Select(at, 0);
            var sw3 = Stopwatch.StartNew();
            textBox.SelectedText = "x";
            times.Add(sw3.Elapsed.TotalMilliseconds);
            textBox.Select(at, 1);
            textBox.SelectedText = string.Empty;
        }

        Log("platform TextBox insert in 100 KB (median/max of 20)", $"{Median(times)}/{times.Max():F2} ms");

        var plainBox = new RichEditBox { TextWrapping = TextWrapping.Wrap };
        _root.Children.Clear();
        _root.Children.Add(plainBox);
        await NextFrame();
        plainBox.Document.SetText(TextSetOptions.None, large);
        await NextFrame();
        times.Clear();
        for (int i = 0; i < 20; i++)
        {
            int at = rng2.Next(0, large.Length - 10);
            plainBox.Document.Selection.SetRange(at, at);
            var sw3 = Stopwatch.StartNew();
            plainBox.Document.Selection.TypeText("x");
            times.Add(sw3.Elapsed.TotalMilliseconds);
            plainBox.Document.GetRange(at, at + 1).Text = string.Empty;
        }

        Log("platform RichEditBox (unstyled) insert in 100 KB (median/max of 20)", $"{Median(times)}/{times.Max():F2} ms");

        _root.Children.Clear();
        _root.Children.Add(_grow ? _scroller : _box);
    }

    /// <summary>
    /// Notes of everyday sizes, styled: RTF load to first frame, a typed character, and the restyle of the edited line,
    /// with markup hidden and with it shown.
    /// </summary>
    private async Task StyledAsync()
    {
        Log("layout mode", _grow ? "grow" : "fixed");
        foreach (int size in new[] { 500, 1_000, 2_500, 5_000, 10_000, 25_000, 50_000, 100_000 })
        {
            string text = Corpus.Large(size);
            List<Span> spans = SpikeMarkdown.Parse(text);
            string[] lines = text.Split('\n');

            foreach (bool show in new[] { false, true })
            {
                _showMarkup = show;
                var loads = new List<double>();
                for (int run = 0; run < 3; run++)
                {
                    Doc.SetText(TextSetOptions.None, string.Empty);
                    await NextFrame();
                    var sw = Stopwatch.StartNew();
                    Doc.SetText(TextSetOptions.FormatRtf, Rtf.FromSpans(text, spans, show));
                    await NextFrame();
                    loads.Add(sw.Elapsed.TotalMilliseconds);
                }

                var type = new List<double>();
                var restyle = new List<double>();
                var rng = new Random(7);
                for (int i = 0; i < 20; i++)
                {
                    int n;
                    do
                    {
                        n = rng.Next(0, lines.Length);
                    }
                    while (!lines[n].StartsWith("Some ", StringComparison.Ordinal));

                    int lineStart = lines.Take(n).Sum(l => l.Length + 1);
                    int at = lineStart + 5;
                    Doc.Selection.SetRange(at, at);
                    var sw = Stopwatch.StartNew();
                    Doc.Selection.TypeText("x");
                    type.Add(sw.Elapsed.TotalMilliseconds);
                    Doc.GetText(TextGetOptions.UseLf, out string now);
                    int end = now.IndexOf('\n', at);
                    var lineSpans = new List<Span>();
                    bool fence = false;
                    SpikeMarkdown.ParseLine(now, lineStart, end, ref fence, lineSpans);
                    sw.Restart();
                    Style(lineStart, end, lineSpans);
                    restyle.Add(sw.Elapsed.TotalMilliseconds);
                    Doc.GetRange(at, at + 1).Text = string.Empty;
                }

                Log($"styled {text.Length} chars, {spans.Count} spans, markup {(show ? "shown" : "hidden")}",
                    $"RTF load to frame {Median(loads)} · type {Median(type)}/{type.Max():F2} · restyle line {Median(restyle)}/{restyle.Max():F2} ms");
            }
        }

        _showMarkup = false;
    }

    /// <summary>
    /// A long note opened head first: the plain text, then the part shown styled by replacing that range with its own
    /// RTF (the same text), then the rest the same way in slices. Checks the text never changes and the caret stays.
    /// </summary>
    private async Task ChunkAsync()
    {
        Log("layout mode", _grow ? "grow" : "fixed");
        foreach (int size in new[] { 10_000, 100_000 })
        {
            string text = Corpus.Large(size);
            List<Span> spans = SpikeMarkdown.Parse(text);
            var plains = new List<double>();
            var plainTyping = new List<double>();
            var firsts = new List<double>();
            var slices = new List<double>();
            bool same = true;
            bool caretKept = true;
            int notHidden = 0;
            double rest = 0;

            for (int run = 0; run < 3; run++)
            {
                Doc.SetText(TextSetOptions.None, string.Empty);
                await NextFrame();

                // The plain source alone, to its first frame: a long note opened unstyled.
                var sw = Stopwatch.StartNew();
                Doc.SetText(TextSetOptions.None, text);
                await NextFrame();
                plains.Add(sw.Elapsed.TotalMilliseconds);
                var rng = new Random(run);
                for (int i = 0; i < 20; i++)
                {
                    int at = rng.Next(0, text.Length - 10);
                    Doc.Selection.SetRange(at, at);
                    var typing = Stopwatch.StartNew();
                    Doc.Selection.TypeText("x");
                    plainTyping.Add(typing.Elapsed.TotalMilliseconds);
                    Doc.GetRange(at, at + 1).Text = string.Empty;
                }

                Doc.SetText(TextSetOptions.None, string.Empty);
                await NextFrame();

                sw.Restart();
                Doc.SetText(TextSetOptions.None, text);
                int head = Boundary(text, 3_000);
                ReplaceWithRtf(text, spans, 0, head);
                await NextFrame();
                firsts.Add(sw.Elapsed.TotalMilliseconds);

                // The caret sits in the head while the rest is styled behind it. Read back where it landed: a
                // position inside hidden markup snaps to the next visible one.
                Doc.Selection.SetRange(100, 100);
                int caret = Doc.Selection.StartPosition;
                sw.Restart();
                for (int from = head; from < text.Length;)
                {
                    int to = Boundary(text, from + 3_000);
                    var slice = Stopwatch.StartNew();
                    ReplaceWithRtf(text, spans, from, to);
                    slices.Add(slice.Elapsed.TotalMilliseconds);
                    from = to;
                }

                rest = sw.Elapsed.TotalMilliseconds;
                caretKept &= Doc.Selection.StartPosition == caret && Doc.Selection.EndPosition == caret;
                Doc.GetText(TextGetOptions.UseLf, out string now);
                same &= now == text;
                notHidden = spans.Count(s => s.Kind == SpanKind.Markup && Doc.GetRange(s.Start, s.End).CharacterFormat.Hidden != FormatEffect.On);
            }

            Log($"chunk {text.Length} chars",
                $"plain only to frame {Median(plains)}, typing in it {Median(plainTyping)}/{plainTyping.Max():F2} · plain + styled head to frame {Median(firsts)} ms · rest styled in {slices.Count / 3} slices of median {Median(slices)}/max {slices.Max():F1} ms, {rest:F0} ms in all · " +
                $"text unchanged:{same} caret kept:{caretKept} markupNotHidden={notHidden}");
        }
    }

    private void ReplaceWithRtf(string text, List<Span> spans, int from, int to)
    {
        string slice = text[from..to];
        var shifted = spans.Where(s => s.Start >= from && s.End <= to).Select(s => s with { Start = s.Start - from }).ToList();
        int start = Doc.Selection.StartPosition, end = Doc.Selection.EndPosition;
        Doc.GetRange(from, to).SetText(TextSetOptions.FormatRtf, Rtf.FromSpans(slice, shifted, _showMarkup));
        Doc.Selection.SetRange(start, end);
    }

    /// <summary>The first line end at or after <paramref name="at"/>, or the end: a slice never splits a line.</summary>
    private static int Boundary(string text, int at)
    {
        if (at >= text.Length)
        {
            return text.Length;
        }

        int end = text.IndexOf('\n', at);
        return end < 0 ? text.Length : end;
    }

    // ---------------------------------------------------------------- read view

    /// <summary>A card's read view: the same spans, built as a RichTextBlock (read-only, so markup is simply omitted).</summary>
    private async Task ReadViewAsync()
    {
        foreach ((string name, string text) in new[] { ("small", Corpus.Small()), ("large", Corpus.Large(100_636)) })
        {
            var frames = new List<double>();
            var builds = new List<double>();
            for (int run = 0; run < 5; run++)
            {
                _root.Children.Clear();
                await NextFrame();
                var sw = Stopwatch.StartNew();
                RichTextBlock block = BuildReadView(text, SpikeMarkdown.Parse(text));
                builds.Add(sw.Elapsed.TotalMilliseconds);
                _root.Children.Add(new ScrollViewer { Content = block });
                await NextFrame();
                frames.Add(sw.Elapsed.TotalMilliseconds);
            }

            Log($"perf read view {name} (median of 5)", $"build {Median(builds)} · first frame {Median(frames)} ms");
        }

        _root.Children.Clear();
        _root.Children.Add(_grow ? _scroller : _box);
        await NextFrame();
    }

    private static RichTextBlock BuildReadView(string text, List<Span> spans)
    {
        var block = new RichTextBlock { TextWrapping = TextWrapping.Wrap, FontSize = BaseSize };
        var kinds = new SpanKind?[text.Length];
        var levels = new int[text.Length];
        foreach (Span s in spans.Where(s => s.Kind != SpanKind.Markup))
        {
            for (int i = s.Start; i < s.End; i++)
            {
                kinds[i] = s.Kind;
                levels[i] = s.Level;
            }
        }

        var hidden = new bool[text.Length];
        foreach (Span s in spans.Where(s => s.Kind == SpanKind.Markup))
        {
            Array.Fill(hidden, true, s.Start, s.Length);
        }

        var paragraph = new Paragraph();
        int run = 0;
        for (int i = 0; i <= text.Length; i++)
        {
            bool boundary = i == text.Length || text[i] == '\n' || hidden[i] || (i > run && (kinds[i] != kinds[i - 1]));
            if (!boundary)
            {
                continue;
            }

            if (i > run && !hidden[run])
            {
                var r = new Run { Text = text[run..i] };
                Decorate(r, kinds[run], levels[run]);
                paragraph.Inlines.Add(r);
            }

            if (i < text.Length && text[i] == '\n')
            {
                block.Blocks.Add(paragraph);
                paragraph = new Paragraph();
            }

            run = i < text.Length && (text[i] == '\n' || hidden[i]) ? i + 1 : i;
        }

        block.Blocks.Add(paragraph);
        return block;
    }

    private static void Decorate(Run r, SpanKind? kind, int level)
    {
        switch (kind)
        {
            case SpanKind.Strong: r.FontWeight = FontWeights.Bold; break;
            case SpanKind.Emphasis or SpanKind.Quote: r.FontStyle = Windows.UI.Text.FontStyle.Italic; break;
            case SpanKind.Strike or SpanKind.TaskDone: r.TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough; break;
            case SpanKind.Underline or SpanKind.Link: r.TextDecorations = Windows.UI.Text.TextDecorations.Underline; break;
            case SpanKind.Code or SpanKind.CodeBlock: r.FontFamily = new FontFamily("Consolas"); break;
            case SpanKind.Heading: r.FontSize = HeadingSize[level]; r.FontWeight = FontWeights.Bold; break;
        }
    }

    // ---------------------------------------------------------------- UI Automation hold

    private void Hold(string dir)
    {
        string text = SpikeMarkdown.NormaliseLineEnds(Corpus.Fidelity[0].Text + "\n" + Corpus.Fidelity[1].Text);
        List<Span> spans = Load(text);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "markdown.txt"), text);
        File.WriteAllText(Path.Combine(dir, "visible.txt"), SpikeMarkdown.Visible(text, spans));
        File.WriteAllText(Path.Combine(dir, "ready"), Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
    }

    // ---------------------------------------------------------------- styling

    private List<Span> Load(string text)
    {
        Doc.SetText(TextSetOptions.None, text);
        List<Span> spans = SpikeMarkdown.Parse(SpikeMarkdown.NormaliseLineEnds(text));
        Style(0, text.Length, spans);
        return spans;
    }

    /// <summary>Resets [from, to) to body text, then applies each span. The host's whole job: no rule is decided here.</summary>
    private void Style(int from, int to, List<Span> spans, bool batch = true)
    {
        if (batch)
        {
            Doc.BatchDisplayUpdates();
        }

        ITextCharacterFormat body = Doc.GetRange(from, to).CharacterFormat;
        body.Bold = FormatEffect.Off;
        body.Italic = FormatEffect.Off;
        body.Hidden = FormatEffect.Off;
        body.Strikethrough = FormatEffect.Off;
        body.Underline = UnderlineType.None;
        body.Size = BaseSize;
        if (_fonts)
        {
            body.Name = "Segoe UI Variable Text";
        }
        body.ForegroundColor = Foreground;
        body.BackgroundColor = Colors.Transparent;

        foreach (Span s in spans)
        {
            ITextCharacterFormat f = Doc.GetRange(s.Start, s.End).CharacterFormat;
            switch (s.Kind)
            {
                case SpanKind.Markup when _showMarkup: f.ForegroundColor = Dim; break;
                case SpanKind.Markup: f.Hidden = FormatEffect.On; break;
                case SpanKind.Strong: f.Bold = FormatEffect.On; break;
                case SpanKind.Emphasis: f.Italic = FormatEffect.On; break;
                case SpanKind.Strike: f.Strikethrough = FormatEffect.On; break;
                case SpanKind.Underline: f.Underline = UnderlineType.Single; break;
                case SpanKind.Mark: f.BackgroundColor = Highlight; break;
                case SpanKind.Code or SpanKind.CodeBlock when _fonts: f.Name = "Consolas"; break;
                case SpanKind.Link: f.ForegroundColor = Accent; f.Underline = UnderlineType.Single; break;
                case SpanKind.Heading: f.Size = HeadingSize[s.Level]; f.Bold = FormatEffect.On; break;
                case SpanKind.Quote: f.Italic = FormatEffect.On; f.ForegroundColor = Dim; break;
                case SpanKind.ListMarker or SpanKind.TaskMarker or SpanKind.Rule: f.ForegroundColor = Dim; break;
                case SpanKind.TaskDone: f.Strikethrough = FormatEffect.On; f.ForegroundColor = Dim; break;
            }
        }

        if (batch)
        {
            Doc.ApplyDisplayUpdates();
        }
    }

    // ---------------------------------------------------------------- helpers

    private string Text(int start, int end)
    {
        Doc.GetRange(start, end).GetText(TextGetOptions.UseLf, out string text);
        return text;
    }

    private static Task NextFrame()
    {
        var done = new TaskCompletionSource();
        EventHandler<object>? handler = null;
        handler = (_, _) =>
        {
            CompositionTarget.Rendering -= handler;
            done.TrySetResult();
        };
        CompositionTarget.Rendering += handler;
        // A hidden or occluded window renders no frame; never wait forever for one.
        return Task.WhenAny(done.Task, Task.Delay(2000));
    }

    private void Log(string key, string value)
    {
        _log.Append(key).Append(": ").AppendLine(value);
        if (_output is not null)
        {
            File.WriteAllText(_output, _log.ToString());
        }
    }

    private static string Ms(Stopwatch sw) => $"{sw.Elapsed.TotalMilliseconds:F1} ms";

    private static string Median(List<double> values)
    {
        var sorted = values.Order().ToList();
        return sorted[sorted.Count / 2].ToString("F2", CultureInfo.InvariantCulture);
    }

    private static string Escape(string s) => s.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");

    private static string? After(string[] argv, string flag)
    {
        int i = Array.IndexOf(argv, flag);
        return i >= 0 && i + 1 < argv.Length ? argv[i + 1] : null;
    }
}
