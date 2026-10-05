using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Noto.Platform.Windows;
using Noto.UseCases.Workspace;
using Windows.Graphics;

namespace Noto;

/// <summary>
/// The workspace window's show/hide lifecycle (#16 slice 5, ADR-007 §4).
/// </summary>
/// <remarks>
/// <para>
/// The one place activation requests are carried out. A request is judged by
/// <see cref="WorkspaceToggle.Decide"/> against where the window is now and
/// what the coordinator knows (startup finished, a transition running, the
/// window closing), then carried out synchronously. Requests arrive on the UI
/// thread from the hotkey's message, one at a time, so they are serialized
/// by construction; nothing is queued, and a request that cannot run now is
/// dropped.
/// </para>
/// <para>
/// Deliberately small: it owns this lifecycle for the one window that
/// exists, not window creation in general. The coordinator architecture
/// describes is not built (architecture-overview, implementation status).
/// </para>
/// </remarks>
internal sealed class WindowCoordinator(
    Window window,
    WindowHandle handle,
    DockedWindow docked,
    WorkspacePreferences preferences,
    Func<bool> saveBeforeHide)
{
    private int _readyTime = Environment.TickCount;
    private int? _lastTransitionEnd;
    private bool _transitioning;
    private bool _shuttingDown;

    /// <summary>Startup has finished; requests made before now were made while Noto was launching.</summary>
    public void MarkReady() => _readyTime = Environment.TickCount;

    /// <summary>The window is closing: every request from now on is dropped.</summary>
    public void BeginShutdown() => _shuttingDown = true;

    /// <summary>
    /// Handles one activation request — today, a press of the global hotkey,
    /// handled synchronously inside its <c>WM_HOTKEY</c> so Noto is entitled
    /// to take the foreground.
    /// </summary>
    /// <param name="requestTime">When the request was made, on the <see cref="Environment.TickCount"/> clock.</param>
    /// <returns>What was decided; <see cref="WorkspaceAction.None"/> when the request was dropped.</returns>
    public WorkspaceAction OnActivationRequested(int requestTime)
    {
        WorkspaceAction action = WorkspaceToggle.Decide(
            WindowActivation.PresenceOf(handle),
            new ActivationContext(_shuttingDown, docked.IsResizing, _transitioning, requestTime, _readyTime, _lastTransitionEnd));

        if (action == WorkspaceAction.None)
        {
            return action;
        }

        _transitioning = true;

        try
        {
            switch (action)
            {
                case WorkspaceAction.Hide:
                    Hide();
                    break;

                case WorkspaceAction.Show:
                    window.AppWindow.Show(activateWindow: false);
                    BringForward(redock: true);
                    break;

                case WorkspaceAction.Restore:
                    BringForward(redock: true);
                    break;

                case WorkspaceAction.Focus:
                    BringForward(redock: false);
                    break;
            }
        }
        finally
        {
            _transitioning = false;
            _lastTransitionEnd = Environment.TickCount;
        }

        return action;
    }

    /// <summary>
    /// Hides the window — after saving unsaved editor text once. A failed
    /// save keeps the window shown with the editor's notice, so nothing is
    /// hidden that has not been kept.
    /// </summary>
    private void Hide()
    {
        if (!saveBeforeHide())
        {
            return;
        }

        window.AppWindow.Hide();
    }

    /// <summary>
    /// Ends normal, docked and focused: restored if minimized, re-docked
    /// against the display it is on now, then brought to the foreground.
    /// </summary>
    private void BringForward(bool redock)
    {
        if (!WindowActivation.Restore(handle))
        {
            return;
        }

        if (redock)
        {
            Redock();
        }

        _ = WindowActivation.BringToForeground(handle);
    }

    /// <summary>
    /// Docks to the current display's work area, DPI and remembered width —
    /// read now, not remembered from before the window was hidden.
    /// </summary>
    private void Redock()
    {
        try
        {
            double widthDip = preferences.WidthFor(DisplayMonitors.ForWindow(handle).Id.Value);
            PixelRect outer = WindowDocking.OuterBoundsFor(handle, docked.Edge, widthDip);

            docked.Remember(widthDip);
            docked.MoveOwn(() => window.AppWindow.MoveAndResize(new RectInt32(outer.Left, outer.Top, outer.Width, outer.Height)));
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or COMException)
        {
            // Left where it is: shown and focused, undocked only if Windows
            // could not describe the display. The dock's guard still holds.
            Debug.WriteLine($"Re-docking on show failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
