# Printing a note from an unpackaged WinUI 3 app (#246)

**Date:** 2026-11-22. **Machine:** Windows 11 Home 10.0.26300.
**Noto:** unpackaged, self-contained, .NET 9, ADR-008's reduced set of Windows App SDK 2.6.5 parts (WinUI,
InteractiveExperiences, Runtime). **Spike:** [`spikes/Print`](../../spikes/Print/), built, not run (it opens a dialog
that needs a person; see *How to try the spike*).

## Why

Print is SideNotes' top historic complaint, fixed in its 1.6.3; parity rows B21, G25 and I6 ask for `Ctrl+P`
(SHOULD). WinUI 3's printing types were built for UWP's `CoreWindow`, so the question is whether Noto can reach
Windows' print dialog at all, and how the printout keeps the note's look: headings, lists, tasks, pictures.

> The September research ([feature-matrix.md §2](feature-matrix.md)) marked Print REJECT on principle 9. The parity
> plan ([sidenotes-parity.md](../product/sidenotes-parity.md) B21) later made it SHOULD, as it did for hex swatches,
> since built (F14). This document answers *how*; whether is the parity plan's call.

## Options

| # | Option | Windows' print dialog | The note's look | Unpackaged, self-contained | New size | ADR conflict | Verdict |
|---|---|---|---|---|---|---|---|
| 1 | `PrintManagerInterop` + `Microsoft.UI.Xaml.Printing.PrintDocument`, pages built from the read view's renderer | **Yes**, the system dialog with preview | **Native XAML**: the same `MarkdownDocument` parse and the same text engine as the cards | **Yes**, documented and sampled (below) | **0 bytes**: in WinUI and the OS | None | **Recommended** |
| 2 | Render to XPS or PDF in process, then print it | Only through a second step: no in-box API writes a PDF | Whatever is drawn by hand | Yes | A PDF writer or Direct2D drawing code | None, but a large build | Rejected |
| 3 | Render HTML, print with WebView2 (`ShowPrintUI(System)`, `PrintAsync`, `PrintToPdfAsync`) | **Yes** (`CoreWebView2PrintDialogKind.System`) | A second renderer (Markdown to HTML and CSS) to keep in step with the read view | Yes; the runtime is the OS's Evergreen WebView2 | **0 bytes more** (the SDK DLLs already ship), plus a browser process while printing | **ADR-004** ("not to a web view"), **ADR-017** row D | Fallback only |
| 4 | Export a temporary HTML or PDF, open it with the shell's `print` verb | The handler's, not Windows' | The default browser's or PDF app's | Yes | 0 | None, but leaves note text in a temporary file | Rejected |

## Evidence

### Option 1: `PrintManagerInterop` and `PrintDocument` (works)

- **Documented for WinUI 3 desktop.** Microsoft Learn's
  [Print from your app](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/print-from-your-app)
  (`ms.service: windows-app-sdk`, updated 2026-11-16) shows the desktop path: the window's handle from
  `WinRT.Interop.WindowNative.GetWindowHandle`, `PrintManagerInterop.GetForWindow(hWnd)` for `PrintTaskRequested`,
  `PrintManagerInterop.ShowPrintUIForWindowAsync(hWnd)` to open the dialog, and `PrintDocument` from
  `Microsoft.UI.Xaml.Printing` for `Paginate`, `GetPreviewPage` and `AddPages`. It recommends checking
  `PrintManager.IsSupported()` and catching the exception the call throws when printing can't proceed.
- **Sampled unpackaged.** Microsoft's [WindowsAppSDK-Samples Printing sample](https://github.com/microsoft/WindowsAppSDK-Samples/tree/main/Samples/Printing)
  (added 2026-10-12, #680) lists "Run the sample as either a packaged or unpackaged app" and needs Windows 10 1809 or
  later. Its `PrintHelper` flows text over pages with `RichTextBlockOverflow` and lays them out in a hidden canvas
  in the window's tree first, which is what makes `HasOverflowContent` true.
- **Windows 10 needs the March 2023 update.** [microsoft-ui-xaml#4419](https://github.com/microsoft/microsoft-ui-xaml/issues/4419):
  printing worked on Windows 11 21H2 but `ShowPrintUIForWindowAsync` threw `ArgumentException` on Windows 10 until
  KB5023773 (19045.2788, March 2023), whose notes say it "makes printing for them possible on Windows 10 devices"
  (quoted in the thread). One report says 19044 (21H2 LTSC) still sends nothing. The issue stays open only for UWP
  partial trust, which Noto is not: "fixed in 2023 ... seems to work well" (a contributor, in the thread).
- **Known bug, pictures.** [microsoft-ui-xaml#11095](https://github.com/microsoft/microsoft-ui-xaml/issues/11095)
  (open, WinAppSDK 2.0.1, May 2026): `Image` elements print blank on the first one or two preview pages and in the
  PDF, likely because the bitmap is still decoding. The read view's pictures are `BitmapImage`s, which decode
  lazily. **Mitigation in the spike:** each picture is decoded to a `SoftwareBitmap` and set on a
  `SoftwareBitmapSource` before the dialog opens, so the pixels exist when a page is made.
- **No new dependency.** `PrintDocument` is in `Microsoft.UI.Xaml` (the WinUI part Noto already references);
  `PrintManager` and `PrintManagerInterop` are Windows' own `Windows.Graphics.Printing`. The spike builds against
  exactly Noto's three Windows App SDK parts with 0 warnings.

### Option 2: XPS or PDF in process (no writer in the box)

- `Windows.Data.Pdf` only **reads and renders** a PDF (`PdfDocument.LoadFromFileAsync`, `PdfPage.RenderToStreamAsync`);
  it has no writer. There is no in-box PDF writer API for desktop apps.
- The XPS route (Print Document Package API, or Direct2D's `ID2D1PrintControl`) means drawing every glyph and picture
  with Direct2D. The one public WinUI 3 attempt ([castorix/WinUI3_Direct2D_Print](https://github.com/castorix/WinUI3_Direct2D_Print),
  linked from #4419) prints `RenderTargetBitmap` snapshots: raster text, not the note.
- A Win32 `PrintDlgEx` and GDI path shows the legacy dialog, not Windows' print experience, and needs a text layout
  of our own.

### Option 3: WebView2 (possible, but against two ADRs)

- **Already in the footprint, as files.** `Microsoft.WindowsAppSDK.WinUI` 2.6.5 depends on `Microsoft.Web.WebView2`
  1.0.3719.77 (its nuspec), so Noto's output already carries `Microsoft.Web.WebView2.Core.dll` (794 KB),
  `Microsoft.Web.WebView2.Core.Projection.dll` (686 KB) and `WebView2Loader.dll` (160 KB). ADR-008's reduction removed
  the AI, ML, search and widgets parts, not this.
- **The runtime is the OS's.** [Distribute your app and the WebView2 Runtime](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution):
  the Evergreen runtime is part of Windows 11; "the vast majority of Windows 10 devices" have it, and apps should
  handle the few that don't. This machine: 157.0.4341.60.
- **The APIs.** [Printing from WebView2 apps](https://learn.microsoft.com/en-us/microsoft-edge/webview2/how-to/print):
  `ShowPrintUI(CoreWebView2PrintDialogKind.System)` opens the system dialog; `PrintAsync` prints silently with
  settings; `PrintToPdfAsync` writes a PDF silently.
- **Against the ADRs.** ADR-004's security requirements: "Markdown is rendered to native controls, not to a web view."
  ADR-017 rejected a WebView2 editor because it "puts a browser engine, and its memory, into a background utility
  (principle 9)". Printing would also need a Markdown-to-HTML renderer beside the read view, a user data folder on
  disk, and a page served to the browser with the note's pictures.

### Option 4: the shell's `print` verb (unpredictable)

- The verb is whatever handler the user has. On this machine `.html` belongs to a third-party browser
  (`Terax.html`) and `htmlfile`'s print verb is the legacy `rundll32 mshtml.dll,PrintHTML` (Trident); `.pdf` belongs
  to a third-party PDF app. Neither is Windows' dialog, and the look is that handler's.
- A PDF to open needs option 2's missing writer.
- The note's text is left in a temporary file for as long as the handler holds it. Noto already offers
  *Export as Markdown* for "take it elsewhere".

## Findings

1. An unpackaged, self-contained WinUI 3 app **can** open Windows' print dialog: `PrintManagerInterop` with the
   window's handle and a `Microsoft.UI.Xaml.Printing.PrintDocument`. Microsoft documents it for WinUI 3 desktop and
   ships an unpackaged sample (October 2026).
2. It costs **nothing in size or dependencies** and contradicts **no ADR**. Pages are XAML built from the same
   `MarkdownDocument` parse as the read view, which is exactly ADR-004's "rendered to native controls".
3. Two platform caveats: **Windows 10 must be 22H2 at 19045.2788 or later** (KB5023773), and **pictures must be
   decoded before a page is made** (microsoft-ui-xaml#11095). The spike does the second with `SoftwareBitmapSource`.
4. A Noto-specific risk not tested: the print dialog may take activation out of Noto's process. If it does, the
   drawer's hide on deactivation (`WindowCoordinator.OnAppDeactivated`) would hide Noto behind the dialog. The
   feature must hold the workspace open while a print task is open.
5. WebView2 works too and is already on disk, but it needs a second renderer and an ADR-004 amendment. Keep it as
   the fallback if option 1 fails on a target machine.

## Recommendation

**Option 1.** Print through `PrintManagerInterop.ShowPrintUIForWindowAsync` and a `PrintDocument` whose pages are the
read view's own rendering, in ink on white, flowed over pages with `RichTextBlockOverflow`. Decode pictures to
`SoftwareBitmapSource` first. Hold the drawer open while the dialog is up. No new package, no ADR change. Windows 10's
floor for printing is 19045.2788; earlier builds get a plain message instead of a dialog.

## Costs

| | |
|---|---|
| Size | 0 bytes: no package, no file added to the installer |
| Dependencies | None new: `Microsoft.UI.Xaml.Printing` (WinUI, referenced) and `Windows.Graphics.Printing` (OS) |
| ADRs | None contradicted. ADR-016 (the card read view) gains a note that its renderer also prints. ADR-004 untouched |
| Code | One shared renderer (the read view's `Render`, made reusable), a `NotePrinter` (registration, pagination, picture decode, about 200 lines), a `Ctrl+P` command, a hold on the drawer's auto-hide |
| Platform floor | Windows 10 22H2 19045.2788+, Windows 11 any. Below that, a message |

## Proposed implementation plan

One slice (fast-track, one user-visible change):

1. **Share the renderer.** Move `MarkdownReadView.Render`'s block and inline building into a renderer both the read
   view and print call, given its brushes (theme, or ink on paper), its picture limit and size, and a picture source.
   The read view keeps its look; its guard tests keep passing. The spike's `NotePrint.Render` is the print side,
   copied rather than shared only because a spike can't reach `internal` code.
2. **`NotePrinter`** in `Noto.Windows`, created once per `MainWindow`: `PrintManagerInterop.GetForWindow(hwnd)` and
   `PrintTaskRequested` registered once (registering twice throws), a hidden layout canvas in the window's root, and
   the spike's `Paginate`, `GetPreviewPage` and `AddPages`. The task's title is `NoteTitle.From` (a PDF's suggested name).
   Code and plain-text notes print as typed, Code in the monospace font, as the read view shows them. The read view's
   20,000-character limit is for cards, so it does not apply to print.
3. **Pictures** are decoded to `SoftwareBitmapSource` before the dialog opens (microsoft-ui-xaml#11095), sized to the
   page's text width and at most two thirds of its height, so a picture never outgrows a page.
4. **`Ctrl+P`** prints the open note from the editor, or the focused card from the stack (B21, G25, I6), with a
   *Print…* item on the card's menu. The open note is saved first, as Export does.
5. **The drawer stays open** while a print task is open: a hold on `WindowCoordinator`'s deactivation hide, released
   on `PrintTask.Completed` (any completion). This needs measuring first: is the dialog in Noto's process?
6. **Failure.** `PrintManager.IsSupported()` false, or the call throws (Windows 10 before 19045.2788): the window's
   existing message, "Printing isn't available on this PC", and nothing else happens.
7. **Tests.** Guard tests (`PrintGuardTests`): one `GetForWindow` registration, `Ctrl+P` wired, pictures via
   `SoftwareBitmapSource` and never `BitmapImage`, the layout canvas hidden. One harness case: `Ctrl+P` on a seeded
   note opens the dialog (found by UI Automation), then *Cancel*, with the note unchanged and Noto still shown.
   Printing to *Microsoft Print to PDF* end to end stays a manual check: the PDF printer's own Save dialog is outside
   Noto.

## How to try the spike

The spike opens Windows' print dialog on start, so a person must be there. It never touches Noto's data.

```text
dotnet build spikes/Print/Print.csproj -c Release
spikes\Print\bin\Release\net9.0-windows10.0.19041.0\win-x64\Noto.Spike.Print.exe
```

With no argument it writes a sample note to `%TEMP%\noto-print-spike` (headings, emphasis, a link, bullets,
numbers, open and done tasks, a quote, a code block, a separator, a generated picture on page 1, and a second page).
To print a real note, export one note with *Export as Markdown* and pass its `.md` file; its pictures are found in
the `attachments` folder beside it.

In the dialog choose **Microsoft Print to PDF**, then *Print*, and save into `%TEMP%`. Check:

- the preview shows two pages, with the picture on page 1 the first time (#11095);
- in the PDF: three heading sizes, bullets, ☐ and ☑, the done task struck through, code in a monospace font, the
  link underlined;
- changing orientation in the dialog flows the pages again;
- `Ctrl+P` or the button opens the dialog again; the window's status line reads `Print task: Submitted, 2 page(s)`;
- whether a separate process owns the dialog (Task Manager, while it is open). This decides plan step 5.

## Not done

- **The spike was not run.** It opens a dialog that needs a person, so it was built (0 warnings) but not exercised
  here. The checks above are for the lead.
- **Windows 10.** Not available here; the 19045.2788 floor is Microsoft's note as quoted in #4419.
- **Highlights** (`::text::`) print underlined in the spike: whether a `TextHighlighter` prints is untested.
