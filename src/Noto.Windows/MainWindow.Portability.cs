using System.Globalization;
using Microsoft.UI.Xaml;
using Noto.Infrastructure.Portability;

namespace Noto;

/// <summary>
/// Export from a folder's or a card's menu, and what the window says about a restore or an import (ADR-020 §6).
/// A result is said on the surface's status line, quietly; a failure on its notice line.
/// </summary>
public sealed partial class MainWindow
{
    // A restore that rolled back at this start, said again after the first folder load.
    private string? _restoreNotice;

    /// <summary>Backup, restore, export and import; set once by the app.</summary>
    internal DataPortability? Portability { get; set; }

    /// <summary>Says what a restore applied at this start did, on the folder list where Noto opens.</summary>
    internal void ShowRestoreOutcome(RestoreOutcome outcome)
    {
        switch (outcome)
        {
            case RestoreOutcome.Applied:
                ShowStatus("Your notes were restored from the backup.");
                break;
            case RestoreOutcome.RolledBack:
                // Kept until the folder list has loaded: a successful load clears the notice line, which would take
                // this with it before it was seen (measured).
                _restoreNotice = "The backup couldn't be restored, so your notes are as they were.";
                ShowNotice(_restoreNotice);
                break;
        }
    }

    /// <summary>After an import from Settings: the folder list again, so the new folders are there when Settings closes.</summary>
    private void OnSettingsImported(object? sender, EventArgs e) => Refresh();

    /// <summary>A quiet result on the folder list: announced politely, never in the error colour.</summary>
    private void ShowStatus(string message)
    {
        StatusText.Text = message;
        StatusText.Visibility = Visibility.Visible;
    }

    private void ShowNoteStatus(string message)
    {
        NoteStatusText.Text = message;
        NoteStatusText.Visibility = Visibility.Visible;
    }

    /// <summary>Exports one folder or one note (from its menu) to a folder the user picks.</summary>
    private async void ExportFromMenu(ExportScope scope, bool fromStack)
    {
        if (Portability is not { Busy: false } portability)
        {
            return;
        }

        Action<string> status = fromStack ? ShowNoteStatus : ShowStatus;
        Action<string> notice = fromStack ? ShowNoteNotice : ShowNotice;
        try
        {
            if (await portability.PickFolder() is not { } parent)
            {
                return;
            }

            status("Exporting…");
            ExportSummary? summary = await portability.Export(scope, parent, null, CancellationToken.None);
            status(summary is null
                ? "There's nothing to export: it has no notes."
                : string.Create(CultureInfo.CurrentCulture, $"Exported {DataPortability.Count(summary.Notes, "note")} to {Path.GetFileName(summary.Directory)}.{Missing(summary)}"));
        }
        catch (PortabilityException)
        {
            status(string.Empty);
            notice("The notes couldn't be exported there.");
        }
    }

    /// <summary>A line about pictures an export could not find, or nothing.</summary>
    internal static string Missing(ExportSummary summary) =>
        summary.MissingImages == 0 ? string.Empty : $" {DataPortability.Count(summary.MissingImages, "picture")} couldn't be found.";
}
