using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Noto.Core.Portability;
using Noto.Infrastructure.Portability;

namespace Noto;

/// <summary>
/// The Data rows (ADR-020 §6): Back up, Restore, Export and Import, each through Windows' own picker, with one progress
/// line and Cancel while it works. A result is said on that line; a failure on the page's notice line; nothing is
/// changed until the user has chosen, and a restore asks first, Cancel being the default.
/// </summary>
public sealed partial class SettingsPage
{
    private CancellationTokenSource? _work;

    /// <summary>Notes were imported: the window shows the new folders.</summary>
    public event EventHandler? Imported;

    private async void OnBackupClick(object sender, RoutedEventArgs e) => await Work(async (data, progress, cancellation) =>
    {
        if (await data.PickBackupDestination() is not { } file)
        {
            return null;
        }

        Begin("Backing up…");
        BackupCounts counts = await data.Backup(file, progress, cancellation);
        return string.Create(CultureInfo.CurrentCulture, $"Backed up {DataPortability.Describe(counts)} to {Path.GetFileName(file)}.");
    });

    private async void OnRestoreClick(object sender, RoutedEventArgs e) => await Work(async (data, progress, cancellation) =>
    {
        if (await data.PickBackup() is not { } file)
        {
            return null;
        }

        Begin("Checking the backup…");
        (BackupManifest? manifest, BackupProblem? problem) = await data.Check(file, progress, cancellation);
        if (manifest is null)
        {
            ShowNotice(DataPortability.Explain(problem!.Value));
            return string.Empty;
        }

        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Replace everything with this backup?",
            Content = string.Create(CultureInfo.CurrentCulture,
                $"The backup from {manifest.CreatedAt.ToLocalTime():f} has {DataPortability.Describe(manifest.Counts)}. Everything in Noto now will be replaced by it. What is here now is saved first, in the data folder's backups. Noto restarts to finish."),
            PrimaryButtonText = "Restore and restart",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
        {
            data.Discard();
            return "Nothing was restored.";
        }

        Begin("Saving what is here now, then restoring…");
        await data.RestoreAndRestart(manifest, progress, cancellation);
        return null;
    });

    private async void OnExportClick(object sender, RoutedEventArgs e) => await Work(async (data, progress, cancellation) =>
    {
        if (await data.PickFolder() is not { } parent)
        {
            return null;
        }

        Begin("Exporting…");
        ExportSummary? summary = await data.Export(new ExportScope.All(), parent, progress, cancellation);
        return summary is null
            ? "There's nothing to export yet: Noto has no notes."
            : string.Create(CultureInfo.CurrentCulture, $"Exported {DataPortability.Count(summary.Notes, "note")} in {DataPortability.Count(summary.Folders, "folder")} to {Path.GetFileName(summary.Directory)}.{MainWindow.Missing(summary)}");
    });

    private async void OnImportClick(object sender, RoutedEventArgs e) => await Work(async (data, progress, cancellation) =>
    {
        if (await data.PickFolder() is not { } folder)
        {
            return null;
        }

        Begin("Importing…");
        ImportReport report = await data.Import(folder, progress, cancellation);
        Imported?.Invoke(this, EventArgs.Empty);

        string done = report.Notes == 0 && report.Skipped.Count == 0
            ? "There were no Markdown files to import there."
            : string.Create(CultureInfo.CurrentCulture, $"Imported {DataPortability.Count(report.Notes, "note")} into {DataPortability.Count(report.Folders, "new folder")}.");
        if (report.Skipped.Count > 0 || report.ImageIssues.Count > 0)
        {
            ShowNotice(string.Create(CultureInfo.CurrentCulture,
                $"{Skipped(report.Skipped)}{(report.Skipped.Count > 0 && report.ImageIssues.Count > 0 ? " " : string.Empty)}{(report.ImageIssues.Count > 0 ? $"{DataPortability.Count(report.ImageIssues.Count, "picture")} couldn't be brought in; {(report.ImageIssues.Count == 1 ? "its note still refers to it" : "their notes still refer to them")} as before." : string.Empty)}"));
        }

        return done;
    });

    private void OnDataCancelClick(object sender, RoutedEventArgs e)
    {
        _work?.Cancel();
        DataCancelButton.IsEnabled = false;
        DataStatusText.Text = "Cancelling…";
    }

    /// <summary>
    /// Runs one piece of Data work: the rows are off while it runs, its progress and result are on the progress line, a
    /// failure is on the notice line, and focus comes back to the row it started from.
    /// </summary>
    private async Task Work(Func<DataPortability, IProgress<PortabilityProgress>, CancellationToken, Task<string?>> work)
    {
        if (_context?.Portability is not { Busy: false } data || _work is not null)
        {
            return;
        }

        Control? from = FocusManager.GetFocusedElement(XamlRoot) as Control;
        using var cancellation = new CancellationTokenSource();
        _work = cancellation;
        SetRowsEnabled(false);
        ShowNotice(null);
        var progress = new Progress<PortabilityProgress>(p =>
        {
            DataProgressBar.IsIndeterminate = false;
            DataProgressBar.Value = p.Total == 0 ? 0 : (double)p.Done / p.Total;
        });

        string? result;
        try
        {
            result = await work(data, progress, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            result = "Cancelled. Nothing was changed.";
        }
        catch (PortabilityException)
        {
            ShowNotice("That couldn't be done. Nothing was changed. Check that the place you chose can be written to and has room.");
            result = string.Empty;
        }
        finally
        {
            _work = null;
            SetRowsEnabled(true);
        }

        End(result);
        _ = from?.Focus(FocusState.Programmatic);
    }

    /// <summary>The progress line, working: the step in words, a bar that fills, and Cancel.</summary>
    private void Begin(string step)
    {
        DataProgress.Visibility = Visibility.Visible;
        DataStatusText.Text = step;
        DataProgressBar.Visibility = Visibility.Visible;
        DataProgressBar.IsIndeterminate = true;
        DataCancelButton.Visibility = Visibility.Visible;
        DataCancelButton.IsEnabled = true;
    }

    /// <summary>The progress line, done: the result in words, or nothing when the user cancelled a picker.</summary>
    private void End(string? result)
    {
        DataProgressBar.Visibility = Visibility.Collapsed;
        DataCancelButton.Visibility = Visibility.Collapsed;
        DataStatusText.Text = result ?? string.Empty;
        DataProgress.Visibility = string.IsNullOrEmpty(result) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SetRowsEnabled(bool enabled)
    {
        BackupButton.IsEnabled = enabled;
        RestoreButton.IsEnabled = enabled;
        ExportButton.IsEnabled = enabled;
        ImportButton.IsEnabled = enabled;
    }

    private static string Skipped(IReadOnlyList<ImportIssue> skipped) => skipped.Count switch
    {
        0 => string.Empty,
        1 => $"{skipped[0].File} wasn't imported: {Reason(skipped[0].Kind)}.",
        _ => string.Create(CultureInfo.CurrentCulture, $"{skipped.Count} files weren't imported, such as {skipped[0].File}: {Reason(skipped[0].Kind)}."),
    };

    private static string Reason(ImportIssueKind kind) => kind switch
    {
        ImportIssueKind.TooLarge => "it is larger than 25 MB",
        _ => "it isn't UTF-8 text",
    };
}
