using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace Noto;

/// <summary>
/// What Narrator needs that WinUI does not do by itself (#161, measured with Narrator on v0.2): a row's state spoken
/// with the row, and a live region's new text spoken when it changes.
/// </summary>
public static class Accessible
{
    /// <summary>
    /// A row's state ("Pinned", "Folded, 3 lines hidden", a colour), as its help text and its item status. Narrator
    /// reads a list item's name and position but not its help text (measured: "Work, 1 of 2", never "Pinned"); item
    /// status is read with the item.
    /// </summary>
    internal static void SetRowState(DependencyObject row, string state)
    {
        AutomationProperties.SetHelpText(row, state);
        AutomationProperties.SetItemStatus(row, state);
        AutomationProperties.SetFullDescription(row, state);
    }

    /// <summary>
    /// Set on a TextBlock whose <c>AutomationProperties.LiveSetting</c> is Polite or Assertive: each new text is
    /// announced. WinUI does not raise LiveRegionChanged when a TextBlock's text changes (measured: Search's
    /// "No notes or folders match." was never spoken), so this does.
    /// </summary>
    public static readonly DependencyProperty AnnouncesProperty =
        DependencyProperty.RegisterAttached("Announces", typeof(bool), typeof(Accessible), new PropertyMetadata(false, OnAnnouncesChanged));

    public static bool GetAnnounces(DependencyObject element) => (bool)element.GetValue(AnnouncesProperty);

    public static void SetAnnounces(DependencyObject element, bool value) => element.SetValue(AnnouncesProperty, value);

    private static void OnAnnouncesChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is TextBlock text && e.NewValue is true)
        {
            _ = text.RegisterPropertyChangedCallback(TextBlock.TextProperty, (sender, _) =>
            {
                if (sender is TextBlock changed && changed.Text.Length > 0)
                {
                    (FrameworkElementAutomationPeer.FromElement(changed) ?? FrameworkElementAutomationPeer.CreatePeerForElement(changed))?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
                }
            });
        }
    }
}
