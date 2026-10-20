using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// Slice 1.2b in the source: the hotkey keeps one owner, Settings reaches it only through delegates, the recorder
/// suspends the chord only while it listens and gives it back on every way out, and the rebind runs the tested
/// procedure with the owner's own effects. What the running application does is the settings harness's to prove.
/// </summary>
public sealed class HotkeyRebindGuardTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly string Windows = Path.GetDirectoryName(SourcePath("src", "Noto.Windows", "App.xaml.cs"))!;
    private static readonly string App = Code(Path.Join(Windows, "App.xaml.cs"));
    private static readonly string Main = Code(Path.Join(Windows, "MainWindow.xaml.cs"));
    private static readonly string Page = Code(Path.Join(Windows, "SettingsPage.xaml.cs"));
    private static readonly XDocument PageXaml = XDocument.Load(Path.Join(Windows, "SettingsPage.xaml"));

    // ---------------------------------------------------------------- one owner

    [Fact]
    public void The_hotkey_has_one_owner_and_settings_never_touches_windows_directly()
    {
        // Every registration is made in one place in the composition root; the settings page and the window make none.
        string allWindows = string.Join('\n', Directory.EnumerateFiles(Windows, "*.cs").Select(Code));
        Assert.Equal(1, Regex.Count(allWindows, @"GlobalHotkey\.Create\(\)"));
        Assert.Contains("GlobalHotkey.Create()", Body(App, "RegisterHotkey"), StringComparison.Ordinal);

        string[] forbidden = ["GlobalHotkey", "RegisterHotKey", "UnregisterHotKey", "NativeMethods", "DllImport", "Registry", "LoginRegistration", "DockedWindow", "TrayIcon"];
        var direct = forbidden.Where(f => Page.Contains(f, StringComparison.Ordinal)).ToList();
        Assert.True(direct.Count == 0, "The settings page reaches past its owners: " + string.Join(", ", direct));

        // Start-up and rebind share the one registration path.
        Assert.Contains("RegisterHotkey(chord)", Body(App, "StartHotkey"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_rebind_runs_the_tested_procedure_with_the_owners_own_effects()
    {
        string rebind = Body(App, "RebindHotkey");

        Assert.Matches(@"HotkeyRebinding\.Rebind\(\s*candidate,\s*current,\s*_hotkey,\s*RegisterHotkey,\s*static old => old\.Dispose\(\),\s*preferences\.SetHotkeyBinding\);", rebind);
        Assert.Contains("_hotkey = result.Active;", rebind, StringComparison.Ordinal);
        Assert.Matches(@"if \(!preferences\.HotkeyEnabled\)\s*\{\s*return \(HotkeyRebindOutcome\.Off,", rebind);

        // A refused chord leaves no registration behind: the instance is disposed before returning.
        string register = Body(App, "RegisterHotkey");
        Assert.Matches(@"if \(!registration\.IsRegistered\)\s*\{[\s\S]*?return \(registration, null\);", register);
        Assert.Contains("hotkey?.Dispose();", register, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- suspension

    [Fact]
    public void A_press_is_ignored_only_while_recording_and_only_while_noto_is_in_front()
    {
        string pressed = Body(App, "OnHotkeyPressed");

        Assert.Matches(@"if \(_hotkeySuspended && _windowHandle is not null && WindowActivation\.PresenceOf\(_windowHandle\) == WorkspacePresence\.Foreground\)\s*\{[^}]*return;", pressed);
        Assert.True(pressed.IndexOf("_hotkeySuspended", StringComparison.Ordinal) < pressed.IndexOf("OnActivationRequested", StringComparison.Ordinal));
    }

    [Fact]
    public void Recording_suspends_the_chord_and_every_way_out_gives_it_back()
    {
        Assert.Matches(@"_recording = true;\s*_context\.SuspendHotkey\(true\);", Body(Page, "OnRecorderClick"));

        // The one way out, and the chord given back in a finally, so even a failing rebind restores it.
        Assert.Matches(@"_recording = false;\s*try\s*\{[\s\S]*\}\s*finally\s*\{[\s\S]*finally\s*\{\s*_context\?\.SuspendHotkey\(false\);", Body(Page, "EndRecording"));

        string keys = Body(Page, "OnRecorderKeyDown");
        Assert.Contains("e.Handled = true;", keys, StringComparison.Ordinal);

        // Every key that reaches the page is read here, as it always was.
        Assert.Matches(@"e\.Handled = true;\s*Record\(\(int\)e\.Key, Held\(\)\);\s*\}$", keys);

        string record = Body(Page, "Record");
        Assert.Matches(@"key == \(int\)global::Windows\.System\.VirtualKey\.Escape && held == HotkeyModifiers\.None\)\s*\{\s*EndRecording\(", record);

        // A chord is tried, and recording ends whatever happens: refused, announced, or thrown.
        Assert.Matches(@"case RecordedKind\.Chord:[\s\S]*?try\s*\{\s*\(HotkeyRebindOutcome _, message\) = _context\.RebindHotkey\(chord\);\s*\}\s*finally\s*\{\s*EndRecording\(message\);\s*\}", record);

        Assert.Contains("private void OnRecorderLostFocus(object sender, RoutedEventArgs e) => CancelRecording();", Page, StringComparison.Ordinal);
        Assert.Matches(@"if \(_recording\)\s*\{\s*EndRecording\(", Body(Page, "CancelRecording"));
        Assert.Matches(@"if \(_recording\)\s*\{\s*args\.Handled = true;", Body(Page, "OnRecorderAccelerators"));

        // Losing activation and closing Settings cancel too.
        Assert.Matches(@"WindowActivationState\.Deactivated\)\s*\{\s*SettingsSurface\?\.CancelRecording\(\);", Main);
        Assert.Contains("page.CancelRecording();", Body(Main, "CloseSettings"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_held_chord_is_observed_below_every_applications_shortcuts_for_one_recording_only()
    {
        // Measured: a chord another application holds is delivered to it, never to this page. While the recorder listens,
        // the keys are observed below the applications' shortcuts and read once, on the UI thread.
        Assert.Matches(
            @"_recording = true;\s*_context\.SuspendHotkey\(true\);[\s\S]*_recorderKeys = _context\.ObserveRecorderKeys\(\(key, held\) => _ = DispatcherQueue\.TryEnqueue\(\(\) => Record\(key, held\)\)\);\s*\}$",
            Body(Page, "OnRecorderClick"));

        // Ended with the recording, on every route (Escape, a chord, focus lost, deactivation, Settings closed all reach
        // EndRecording), before the chord is given back, and even if ending throws.
        Assert.Matches(
            @"finally\s*\{\s*IDisposable\? keys = _recorderKeys;\s*_recorderKeys = null;\s*try\s*\{\s*keys\?\.Dispose\(\);\s*\}\s*finally\s*\{\s*_context\?\.SuspendHotkey\(false\);",
            Body(Page, "EndRecording"));
        Assert.Equal(2, Regex.Count(Page, @"_recorderKeys = "));

        // One place starts it: the composition root, for the settings context, with the workspace window as its owner.
        string allWindows = string.Join('\n', Directory.EnumerateFiles(Windows, "*.cs").Select(Code));
        Assert.Equal(1, Regex.Count(allWindows, @"RecorderKeyboard\.Start\("));
        Assert.Contains("pressed => ObserveRecorderKeys(pressed, preferences)", App, StringComparison.Ordinal);

        // Only a chord the window will never receive is passed on: one that cannot be registered now. Every other key
        // reaches the page and is read there once; Noto's own registered chord stays the hotkey's, ignored while
        // recording, as the rebind's "same chord, registered" rule has it.
        string observe = Body(App, "ObserveRecorderKeys");
        Assert.Matches(@"return RecorderKeyboard\.Start\(_windowHandle, \(key, held\) =>", observe);
        Assert.Matches(
            @"if \(HotkeyRecording\.Read\(key, held\) is not \(RecordedKind\.Chord, HotkeyChord chord\)\)\s*\{\s*return;\s*\}\s*bool own = _hotkey is \{ IsRegistered: true \}\s*&& HotkeyChord\.TryParse\(preferences\.HotkeyBinding, out HotkeyChord current\)\s*&& chord == current;\s*if \(!own && !GlobalHotkey\.IsFree\(chord\)\)\s*\{\s*pressed\(key, held\);\s*\}",
            observe);
        Assert.Equal(1, Regex.Count(allWindows, @"GlobalHotkey\.IsFree\("));
    }

    [Fact]
    public void A_successful_rebind_shows_the_new_chord_at_once()
    {
        // The recorder reads back what is stored, which after a rebind is the chord now registered.
        Assert.Contains("ShowShortcut(_context.Preferences);", Body(Page, "EndRecording"), StringComparison.Ordinal);
        Assert.Contains("HotkeyRecorder.IsEnabled = preferences.HotkeyEnabled;", Body(Page, "ShowShortcut"), StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- accessibility

    [Fact]
    public void The_recorder_has_a_name_instructions_and_an_announced_status()
    {
        XElement recorder = Named("HotkeyRecorder");
        XElement status = Named("HotkeyRecorderStatus");

        Assert.False(string.IsNullOrWhiteSpace((string?)recorder.Attribute("AutomationProperties.Name")));
        Assert.Contains("Escape cancels", (string?)recorder.Attribute("AutomationProperties.HelpText") ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal("Assertive", (string?)status.Attribute("AutomationProperties.LiveSetting"));
        Assert.Null(status.Attribute("AutomationProperties.Name"));

        // The current chord is part of the recorder's name, kept current.
        Assert.Contains("AutomationProperties.SetName(HotkeyRecorder, $\"Show and hide shortcut, {shown}. Select to change it.\");", Page, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- helpers

    private static XElement Named(string name) => PageXaml.Descendants().Single(e => (string?)e.Attribute(X + "Name") == name);

    private static string Code(string path) => Regex.Replace(File.ReadAllText(path), @"^\s*//.*$", string.Empty, RegexOptions.Multiline);

    private static string Body(string source, string method)
    {
        var declaration = Regex.Match(source, $@"(private|internal|public)( static)? [\w?<>,()\s]+ {method}\(");
        Assert.True(declaration.Success, $"Not found: {method}");
        int open = source.IndexOf('{', declaration.Index);
        int depth = 0;

        for (int i = open; i < source.Length; i++)
        {
            depth += source[i] == '{' ? 1 : source[i] == '}' ? -1 : 0;

            if (depth == 0)
            {
                return source[open..(i + 1)];
            }
        }

        throw new InvalidOperationException($"Unbalanced body for {method}");
    }

    /// <summary>Walks up to the repository root; a guard that silently skips is worse than none.</summary>
    private static string SourcePath(params string[] parts)
    {
        string relative = Path.Join(parts);

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Join(directory.FullName, relative);

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Could not locate {relative} by walking up from {AppContext.BaseDirectory}");
    }
}
