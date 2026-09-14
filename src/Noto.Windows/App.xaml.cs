using Microsoft.UI.Xaml;

namespace Noto;

/// <summary>
/// Application entry point and composition root.
/// </summary>
/// <remarks>
/// <para>
/// This is a <b>bootstrap only</b>. No Noto feature is implemented: no notes,
/// no storage, no sidebar, no editor. Those arrive in M1 and M2.
/// </para>
/// <para>
/// Staged startup (see <c>architecture-overview.md</c>) is not implemented
/// either — there is nothing yet to defer.
/// </para>
/// </remarks>
public partial class App : Application
{
    private Window? _window;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();

        // Lets a scripted or CI run verify startup without a human closing the
        // window. Not a product feature.
        if (Environment.GetCommandLineArgs().Contains("--smoke-test"))
        {
            _ = _window.DispatcherQueue.TryEnqueue(Exit);
        }
    }
}
