using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Noto.Infrastructure.Storage;

namespace Noto;

/// <summary>
/// Appearance's Theme row (#269, ADR-023 §8; parity F2–F7, J22): Default and the installed theme files, read from the
/// themes folder each time Settings opens; choosing one applies it at once (App.OnSettingChanged). Install theme… checks
/// a picked file completely and copies it in, Show in Explorer reveals the chosen one, Remove sends it to the Windows
/// Recycle Bin and goes back to Default.
/// </summary>
public sealed partial class SettingsPage
{
    private IReadOnlyList<InstalledTheme> _themes = [];

    /// <summary>Lists the installed themes, off the UI thread, and shows the stored choice among them.</summary>
    private async Task ShowThemes()
    {
        if (_context is null)
        {
            return;
        }

        _themes = await _context.Themes.List();
        bool wasShowing = _showing;
        _showing = true;

        try
        {
            ThemeFileSetting.Items.Clear();
            ThemeFileSetting.Items.Add(new ComboBoxItem { Content = Core.Themes.ThemeFile.DefaultName, Tag = string.Empty });
            foreach (InstalledTheme theme in _themes)
            {
                // One that can't be read is listed, and can't be chosen (ADR-023 §8).
                var item = new ComboBoxItem
                {
                    Content = theme.Name ?? $"{Path.GetFileNameWithoutExtension(theme.File)} (can't be read)",
                    Tag = theme.File,
                    IsEnabled = theme.Name is not null,
                };
                if (theme.Author is { } author)
                {
                    AutomationProperties.SetHelpText(item, $"By {author}");
                }

                ThemeFileSetting.Items.Add(item);
            }

            string stored = _context.Preferences.ThemeFile;
            ThemeFileSetting.SelectedItem = ThemeFileSetting.Items.OfType<ComboBoxItem>().FirstOrDefault(i => FileOf(i) == stored) ?? ThemeFileSetting.Items[0];
            ShowThemeChoice();
        }
        finally
        {
            _showing = wasShowing;
        }
    }

    /// <summary>The chosen theme's author, and Show in Explorer and Remove only while an installed one is chosen.</summary>
    private void ShowThemeChoice()
    {
        string file = ThemeFileSetting.SelectedItem is ComboBoxItem item ? FileOf(item) : string.Empty;
        InstalledTheme? chosen = _themes.FirstOrDefault(t => t.File == file);
        ThemeFileText.Text = chosen is null ? "Noto's own colours." : chosen.Author is { } author ? $"By {author}." : "An installed theme.";
        RevealThemeButton.IsEnabled = chosen is not null;
        RemoveThemeButton.IsEnabled = chosen is not null;
    }

    /// <summary>An item's file: its tag, or Default's empty one, which XAML may hand back as no tag at all.</summary>
    private static string FileOf(ComboBoxItem item) => item.Tag as string ?? string.Empty;

    private void OnThemeFileChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_showing || _context is null)
        {
            return;
        }

        Write(p => ThemeFileSetting.SelectedItem is ComboBoxItem item && p.SetThemeFile(FileOf(item)));
        ShowThemeChoice();
        ShowThemeNotice();
    }

    /// <summary>What applying the chosen theme had to say (it couldn't be read, or some colours are Noto's own).</summary>
    private void ShowThemeNotice()
    {
        if (_context?.Themes.Notice is { } notice)
        {
            ShowNotice(notice);
        }
    }

    private async void OnInstallThemeClick(object sender, RoutedEventArgs e)
    {
        if (_context is null || await _context.Themes.Pick() is not { } path)
        {
            return;
        }

        ThemeCheck check = await _context.Themes.Check(path);
        if (check.Refusal is { } refusal)
        {
            ShowNotice($"That theme can't be installed. {refusal}");
            return;
        }

        // A theme's identity is its name, ignoring case: the same name asks first, Cancel being the default.
        string? replacing = _themes.FirstOrDefault(t => string.Equals(t.Name, check.Theme!.Name, StringComparison.OrdinalIgnoreCase))?.File;
        if (replacing is not null)
        {
            var confirm = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = $"A theme named {check.Theme!.Name} is installed. Replace it?",
                PrimaryButtonText = "Replace",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
            };

            if (await confirm.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }
        }

        if (await _context.Themes.Install(check, replacing) is not { } file)
        {
            ShowNotice("The theme couldn't be copied into Noto's data folder, so nothing changed.");
            return;
        }

        // Chosen and applied at once; a replaced active theme is applied again with its new colours.
        Write(p => p.SetThemeFile(file));
        await ShowThemes();
        ShowNotice(check.Theme!.Warnings.Count > 0 ? $"{check.Theme.Name} is installed. {check.Theme.Warnings[0]}" : null);
        ShowThemeNotice();
        _ = InstallThemeButton.Focus(FocusState.Programmatic);
    }

    private void OnRevealThemeClick(object sender, RoutedEventArgs e)
    {
        if (_context is not null && ThemeFileSetting.SelectedItem is ComboBoxItem item && FileOf(item).Length > 0)
        {
            ShowNotice(_context.Themes.Reveal(FileOf(item)) ? null : "The theme's file couldn't be shown in Explorer.");
        }
    }

    // Removing the chosen theme, which is the one applied, goes back to Default at once.
    private async void OnRemoveThemeClick(object sender, RoutedEventArgs e)
    {
        if (_context is null || ThemeFileSetting.SelectedItem is not ComboBoxItem item || FileOf(item) is not { Length: > 0 } file)
        {
            return;
        }

        if (!_context.Themes.Remove(file))
        {
            ShowNotice("The theme couldn't be moved to the Windows Recycle Bin, so it is still installed.");
            return;
        }

        if (_context.Preferences.ThemeFile == file)
        {
            Write(p => p.SetThemeFile(string.Empty));
        }

        await ShowThemes();
        _ = InstallThemeButton.Focus(FocusState.Programmatic);
    }
}
