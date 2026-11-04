using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Noto.Core.Workspace;
using Windows.System;

namespace Noto;

/// <summary>
/// The welcome's surface (ADR-021): shows the tour's current step and moves through it. What the steps say, and how
/// far the tour may move, is <see cref="WelcomeTour"/>'s; recording how it ended is the composition root's.
/// </summary>
public sealed partial class WelcomePage : UserControl
{
    private WelcomeTour? _tour;

    public WelcomePage()
    {
        InitializeComponent();
    }

    /// <summary>The welcome was finished (Get started) or skipped (Skip, Escape).</summary>
    public event EventHandler<WelcomeState>? Ended;

    /// <summary>Shows <paramref name="tour"/> from its current step.</summary>
    internal void Show(WelcomeTour tour)
    {
        _tour = tour ?? throw new ArgumentNullException(nameof(tour));
        ShowStep(announce: false);
    }

    /// <summary>The primary action, Next or Get started: where focus goes back to when Settings opened over it is left.</summary>
    internal Control PrimaryButton => WelcomeNextButton;

    /// <summary>Puts keyboard focus on the primary action, Next or Get started.</summary>
    internal void FocusPrimary() => _ = WelcomeNextButton.Focus(FocusState.Programmatic);

    /// <summary>Ends the welcome: <see cref="WelcomeState.Completed"/> or <see cref="WelcomeState.Skipped"/>.</summary>
    internal void End(WelcomeState outcome) => Ended?.Invoke(this, outcome);

    /// <summary>
    /// The page is one element in UI Automation, WelcomeRoot, named Welcome to Noto, holding its controls: a
    /// UserControl has no automation peer of its own (as SettingsPage).
    /// </summary>
    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);

    /// <summary>
    /// Enter that no button took is the primary action's. A focused button takes its own Enter first, so Enter on
    /// Back goes back and Enter on Skip skips.
    /// </summary>
    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (e.Key == VirtualKey.Enter && !e.Handled)
        {
            e.Handled = true;
            Primary();
            return;
        }

        base.OnKeyDown(e);
    }

    private void ShowStep(bool announce)
    {
        if (_tour is not { } tour)
        {
            return;
        }

        WelcomeStep step = tour.Current;
        WelcomeProgressText.Text = tour.Progress;
        WelcomeProgressBar.Maximum = tour.Steps.Count;
        WelcomeProgressBar.Value = tour.Index + 1;
        WelcomeTitle.Text = step.Title;
        WelcomeSummary.Text = step.Summary;

        WelcomeLines.ItemsSource = step.Lines;

        // Not on the first step, where there is nothing to go back to: absent rather than disabled, so the row does not
        // show a control that does nothing (a disabled subtle button draws a border it has nowhere else).
        WelcomeBackButton.Visibility = tour.IsFirst ? Visibility.Collapsed : Visibility.Visible;
        WelcomeNextButton.Content = tour.PrimaryLabel;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(WelcomeNextButton, tour.PrimaryLabel);

        if (announce)
        {
            // Focus stays on the button that moved, so the new step is said once: where, what, and its one line.
            FrameworkElementAutomationPeer.FromElement(this)?.RaiseNotificationEvent(
                AutomationNotificationKind.Other,
                AutomationNotificationProcessing.ImportantMostRecent,
                $"{tour.Progress}. {step.Title}. {step.Summary}",
                "NotoWelcome");
        }
    }

    private void Primary()
    {
        if (_tour is not { } tour)
        {
            return;
        }

        if (tour.IsLast)
        {
            End(WelcomeState.Completed);
            return;
        }

        _ = tour.Next();
        ShowStep(announce: true);
    }

    private void OnNextClick(object sender, RoutedEventArgs e) => Primary();

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        if (_tour is not { } tour || !tour.Back())
        {
            return;
        }

        ShowStep(announce: true);

        // Back is gone on the first step, and a collapsed button cannot keep focus: it goes to the primary action.
        if (tour.IsFirst)
        {
            FocusPrimary();
        }
    }

    private void OnSkipClick(object sender, RoutedEventArgs e) => End(WelcomeState.Skipped);
}
