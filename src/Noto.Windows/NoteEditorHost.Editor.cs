using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Noto;

/// <summary>
/// The editor host's text control (ADR-017 §1): a RichEditBox whose text is the note's markdown source, its markup
/// hidden by format. It carries the theme brushes the host styles with, so a theme switch restyles, and it answers
/// UI Automation's Value pattern with the source, as the TextBox it replaced did.
/// </summary>
internal sealed partial class EditorBox : RichEditBox
{
    public static readonly DependencyProperty MarkupBrushProperty = Brush(nameof(MarkupBrush));
    public static readonly DependencyProperty LinkBrushProperty = Brush(nameof(LinkBrush));
    public static readonly DependencyProperty CodeSurfaceBrushProperty = Brush(nameof(CodeSurfaceBrush));
    public static readonly DependencyProperty MarkBrushProperty = Brush(nameof(MarkBrush));
    public static readonly DependencyProperty MarkTextBrushProperty = Brush(nameof(MarkTextBrush));

    /// <summary>Markup when it is shown (Ctrl+Shift+R), list markers, task boxes, quotes, separators.</summary>
    public Brush? MarkupBrush
    {
        get => (Brush?)GetValue(MarkupBrushProperty);
        set => SetValue(MarkupBrushProperty, value);
    }

    /// <summary>A link's text.</summary>
    public Brush? LinkBrush
    {
        get => (Brush?)GetValue(LinkBrushProperty);
        set => SetValue(LinkBrushProperty, value);
    }

    /// <summary>Behind inline and fenced code.</summary>
    public Brush? CodeSurfaceBrush
    {
        get => (Brush?)GetValue(CodeSurfaceBrushProperty);
        set => SetValue(CodeSurfaceBrushProperty, value);
    }

    /// <summary>Behind a highlight (F8, #151).</summary>
    public Brush? MarkBrush
    {
        get => (Brush?)GetValue(MarkBrushProperty);
        set => SetValue(MarkBrushProperty, value);
    }

    /// <summary>A highlight's text (F8, #151).</summary>
    public Brush? MarkTextBrush
    {
        get => (Brush?)GetValue(MarkTextBrushProperty);
        set => SetValue(MarkTextBrushProperty, value);
    }

    /// <summary>A brush changed, by a theme switch or high contrast: everything is restyled.</summary>
    public event EventHandler? PaletteChanged;

    /// <summary>The source, for UI Automation's Value pattern; set by the host.</summary>
    public Func<string>? ValueSource { get; set; }

    /// <summary>Replaces the source, for UI Automation's Value pattern; set by the host.</summary>
    public Action<string>? ValueSink { get; set; }

    protected override AutomationPeer OnCreateAutomationPeer() => new EditorBoxAutomationPeer(this);

    private static DependencyProperty Brush(string name) =>
        DependencyProperty.Register(name, typeof(Brush), typeof(EditorBox), new PropertyMetadata(null, (d, _) => ((EditorBox)d).PaletteChanged?.Invoke(d, EventArgs.Empty)));
}

/// <summary>
/// RichEditBox's own peer (Text pattern: what a screen reader reads, ADR-017 §6), plus the Value pattern over the
/// markdown source, which the TextBox had and the validation harnesses read and write.
/// </summary>
internal sealed partial class EditorBoxAutomationPeer(EditorBox owner) : RichEditBoxAutomationPeer(owner), IValueProvider
{
    public bool IsReadOnly => false;

    public string Value => owner.ValueSource?.Invoke() ?? string.Empty;

    public void SetValue(string value) => owner.ValueSink?.Invoke(value);

    protected override object GetPatternCore(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.Value ? this : base.GetPatternCore(patternInterface);
}
