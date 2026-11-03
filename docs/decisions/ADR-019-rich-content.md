# ADR-019 — Rich content: links that open safely, and images kept beside the text

**Status:** Accepted
**Date:** 2026-11-03
**Related:**
- ADR-004 (markdown is the content; link security), ADR-012 (identifiers), ADR-017 (the source is the editor's text; nothing enters the document that is not in it), ADR-018 (search indexes the source)
- Parity D17, D29, D30, D34, D37, D53, J16, H14; implementation plan P3.0–P3.5; risk R6

---

## Context

Rich content is the last core-editing capability before backup. Two facts shape it:
- **Nothing opens a link yet.** The only scheme check is private to `HtmlToMarkdown`.
- **ADR-017's invariant holds:** the editor's text is the markdown source, 1:1. RichEdit objects (link fields, embedded images) are forbidden, and a guard enforces it.

Images could break that invariant. This ADR keeps it.

## Decision

### 1. One allow-list, in Core

`LinkTarget.Classify(string destination)` is the single answer to "what is this link?". `HtmlToMarkdown`, the editor and the card view all use it.

| Kind | Destination | Opened by |
|---|---|---|
| **Web** | `http:` / `https:` URL | the default browser |
| **Mail** | `mailto:` | the default mail app |
| **File** | `file:` URI of a local path | Explorer's association |
| **Folder** | `file:` URI of a local folder | Explorer |
| **Attachment** | `attachments/<name>` (§4) | the viewer for its type |
| **Refused** | anything else (`javascript:`, `data:`, `ms-*:`, UNC `\\host\…`, relative paths, whitespace) | never |

UNC paths are refused: opening one contacts a server, as a web link does, but silently.

### 2. Opening (ADR-004 refined)

- **In the editor** (the open runs after the input that asked for it, never inside the key or pointer handler: measured, a shell launch inside the editor's key handler left the UI thread stuck):
  - **Ctrl+Click** on a link opens it (the convention in Word, OneNote and VS Code);
  - **Ctrl+Enter** with the caret in a link opens it from the keyboard.
  - A plain click places the caret, as for any text.
- **In a card:** a click on a link opens it. The card is a read view, so a click there has nothing else to do.
- **Through one checked entry point:** `ShellLaunch.OpenLink` runs `Classify` again, so no caller can skip the check.
- **ADR-004's "`file:` asks first", refined:**
  - a folder, or a file of a document type (text, office, PDF, image, audio, video, archive), opens directly;
  - **a program, script, shortcut or unknown type asks first** (`.exe .com .bat .cmd .ps1 .vbs .js .msi .lnk .scr .hta` …).

  This keeps ADR-004's safety intent: a pasted note can't run a program on one click. It also makes D34's file shortcuts usable, where asking about every PDF would teach the user to click Yes without reading.
- **Missing targets:** a file or folder that no longer exists is reported in the notice line ("That file is no longer there"). Nothing is opened, and the path is never logged.

### 3. Links in the text

- **Bare URLs:**
  - `https://…`, `http://…` and `www.…` in ordinary text are links (GFM's extended autolink).
  - The parser marks them `Link` with no markup. Trailing punctuation stays outside, by GFM's rule.
  - Search already indexes them as text.
- **Paste a URL over a selection (D53):** the selection becomes `[selection](url)`, one undo step.
- **File and folder links (D34, J16):**
  - files and folders copied in Explorer (Ctrl+C) and pasted into the editor (Ctrl+V) become `[name](file:///…)`, one per line, at the caret: the keyboard path, and the one in the links PR;
  - dropping them from Explorer does the same, at the drop point (built with the images, §4).
  - A file link reads as its name. Its destination is hidden like any link's.

### 4. Images: files beside the database, markdown in the note

- **Stored as files** in the data root's reserved `attachments` folder, which `NotoStoragePaths` already creates.
  - Name: a new ULID plus the image's own extension. The original name is never used: a dropped name is untrusted input (data-model.md).
  - Only `png`, `jpg`, `jpeg`, `gif`, `webp` and `bmp` are accepted, and only up to 25 MB.
- **Referenced in the note as markdown:** `![name](attachments/01J….png)`.
  - The note's text stays the truth (ADR-004). Search indexes it, export copies it, and the source shows it.
  - **There is no Attachments table.** A table would be a second record of what the text already says, and could disagree with it. data-model.md's sketch is superseded.
- **In the editor:**
  - the image's line keeps its source, with the markup hidden: what shows is its name, dimmed, as a caption;
  - its paragraph is given **space below** (`SpaceAfter`) equal to the image's drawn height and a 6 px gap above and below;
  - an overlay draws the image in that space, as the checkbox overlay draws boxes (ADR-017 §10). RichEdit's rectangle for the line takes in that space (measured), so the picture is placed at the top of it.
  - The text stays 1:1, and no object enters the document; the guard stands.
  - With Show markup on, no image is drawn. A note with more than 50 images keeps them all as captions.
- **In a card:** the read view draws each image below its caption, at most 160 px high, for a note's first 4 images: a card is a glance.
- **Sizing (`ImageSize.Fit`, Core):** natural size, made smaller to fit the editor's width and 320 px high (the card's width and 160 px), aspect kept, **never enlarged**. The size is read from the file's header first; the picture is then decoded at the drawn width (`DecodePixelWidth`), never at full resolution.
- **A missing or unreadable file:** *Image not found* or *This image can't be shown* is drawn where the picture would be, in the editor and the card. Nothing else changes; the source is untouched.
- **Accessibility:** the drawn pictures are out of the accessibility tree (`AccessibilityView.Raw`). What a reader of the screen hears is the line itself: the image's name.
- **How it gets in:**
  - **Ctrl+V of a picture** (a screenshot, an image copied from a browser) is re-encoded as PNG and named *Pasted image*. Only when the clipboard has no files and no text: with text, the text is pasted;
  - **Ctrl+V of files copied in Explorer:** files that are not images are linked (§3); each image file is copied in and named by its file name;
  - **dropping files** does the same at the drop point;
  - each image goes on a line of its own, one undo step each. A refusal (over 25 MB, not an image of an accepted type, a write that failed) is said in the notice line.
- **Removal:** deleting the image's line deletes the reference. Undo brings it back, since the file is still there.
- **Sweep:** once per start, off the UI thread, a file in `attachments` is deleted when Noto named it, no note refers to it (binned notes included) and it is older than 7 days. The 7 days let undo, the bin and a crash mid-edit all keep their image. A sweep that cannot read the notes or the folder deletes nothing.
- **Not in v0.1:** an image's own menu (copy, save, preview: D32, D33, D35) and screenshots by hotkey.

### 5. Order: two PRs, one design

1. **Links:** §1, §2, §3. It needs nothing from §4.
2. **Images:** §4, with the drop of §3's files, which shares the editor's drop handling.

## Consequences

- **Positive:**
  - one allow-list;
  - one launcher that checks again;
  - the source stays the editor's text;
  - images survive export as plain files;
  - no new table, no new store.
- **Negative:**
  - Narrator reads an image line as its markdown (the same RC gate as ADR-017 §6);
  - an overlay image is not part of the text's own selection highlight.

## Verification

- **Core tests:**
  - `Classify` over every kind and every refusal (UNC, `javascript:`, `data:`, relative paths, whitespace);
  - bare URL boundaries;
  - paste over a selection;
  - the attachment reference format.
- **Platform tests:**
  - `OpenLink` refuses what `Classify` refuses;
  - the program-type list;
  - a missing target is reported.
- **Infrastructure:** saving an image (type and size limits, ULID names) and the sweep (referenced, recent and binned files kept).
- **Guards:**
  - no object is inserted into the document;
  - every launch goes through `OpenLink`;
  - no path is logged.
- **Runtime harness:**
  - Ctrl+Click and Ctrl+Enter open (observed through a test URI handler, never a real browser);
  - a card link opens;
  - a copied file becomes a link (`Invoke-Links.ps1`);
  - `Invoke-Images.ps1`: the sweep at start; drawing in the card and the editor, below the line in reserved space; tall and small images; missing and unreadable files in both; the picture out of the accessibility tree; a pasted picture as a PNG file and a reference, one undo; text winning over a picture; copied image and text files; a real drop at the drop point; a removed image swept at the next start; timings for a note with five images.
