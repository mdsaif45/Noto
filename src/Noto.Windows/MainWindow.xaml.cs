using Microsoft.UI.Xaml;

namespace Noto;

/// <summary>
/// Bootstrap window. Proves the application starts and renders.
/// </summary>
/// <remarks>
/// The real workspace — the edge-docked drawer — arrives in M2. The window
/// behaviour it needs (edge docking, always-on-top, DPI handling) was validated
/// by the spike for issue #2 and is recorded in ADR-007.
/// </remarks>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
}
