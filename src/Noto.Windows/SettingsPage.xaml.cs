using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Noto.Core.Activation;
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
/// <param name="RebindHotkey">Rebinds the show/hide hotkey through its owner, in the safe order; how it ended and what to announce.</param>
/// <param name="SuspendHotkey">Has the hotkey's owner ignore the chord (true) or act on it again (false) while the recorder listens.</param>
/// <param name="ObserveRecorderKeys">Reports, until disposed, each chord pressed while Noto is in front that another application holds, which never reaches the page; <see langword="null"/> when that is unavailable.</param>
internal sealed record SettingsContext(
    WorkspacePreferences Preferences,
    Func<LoginStartupState?> LoginState,
    Func<bool, LoginStartupState?> SetLogin,
    Func<bool> OpenDataFolder,
    Func<bool> OpenStartupApps,
    Func<HotkeyChord, (HotkeyRebindOutcome Outcome, string Message)> RebindHotkey,
    Action<bool> SuspendHotkey,
    Func<Action<int, HotkeyModifiers>, IDisposable?> ObserveRecorderKeys);

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

    // True while the shortcut recorder listens for a chord.
    private bool _recording;

    // The chords observed below every application's shortcuts while recording; null when not recording, or unavailable.
    private IDisposable? _recorderKeys;

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
            ShowShortcut(preferences);
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

    private void OnHotkeyEnabledToggled(object sender, RoutedEventArgs e)
    {
        Write(p => p.SetHotkeyEnabled(HotkeyEnabledSetting.IsOn));

        if (_context is not null)
        {
            ShowShortcut(_context.Preferences);
        }
    }

    // The stored chord, on the description line and in the recorder's text and name; the recorder only while the
    // shortcut is on, since an off shortcut has nothing to rebind.
    private void ShowShortcut(WorkspacePreferences preferences)
    {
        string shown = HotkeyChord.TryParse(preferences.HotkeyBinding, out HotkeyChord chord) ? HotkeyRebinding.Show(chord) : preferences.HotkeyBinding;

        HotkeyChordText.Text = shown;
        HotkeyRecorder.Content = $"{shown}   Change";
        HotkeyRecorder.IsEnabled = preferences.HotkeyEnabled;
        AutomationProperties.SetName(HotkeyRecorder, $"Show and hide shortcut, {shown}. Select to change it.");
    }

    /// <summary>Stops the recorder if it is listening, as if Escape were pressed: the window lost activation, or Settings closed.</summary>
    internal void CancelRecording()
    {
        if (_recording)
        {
            EndRecording("Cancelled. The shortcut is unchanged.");
        }
    }

    private void OnRecorderClick(object sender, RoutedEventArgs e)
    {
        if (_recording || _context is null || !_context.Preferences.HotkeyEnabled)
        {
            return;
        }

        _recording = true;
        _context.SuspendHotkey(true);
        HotkeyRecorder.Content = "Press the new shortcut…";
        ShowRecorderStatus("Recording. Press the new shortcut, or Escape to cancel.");

        // A chord another application holds is delivered to it and never reaches this page (measured), so it could not
        // be tried and refused. While listening, such a chord is observed below every application's shortcuts, for this
        // recording only, and queued here; every other key reaches this page and is read as it always was.
        _recorderKeys = _context.ObserveRecorderKeys((key, held) => _ = DispatcherQueue.TryEnqueue(() => Record(key, held)));
    }

    /// <summary>While recording, every key is the recorder's: nothing else in Noto acts on it.</summary>
    private void OnRecorderKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_recording || _context is null)
        {
            return;
        }

        e.Handled = true;
        Record((int)e.Key, Held());
    }

    /// <summary>One key press while recording: a modifier, a key that cannot be used, Escape, or a chord to try.</summary>
    private void Record(int key, HotkeyModifiers held)
    {
        if (!_recording || _context is null)
        {
            return;
        }

        if (key == (int)global::Windows.System.VirtualKey.Escape && held == HotkeyModifiers.None)
        {
            EndRecording("Cancelled. The shortcut is unchanged.");
            return;
        }

        (RecordedKind kind, HotkeyChord chord) = HotkeyRecording.Read(key, held);

        switch (kind)
        {
            case RecordedKind.Modifier:
                HotkeyRecorder.Content = HotkeyRecording.Partial(held);
                break;

            case RecordedKind.Unsupported:
                ShowRecorderStatus("That key cannot be part of a shortcut. Use a letter, a digit, F1 to F24 or Space, or Escape to cancel.");
                break;

            case RecordedKind.NeedsModifier:
                ShowRecorderStatus("A shortcut needs Ctrl, Alt or Win as well. Try again, or Escape to cancel.");
                break;

            case RecordedKind.Chord:
                // Tried in the safe order; refused (another application holds it), the old chord stays and is announced.
                // Recording ends whatever happens, so the observation never outlives an exception.
                string message = "Cancelled. The shortcut is unchanged.";

                try
                {
                    (HotkeyRebindOutcome _, message) = _context.RebindHotkey(chord);
                }
                finally
                {
                    EndRecording(message);
                }

                break;
        }
    }

    // Leaving the recorder by any route (Tab, a click elsewhere) cancels: the old chord is never left suspended.
    private void OnRecorderLostFocus(object sender, RoutedEventArgs e) => CancelRecording();

    // Noto's own shortcuts (Ctrl+W, Ctrl+,) are chords the user may be recording: while listening, none of them acts.
    private void OnRecorderAccelerators(UIElement sender, ProcessKeyboardAcceleratorEventArgs args)
    {
        if (_recording)
        {
            args.Handled = true;
        }
    }

    /// <summary>Stops listening, gives the chord back to its owner, and shows what is stored and what happened.</summary>
    private void EndRecording(string message)
    {
        _recording = false;

        try
        {
            if (_context is not null)
            {
                ShowShortcut(_context.Preferences);
            }

            ShowRecorderStatus(message);
        }
        finally
        {
            // The observation ends with the recording, by every route, before the chord is given back.
            IDisposable? keys = _recorderKeys;
            _recorderKeys = null;

            try
            {
                keys?.Dispose();
            }
            finally
            {
                _context?.SuspendHotkey(false);
            }
        }
    }

    private void ShowRecorderStatus(string message)
    {
        HotkeyRecorderStatus.Text = message;
        HotkeyRecorderStatus.Visibility = Visibility.Visible;
    }

    private static HotkeyModifiers Held()
    {
        static bool Down(global::Windows.System.VirtualKey key) =>
            InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(global::Windows.UI.Core.CoreVirtualKeyStates.Down);

        HotkeyModifiers held = HotkeyModifiers.None;

        if (Down(global::Windows.System.VirtualKey.Control))
        {
            held |= HotkeyModifiers.Ctrl;
        }

        if (Down(global::Windows.System.VirtualKey.Menu))
        {
            held |= HotkeyModifiers.Alt;
        }

        if (Down(global::Windows.System.VirtualKey.Shift))
        {
            held |= HotkeyModifiers.Shift;
        }

        if (Down(global::Windows.System.VirtualKey.LeftWindows) || Down(global::Windows.System.VirtualKey.RightWindows))
        {
            held |= HotkeyModifiers.Win;
        }

        return held;
    }

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
