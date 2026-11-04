using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Noto.Core.Workspace;
using Windows.System;

namespace Noto;

/// <summary>The welcome (ADR-021): opened at the first start of a new workspace, and from Settings > Help.</summary>
public sealed partial class MainWindow
{
    // The surface the welcome was opened from, and what had focus there: where leaving it goes back to.
    private UIElement? _welcomeReturn;
    private Control? _welcomeReturnFocus;

    /// <summary>
    /// The welcome tour to show, with the shortcut as it is now. Set once by the composition root; without it, the
    /// welcome does not open.
    /// </summary>
    internal Func<WelcomeTour>? WelcomeTour { get; set; }

    /// <summary>How the welcome ended, Completed or Skipped, for the composition root to record.</summary>
    internal Action<WelcomeState>? WelcomeEnded { get; set; }

    /// <summary>Open the welcome once the folder list has first loaded: this start is a new workspace's first.</summary>
    internal bool WelcomeAtStart { get; set; }

    /// <summary>The welcome while it is open; <see langword="null"/> otherwise. At most one exists.</summary>
    private WelcomePage? WelcomeSurface { get; set; }

    /// <summary>
    /// Opens the welcome over whichever surface is showing, which keeps its state, and puts focus on its primary
    /// action. Already open (Settings was opened from it): Settings is left and the welcome is in front again.
    /// </summary>
    internal void OpenWelcome()
    {
        if (WelcomeTour is null)
        {
            return;
        }

        if (WelcomeSurface is { } open)
        {
            CloseSettings();
            open.FocusPrimary();
            return;
        }

        UIElement from = SettingsSurface is { } settings ? settings
            : RecycleBinSurface is { } bin ? bin
            : SearchSurface is { } search ? search
            : NoteRoot.Visibility == Visibility.Visible ? NoteRoot
            : PaneRoot;

        _welcomeReturn = from;
        _welcomeReturnFocus = Content?.XamlRoot is { } root ? FocusManager.GetFocusedElement(root) as Control : null;

        var page = new WelcomePage();
        Grid.SetRow(page, Grid.GetRow(PaneRoot));
        page.KeyDown += OnWelcomeKeyDown;
        page.Ended += OnWelcomeEnded;
        ((Panel)PaneRoot.Parent).Children.Add(page);
        WelcomeSurface = page;

        page.Show(WelcomeTour());
        from.Visibility = Visibility.Collapsed;
        page.Visibility = Visibility.Visible;
        Motion.EnterSurface(page, forward: true);

        // After layout: an element made visible this frame cannot take focus until it has been measured.
        _ = DispatcherQueue.TryEnqueueGuarded(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, page.FocusPrimary);
        Trace.WriteLine("Noto.Welcome opened");
    }

    /// <summary>Leaves the welcome for the surface it was opened from, with focus back where it was.</summary>
    private void CloseWelcome()
    {
        if (WelcomeSurface is not { } page)
        {
            return;
        }

        UIElement back = _welcomeReturn ?? PaneRoot;
        back.Visibility = Visibility.Visible;
        page.Visibility = Visibility.Collapsed;
        Motion.EnterSurface(back, forward: false);

        ((Panel)page.Parent).Children.Remove(page);
        page.KeyDown -= OnWelcomeKeyDown;
        page.Ended -= OnWelcomeEnded;
        WelcomeSurface = null;

        Control restore = _welcomeReturnFocus is { IsLoaded: true, Visibility: Visibility.Visible } previous
            ? previous
            : back == NoteRoot ? NoteList
            : back is SettingsPage settings ? settings.HelpButton
            : Folders.Count > 0 ? FolderList
            : FolderNameInput;

        _welcomeReturn = null;
        _welcomeReturnFocus = null;
        _ = DispatcherQueue.TryEnqueueGuarded(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => restore.Focus(FocusState.Programmatic));
        Trace.WriteLine("Noto.Welcome closed");
    }

    private void OnSettingsWelcomeRequested(object? sender, EventArgs e) => OpenWelcome();

    private void OnWelcomeEnded(object? sender, WelcomeState outcome)
    {
        CloseWelcome();
        WelcomeEnded?.Invoke(outcome);
        Trace.WriteLine($"Noto.Welcome ended {outcome}");
    }

    /// <summary>Escape puts the welcome away as Skip does: it is not opened again at start.</summary>
    private void OnWelcomeKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape && WelcomeSurface is { } page)
        {
            e.Handled = true;
            page.End(WelcomeState.Skipped);
        }
    }

    // After the first load, so the folder list's own first focus has already happened and the welcome takes it.
    private void OpenWelcomeAtStart()
    {
        if (WelcomeAtStart)
        {
            WelcomeAtStart = false;
            OpenWelcome();
        }
    }
}
