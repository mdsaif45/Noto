using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Noto.Core.Workspace;
using Noto.Platform.Windows;
using Noto.UseCases.Workspace;

namespace Noto;

/// <summary>
/// What the settings surface may change, and the one way it changes each (Slice 1).
/// </summary>
/// <param name="Preferences">Every stored preference: read here, written only through its typed setter.</param>
/// <param name="LoginState">Start with Windows, read from Windows itself (#94); <see langword="null"/> when unavailable.</param>
/// <param name="SetLogin">Turns Start with Windows on or off through the same registration the tray uses; returns what Windows has after.</param>
/// <param name="OpenDataFolder">Opens the data folder in Explorer; whether the shell accepted it.</param>
/// <param name="OpenStartupApps">Opens Windows Settings' Startup apps page; whether the shell accepted it.</param>
internal sealed record SettingsContext(
    WorkspacePreferences Preferences,
    Func<LoginStartupState?> LoginState,
    Func<bool, LoginStartupState?> SetLogin,
    Func<bool> OpenDataFolder,
    Func<bool> OpenStartupApps);

/// <summary>
/// The settings surface (Slice 1): shows each preference as it is stored, and writes a change through its one path.
/// </summary>
/// <remarks>
/// <para>
/// <b>It applies nothing itself.</b> A stored preference is written through <see cref="WorkspacePreferences"/>; the
/// store announces the change, and the part of Noto that owns it (the window, the dock, the hotkey) applies it. Start
/// with Windows is not a stored preference at all: it is Windows' own registration, read and set through the same
/// path the tray uses, so there is no second copy of it to disagree.
/// </para>
/// <para>
/// A value that could not be kept is reported and the page shows what is stored again; nothing on screen claims a
/// setting that is not in effect.
/// </para>
/// </remarks>
public sealed partial class SettingsPage : UserControl
{
    private const string NotSaved = "That setting could not be saved, so it is unchanged.";

    private SettingsContext? _context;

    // True while the page shows stored values, so the change events that raises are not taken as the user's.
    private bool _showing;

    public SettingsPage()
    {
        InitializeComponent();
    }

    /// <summary>The user asked to leave Settings: the Back button.</summary>
    public event EventHandler? BackRequested;

    /// <summary>Shows every setting as it is stored now.</summary>
    internal void Show(SettingsContext context, string? hotkeyMessage)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        ShowStored();
        ShowHotkeyMessage(hotkeyMessage);
        ShowNotice(null);
    }

    /// <summary>Puts keyboard focus on the page's first control, in visual order.</summary>
    internal void FocusFirst()
    {
        if (FocusManager.FindFirstFocusableElement(SettingsLayout) is Control first)
        {
            _ = first.Focus(FocusState.Programmatic);
        }
    }

    /// <summary>
    /// The page is one element in UI Automation, SettingsRoot, named Settings, holding its controls. A UserControl has
    /// no automation peer of its own and a panel never has one, so without this the page's identity is invisible
    /// (measured on the frozen Release build: the id on the inner grid was absent from the tree).
    /// </summary>
    protected override Microsoft.UI.Xaml.Automation.Peers.AutomationPeer OnCreateAutomationPeer() =>
        new Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer(this);

    /// <summary>Why the show/hide shortcut is not active, or <see langword="null"/> when it is (or is off by choice).</summary>
    internal void ShowHotkeyMessage(string? message)
    {
        HotkeyMessage.Text = message ?? string.Empty;
        HotkeyMessage.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ShowStored()
    {
        if (_context is null)
        {
            return;
        }

        _showing = true;

        try
        {
            WorkspacePreferences preferences = _context.Preferences;

            Select(SideSetting, preferences.Edge.ToString());
            Select(EscapeSetting, preferences.Escape.ToString());
            Select(ThemeSetting, preferences.Theme.ToString());
            HideOnDeactivationSetting.IsOn = preferences.HideOnDeactivation;
            SingleClickSetting.IsOn = preferences.SingleClickOpen;
            HotkeyEnabledSetting.IsOn = preferences.HotkeyEnabled;
            HotkeyChordText.Text = preferences.HotkeyBinding;
            ShowLogin(_context.LoginState());
        }
        finally
        {
            _showing = false;
        }
    }

    // As the tray shows it (LoginStartupRow): on, off, or turned off in Windows, where Noto may not turn it back on.
    private void ShowLogin(LoginStartupState? state)
    {
        bool wasShowing = _showing;
        _showing = true;

        try
        {
            LoginStartupRow row = LoginStartupRow.For(state ?? LoginStartupState.Off);

            LaunchAtLoginSetting.IsOn = row.Checked;
            LaunchAtLoginSetting.IsEnabled = state is not null && row.Enabled;
            LaunchAtLoginNote.Text = state is null
                ? "Not available for this copy of Noto."
                : "Turned off in Windows Startup apps. Turn it back on there.";
            LaunchAtLoginNote.Visibility = state is null || row.TurnedOffInWindows ? Visibility.Visible : Visibility.Collapsed;
            OpenStartupAppsButton.Visibility = row.TurnedOffInWindows ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            _showing = wasShowing;
        }
    }

    private static void Select(ComboBox box, string tag) =>
        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == tag);

    private static TEnum Selected<TEnum>(ComboBox box)
        where TEnum : struct, Enum =>
        Enum.Parse<TEnum>((string)((ComboBoxItem)box.SelectedItem).Tag);

    /// <summary>Writes one change; a refused or failed write is reported and the stored value shown again.</summary>
    private void Write(Func<WorkspacePreferences, bool> write)
    {
        if (_showing || _context is null)
        {
            return;
        }

        bool kept = write(_context.Preferences);
        ShowNotice(kept ? null : NotSaved);

        if (!kept)
        {
            ShowStored();
        }
    }

    private void ShowNotice(string? notice)
    {
        SettingsNoticeText.Text = notice ?? string.Empty;
        SettingsNoticeText.Visibility = notice is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnSideChanged(object sender, SelectionChangedEventArgs e) =>
        Write(p => SideSetting.SelectedItem is not null && p.SetEdge(Selected<WorkspaceEdge>(SideSetting)));

    private void OnEscapeChanged(object sender, SelectionChangedEventArgs e) =>
        Write(p => EscapeSetting.SelectedItem is not null && p.SetEscape(Selected<EscapeBehavior>(EscapeSetting)));

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e) =>
        Write(p => ThemeSetting.SelectedItem is not null && p.SetTheme(Selected<WorkspaceTheme>(ThemeSetting)));

    private void OnHideOnDeactivationToggled(object sender, RoutedEventArgs e) =>
        Write(p => p.SetHideOnDeactivation(HideOnDeactivationSetting.IsOn));

    private void OnSingleClickToggled(object sender, RoutedEventArgs e) =>
        Write(p => p.SetSingleClickOpen(SingleClickSetting.IsOn));

    private void OnHotkeyEnabledToggled(object sender, RoutedEventArgs e) =>
        Write(p => p.SetHotkeyEnabled(HotkeyEnabledSetting.IsOn));

    private void OnLaunchAtLoginToggled(object sender, RoutedEventArgs e)
    {
        if (_showing || _context is null)
        {
            return;
        }

        // Whatever Windows has afterwards is what is shown, even if it is not what was asked for.
        bool requested = LaunchAtLoginSetting.IsOn;
        LoginStartupState? after = _context.SetLogin(requested);
        ShowLogin(after);
        ShowNotice(after is not null && (after == LoginStartupState.On) == requested ? null : NotSaved);
    }

    private void OnRevealDataFolderClick(object sender, RoutedEventArgs e) =>
        ShowNotice(_context is null || _context.OpenDataFolder() ? null : "The data folder could not be opened.");

    private void OnOpenStartupAppsClick(object sender, RoutedEventArgs e) =>
        ShowNotice(_context is null || _context.OpenStartupApps() ? null : "Windows Settings could not be opened.");

    private void OnBackClick(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);
}
