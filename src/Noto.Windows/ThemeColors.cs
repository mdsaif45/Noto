using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Noto.Core.Themes;
using Noto.Infrastructure.Storage;
using Noto.Platform.Windows;
using Windows.Storage.Pickers;
using WinRT.Interop;
using static Noto.Core.Themes.ThemePalette;
using Color = Windows.UI.Color;

namespace Noto;

/// <summary>The result of checking a picked theme file for install (#269): its bytes and theme, or why it is refused.</summary>
internal sealed record ThemeCheck(byte[]? Bytes, ThemeFile? Theme, string? Refusal);

/// <summary>
/// Theme files at run time (#269, ADR-023 §6, §8): their colours set on the design system's own brushes, and their
/// install, listing, reveal and removal for Settings › Appearance › Theme.
/// </summary>
/// <remarks>
/// <para>
/// <b>Noto changes the colour of the brushes it already has.</b> At start, before any theme, the colour of every brush a
/// theme can reach is recorded, in Colors.xaml's Light and Dark dictionaries; a theme sets <c>SolidColorBrush.Color</c>
/// on those same objects, Light's from its light colours and Dark's from its dark ones, and Default sets the recorded
/// colours back. Every element holding the brush repaints, popups and style setters included; nothing is added to or
/// removed from a dictionary, and no key is resolved again. Colors.xaml is merged by several style dictionaries, so each
/// instance of it is found and set alike.
/// </para>
/// <para>
/// <b>High contrast is never touched</b>: its dictionary is never read or written, so while it is on the user's palette
/// is what is drawn, and turning it off shows the theme again.
/// </para>
/// <para>
/// <b>Switch colours stay colours.</b> ADR-023 §6 asks for the colour-typed aliases to become brush aliases, but WinUI's
/// switch template animates those six keys as colours, and given a brush it draws no outline (DesignSystemGuardTests,
/// measured). So their values are replaced in the dictionary instead: a switch templated after a theme is applied (the
/// next Settings opened) takes them.
/// </para>
/// </remarks>
internal sealed class ThemeColors
{
    /// <summary>ADR-023 §2: the brushes each palette key sets, in Light and Dark alike.</summary>
    private static readonly (string Key, string[] Brushes)[] BrushTargets =
    [
        ("background", ["SurfaceSidebarBrush", "SurfaceWindowBrush"]),
        ("card", ["SurfaceCardBrush", "SurfaceCardSelectedBrush", "SurfaceInputBrush"]),
        ("code", ["SurfaceCodeBrush"]),
        ("text", ["TextPrimaryBrush", "InteractionTextBrush", "InteractionSelectedTextBrush"]),
        ("textSecondary", ["TextSecondaryBrush"]),
        ("textDisabled", ["TextDisabledBrush"]),
        ("link", ["TextLinkBrush"]),
        ("quote", ["TextQuoteBrush"]),
        ("checkmark", ["TaskMarkBrush"]),
        ("danger", ["TextDangerBrush"]),
        ("accent", ["SelectionIndicatorBrush", "ToggleSwitchFillOnPointerOver", "ToggleSwitchFillOnPressed"]),
        ("border", ["BorderSubtleBrush"]),
        ("highlight", ["MarkBrush"]),
        ("highlightText", ["MarkTextBrush"]),
        (Hover, ["InteractionHoverBrush"]),
        (Pressed, ["InteractionPressedBrush"]),
        (Selected, ["InteractionSelectedBrush"]),
        (CardHover, ["SurfaceCardHoverBrush"]),
        .. ThemeFile.Notes.Select((n, i) => ($"{n}.tint", new[] { $"Note{i + 1}TintBrush" })),
        .. ThemeFile.Notes.Select((n, i) => ($"{n}.marker", new[] { $"Note{i + 1}MarkerBrush" })),
    ];

    /// <summary>The switch's colour-animated keys and the palette key each takes (Colors.xaml's aliases, by role).</summary>
    private static readonly (string Key, string Entry)[] ColourTargets =
    [
        (Hover, "ToggleSwitchFillOffPointerOver"),
        (Pressed, "ToggleSwitchFillOffPressed"),
        ("background", "ToggleSwitchFillOffDisabled"),
        ("text", "ToggleSwitchStrokeOffPointerOver"),
        ("text", "ToggleSwitchStrokeOffPressed"),
        ("textDisabled", "ToggleSwitchStrokeOffDisabled"),
    ];

    private readonly ThemeStore _store;
    private readonly Func<nint> _window;
    private readonly List<Target> _targets = [];
    private readonly Dictionary<ThemeMode, Dictionary<string, ThemeColor>> _own = [];
    private readonly HashSet<Target> _written = [];

    /// <summary>Records Noto's own colours; call once, before any theme and before the window is shown.</summary>
    /// <param name="store">The installed theme files.</param>
    /// <param name="window">The window the picker and the Recycle Bin's warning belong to.</param>
    public ThemeColors(ThemeStore store, Func<nint> window)
    {
        _store = store;
        _window = window;

        foreach (ResourceDictionary colours in ColourDictionaries(Application.Current.Resources, new(ReferenceEqualityComparer.Instance)))
        {
            foreach (ThemeMode mode in Enum.GetValues<ThemeMode>())
            {
                var dictionary = (ResourceDictionary)colours.ThemeDictionaries[mode.ToString()];
                foreach ((string key, string[] brushes) in BrushTargets)
                {
                    _targets.AddRange(brushes.Where(b => ColourOf(dictionary, b) is not null).Select(b => new Target(dictionary, b, key, mode, ColourOf(dictionary, b)!.Value)));
                }

                _targets.AddRange(ColourTargets.Where(t => ColourOf(dictionary, t.Entry) is not null).Select(t => new Target(dictionary, t.Entry, t.Key, mode, ColourOf(dictionary, t.Entry)!.Value)));

                // Noto's own palette for the rules, from the first Colors.xaml: each key's first brush, and the fixed focus
                // ring's two tones.
                _own.TryAdd(mode, new Dictionary<string, ThemeColor>(
                    BrushTargets.Select(t => (t.Key, ColourOf(dictionary, t.Brushes[0])))
                        .Append((Focus, ColourOf(dictionary, "BorderFocusBrush")))
                        .Append((FocusInner, ColourOf(dictionary, "BorderFocusInnerBrush")))
                        .Where(o => o.Item2 is not null)
                        .Select(o => KeyValuePair.Create(o.Item1, Of(o.Item2!.Value)))));
            }
        }
    }

    /// <summary>A theme's colours were set on the brushes: whatever copies them (the editor's RTF, a card's runs) draws again.</summary>
    public static event EventHandler? Changed;

    /// <summary>What the last <see cref="Apply"/> has to say (the file couldn't be read, or colours fell back), or <see langword="null"/>.</summary>
    public string? Notice { get; private set; }

    /// <summary>
    /// Draws Noto in the installed theme <paramref name="file"/>, or its own colours for an empty one (Default). A file
    /// that can't be read leaves Default; each colour that makes a pair unreadable, or can't be read, is Noto's own
    /// (ADR-023 §4). Read now, never watched: an edit shows when the theme is chosen again or at the next start.
    /// </summary>
    /// <param name="file">The stored <c>appearance.theme-file</c>.</param>
    /// <param name="drawer">The drawer, to report whether it holds the very brushes set (ADR-023's assumption).</param>
    public void Apply(string file, FrameworkElement drawer)
    {
        if (_own.Count < 2)
        {
            Trace.WriteLine("Noto.Theme refused reason=no-design-system");
            return;
        }

        ThemeFile? theme = null;
        string? refusal = null;
        if (file.Length > 0)
        {
            theme = _store.Read(file) is { } bytes ? ThemeFile.Read(bytes, out refusal) : null;
            refusal ??= theme is null ? "unreadable" : null;
        }

        int fallbacks = theme?.Errors.Count ?? 0;
        var palettes = new Dictionary<ThemeMode, (Dictionary<string, ThemeColor> Palette, HashSet<string> Touched)>();
        foreach (ThemeMode mode in Enum.GetValues<ThemeMode>())
        {
            (Dictionary<string, ThemeColor> palette, Dictionary<string, ThemeColor> kept, int dropped) =
                Fallback(_own[mode], theme?.ColoursFor(mode) ?? [], mode);
            fallbacks += dropped;
            HashSet<string> touched = [.. kept.Keys];
            if (kept.ContainsKey("background") || kept.ContainsKey("card") || kept.ContainsKey("text"))
            {
                touched.UnionWith([Hover, Pressed, Selected, CardHover]);
            }

            palettes[mode] = (palette, touched);
        }

        // Only what a theme sets is written, and only what was written is set back: a brush no theme reached keeps its
        // own reference (the Windows accent follows Windows).
        foreach (Target target in _targets)
        {
            (Dictionary<string, ThemeColor> palette, HashSet<string> touched) = palettes[target.Mode];
            if (touched.Contains(target.Key))
            {
                Set(target, palette[target.Key].ToColor());
                _written.Add(target);
            }
            else if (_written.Remove(target))
            {
                Set(target, target.Own);
            }
        }

        NotePalette.Rename(theme?.NoteNamesFor(ThemeMode.Light) ?? [], theme?.NoteNamesFor(ThemeMode.Dark) ?? []);

        string name = theme?.Name ?? Path.GetFileNameWithoutExtension(file);
        Notice = refusal is not null ? $"{name} couldn't be read, so Noto is using its own colours."
            : fallbacks > 0 ? $"Some of {name}'s colours weren't readable, so Noto's own are used for them."
            : null;

        Changed?.Invoke(this, EventArgs.Empty);

        // The theme's name and counts, never a path (a name is the user's own text, as a folder's is never traced either
        // way: it is shown in Settings, not secret).
        bool highContrast = new global::Windows.UI.ViewManagement.AccessibilitySettings().HighContrast;
        bool shared = drawer is Panel { Background: SolidColorBrush shown } && _targets.Any(t => ReferenceEquals(t.Brush, shown));
        Trace.WriteLine(refusal is not null
            ? $"Noto.Theme refused reason={(refusal == "unreadable" ? "unreadable" : "invalid")}"
            : $"Noto.Theme applied={theme?.Name ?? ThemeFile.DefaultName} warnings={theme?.Warnings.Count ?? 0} fallbacks={fallbacks} shared={(highContrast ? "hc" : shared)}");
    }

    /// <summary>The installed themes, read off the UI thread.</summary>
    public Task<IReadOnlyList<InstalledTheme>> List() => Task.Run(() =>
    {
        try
        {
            return _store.List();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A themes folder that can't be read lists nothing; Default is still there.
            Trace.WriteLine($"Noto.Theme list failed error={ex.GetType().Name}");
            return (IReadOnlyList<InstalledTheme>)[];
        }
    });

    /// <summary>Asks for a theme file to install; <see langword="null"/> when the user cancels.</summary>
    public async Task<string?> Pick()
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.Downloads };
        picker.FileTypeFilter.Add(ThemeFile.Extension);
        picker.FileTypeFilter.Add(".json");
        InitializeWithWindow.Initialize(picker, _window());
        return (await picker.PickSingleFileAsync())?.Path;
    }

    /// <summary>
    /// Checks a picked file completely, off the UI thread (ADR-023 §8, §9, §1, §4): refused for anything a load would
    /// ignore or fall back from, with the reason in words; nothing is copied.
    /// </summary>
    public Task<ThemeCheck> Check(string path) => Task.Run(() =>
    {
        if (ThemeStore.ReadCapped(path) is not { } bytes)
        {
            return new ThemeCheck(null, null, "It couldn't be read, or it's larger than 64 KB.");
        }

        if (ThemeFile.Read(bytes, out string? refusal) is not { } theme)
        {
            return new ThemeCheck(null, null, refusal);
        }

        if (theme.Errors.Count > 0)
        {
            return new ThemeCheck(null, null, theme.Errors[0]);
        }

        var failures = Enum.GetValues<ThemeMode>()
            .SelectMany(mode =>
            {
                Dictionary<string, ThemeColor> set = theme.ColoursFor(mode);
                return Problems(Resolve(_own[mode], set), set, mode);
            })
            .Take(2)
            .Select(f => f.Reason)
            .ToList();

        return failures.Count > 0 ? new ThemeCheck(null, null, string.Join("; ", failures) + ".") : new ThemeCheck(bytes, theme, null);
    });

    /// <summary>Installs a checked theme, over <paramref name="replacing"/> when it has that name; its file name, or <see langword="null"/> when it couldn't be written.</summary>
    public Task<string?> Install(ThemeCheck check, string? replacing) => Task.Run(() =>
    {
        try
        {
            return _store.Install(check.Bytes!, check.Theme!.Name, replacing);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"Noto.Theme install failed error={ex.GetType().Name}");
            return (string?)null;
        }
    });

    /// <summary>Opens Explorer with an installed theme's file selected; whether the shell accepted it.</summary>
    public bool Reveal(string file) => ShellLaunch.ShowInFolder(_store.PathOf(file));

    /// <summary>
    /// Sends an installed theme's file to Windows' Recycle Bin, where it can be restored (it may be the only copy), with
    /// the same safety as an import's (#267): only where there is a Recycle Bin. Whether it has gone.
    /// </summary>
    public bool Remove(string file) => ShellRecycleBin.Recycle([_store.PathOf(file)], WindowHandle.FromHwnd(_window())).Recycled == 1;

    /// <summary>Every instance of Colors.xaml: a dictionary whose Light theme holds Noto's own TextQuoteBrush.</summary>
    private static IEnumerable<ResourceDictionary> ColourDictionaries(ResourceDictionary dictionary, HashSet<object> seen)
    {
        if (!seen.Add(dictionary))
        {
            yield break;
        }

        if (dictionary.ThemeDictionaries.TryGetValue("Light", out object? light) && light is ResourceDictionary l && l.ContainsKey("TextQuoteBrush"))
        {
            yield return dictionary;
        }

        foreach (ResourceDictionary merged in dictionary.MergedDictionaries)
        {
            foreach (ResourceDictionary found in ColourDictionaries(merged, seen))
            {
                yield return found;
            }
        }
    }

    private static void Set(Target target, Color colour)
    {
        if (target.Brush is { } brush)
        {
            brush.Color = colour;
        }
        else
        {
            target.Dictionary[target.Entry] = colour;
        }
    }

    private static ThemeColor Of(Color c) => new(c.R, c.G, c.B, c.A);

    /// <summary>The colour of a theme dictionary's brush or colour entry; <see langword="null"/> when it has neither.</summary>
    private static Color? ColourOf(ResourceDictionary dictionary, string entry) =>
        dictionary.TryGetValue(entry, out object? value) ? value switch { SolidColorBrush brush => brush.Color, Color colour => colour, _ => null } : null;

    /// <summary>One brush, or one colour-typed entry, a theme may set, in one theme dictionary, and Noto's own colour for it.</summary>
    private sealed record Target(ResourceDictionary Dictionary, string Entry, string Key, ThemeMode Mode, Color Own)
    {
        public SolidColorBrush? Brush { get; } = Dictionary[Entry] as SolidColorBrush;
    }
}

/// <summary>Turns a theme's colour into the platform's.</summary>
internal static class ThemeColorExtensions
{
    public static Color ToColor(this ThemeColor c) => Color.FromArgb(c.A, c.R, c.G, c.B);
}
