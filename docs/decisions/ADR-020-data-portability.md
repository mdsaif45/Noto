# ADR-020 — Data portability: backup, restore, export and import

**Status:** Accepted
**Date:** 2026-11-04
**Related:**
- ADR-002 (local-first: "export and backup must be good", "export must be first-class and lossless"), ADR-003 (a backup-and-restore round trip is a test requirement), ADR-004 (markdown is the content), ADR-012 (identifiers; `VACUUM` renumbers rowids), ADR-018 (the FTS index keys on `rowid`), ADR-019 (images are files beside the database)
- Parity I1, I2, I7, I9, I10, I15; implementation plan P5.0 (this gate), P5.2–P5.7

---

## Context

Everything a user owns lives in one data root (`%LOCALAPPDATA%\Noto` by default): `noto.db` (folders, notes, the recycle bin, settings, the search index), its WAL beside it, and `attachments\` (images, named `<ULID>.<type>`, referred to only from note text). Nothing yet copies, restores, exports or imports any of it. The database is open, in WAL mode, whenever Noto runs.

## Decision

### 1. Two formats, two purposes

| | Backup | Export |
|---|---|---|
| For | getting *this* workspace back, exactly | reading notes anywhere, moving them elsewhere |
| Holds | everything: folders, notes, order, pin, fold, colours, the recycle bin, settings, images | note text and its images; order, pins and folds in a small index file |
| Shape | one file, `*.notobackup` | a folder of `.md` files and pictures |
| Read back by | Restore (replaces) | Import (adds) |

A backup is not a export, and an export is not a backup: restoring is "put it all back as it was", importing is "add these notes to what I have". Keeping them apart keeps both simple.

### 2. Backup: one archive, snapshotted, hashed

A `.notobackup` is a ZIP (System.IO.Compression; no new dependency) holding:

```
manifest.json            format, versions, counts, and a SHA-256 for every other entry
noto.db                  a snapshot of the database
attachments/<ULID>.<t>   every image file Noto named
```

- **The snapshot is SQLite's online backup API** (`BackupDatabase`), the page-level copy the pre-migration safety copy already uses: consistent while Noto writes, WAL included, `rowid`s kept (so the search index stays valid). Never a file copy of a live database, never `VACUUM INTO` (it may renumber `rowid`s, ADR-012).
- The snapshot is checked (`PRAGMA integrity_check`) before it is added.
- **Compression by content:** the database entry is deflated (text compresses well: a 3.2 MB snapshot became 0.1 MB); pictures are *stored*. They are already compressed, so deflating them saves nothing (150.0 MB stored, 150.1 MB deflated) and costs forty times the time (measured: 0.27 s stored, 11.2 s deflated, for 150 one-megabyte pictures).
- **Every byte is hashed as it is written**, so the archive is read once; the manifest is written last, from those hashes.
- A cancelled or failed backup leaves no file. That includes the empty file Windows' save dialog creates when the user chooses a name; it is removed only while still empty, so a file the user chose to replace is never deleted.
- **Manifest:** `format = "noto-backup"`, `formatVersion = 1`, `schemaVersion` (the database's `user_version`), `createdAt`, the app version, counts (folders, notes, binned notes, images), and `entries[]` of `{ path, bytes, sha256 }`.
- **Written whole or not at all:** the archive is built in a temporary file in the destination folder and renamed into place when complete. A cancelled or failed backup leaves nothing.
- **Versioning:** a reader accepts `formatVersion` up to the one it knows and refuses a newer one by name ("made by a newer Noto"). A newer *schema* inside a known format is refused the same way; an older one is migrated on restore by the normal migration path (§3).

### 3. Restore: validate everything, then replace at the next start

Restore replaces the workspace. It is destructive, so it is built to be safe:

1. **Validate first, completely**, before anything is touched:
   - the archive opens; `manifest.json` parses; `format` is `noto-backup`;
   - `formatVersion` and `schemaVersion` are not newer than this build;
   - every entry the manifest lists is present, has its size and its SHA-256, and no entry is outside the archive's own paths (no `..`, no rooted path);
   - the database opens and passes `integrity_check`.
   Any failure is reported by kind (*corrupted*, *incomplete*, *made by a newer Noto*, *not a Noto backup*) and nothing changes.
2. **Confirm.** A dialog names the backup's date and counts, says the current notes will be replaced, and defaults to Cancel.
3. **Back up what is there now** (§2) into `backups\before-restore-<stamp>.notobackup`, so a mistaken restore is itself undoable.
4. **Stage:** the validated database and images are extracted into `restore-staging\` under the data root, then a marker, `restore-pending.json`, is written last.
5. **Restart.** Noto quits through its one end (the window's `Closed`): at the end of `ReleaseResources`, once the database and the single-instance claim are let go, `Relaunch` starts Noto's own executable again, passing on only the data root (never `--login`, so the restored workspace is shown), and the process ends.
6. **Apply at start**, after the single-instance claim and before the database is opened — the one point at which no connection exists:
   - move the current `noto.db` (and `-wal`, `-shm`) and `attachments\` aside into `backups\replaced-<stamp>\`;
   - move the staged database and images into place (same volume: renames);
   - delete the marker.
   If any step fails, everything moved aside is moved back and the marker is removed: the workspace is either the old one or the new one, never a mix. A move that cannot be undone leaves its file in the aside directory, which is then kept. After a successful swap the aside directory is removed: the user's undo is step 3's backup. A staging directory without its marker (a restore abandoned before step 4 finished) is removed at start and never applied. The normal `Initialize()` then migrates an older schema forward. The window says what happened: *Your notes were restored from the backup*, or, after a roll-back, *The backup couldn't be restored, so your notes are as they were*.

A restore restores settings too (they are the user's), including the hotkey and theme.

### 4. Export: Markdown, byte for byte

- **Scopes:** the current note, a folder, or everything (the recycle bin is not exported; a backup keeps it).
- **Shape:**
  ```
  Noto export <yyyy-MM-dd HHmm>\
    noto-export.json                 order, pins, folds, colours (optional; Import reads it)
    <Folder>\<Note>.md
    <Folder>\attachments\<ULID>.<t>  each image a note in this folder refers to
  ```
- **The `.md` is the note's source exactly** (UTF-8, no BOM, line endings as stored). Image references are `attachments/<ULID>.<t>`, relative to the note's own folder, so they resolve beside it and the text needs no rewriting.
- **Names:** a note's file is its title (first line, Markdown markers removed), a folder's directory its name; characters Windows forbids become `_`, reserved device names get a `_` suffix, names are cut at 80 characters, and an empty name is `Untitled`. Collisions within a directory take ` (2)`, ` (3)`… in display order (pinned first, then order), so the same workspace always exports to the same names.
- Written into a temporary directory and renamed when complete; cancelled, nothing remains.

### 5. Import: add, never overwrite

- **Source:** a folder. Every `*.md`, `*.markdown` and `*.txt` under it is a note; a note's folder is the directory it is in, relative to the source (`Work`, `Work / 2026` for a nested one: Noto's folders are flat, parity C3, so nesting becomes the name). Files at the source's top level go into a folder named after the source.
- **Never overwrites:** every imported folder is a new folder, every note a new note with a new identifier. A folder whose name already exists gets ` (2)`, ` (3)`… — existing notes are never touched, merged or replaced. Identifiers in `noto-export.json` are not reused (the same export imported twice must not collide).
- **Text:** read as UTF-8 (a BOM is dropped), kept exactly, line endings included; an empty file is an empty note. A file that is not valid UTF-8, or over 25 MB, is skipped and reported. Files inside an `attachments` directory are pictures, never notes.
- **Images:** each image a note refers to by a relative path inside the source folder is copied in (`AttachmentStore`'s checks: type by its bytes, ≤ 25 MB). A Noto-named reference (`attachments/<ULID>.<t>`) keeps its name when that name is free or already holds the same bytes, so an export imports with its text unchanged; otherwise it gets a new name and the reference is rewritten. A reference to a file that is missing, outside the source folder, or not an image is left as written and reported (it shows as *Image not found*).
- **Order, pins, folds:** from `noto-export.json` when present and of a version this build reads (its directories and files are used only inside the source); otherwise folders and notes in name order, unpinned. An export of everything includes its empty folders, so they come back too.
- **All or nothing in the database:** every folder and note is inserted in one transaction; images are copied first and any copied for an import that then fails or is cancelled are removed.
- **Report:** imported folders, notes and images; skipped files with the reason.

### 6. Where it lives

- Core: the formats and the rules (manifest, names, collisions, reference rewriting) — pure and tested.
- Infrastructure: `Portability/` — snapshot, archive, validation, staging, the start-time apply, export and import over `NotoDatabase` and `AttachmentStore`. No new project, no new repository layer.
- Windows: the Data section of Settings (Back up, Restore, Export all, Import) with one progress line and Cancel; Export on a folder's and a card's context menu; Windows' own file pickers. Work runs off the UI thread and reports progress through the dispatcher.

## Performance

Measured on the realistic workload (2,000 notes including a 2 MB one, 20 folders, 150 one-megabyte pictures; a 150 MB backup), on the development machine, Release build:

| | Time |
|---|---|
| Backup, inside Noto (trace: snapshot 33 ms, archive 308 ms) | 0.34 s |
| Backup, from Back up… to the result (Windows' save dialog included) | 2.2–2.5 s |
| Export of everything | 3.1–3.5 s |
| Import of that export | 2.7–3.0 s |
| Restore, from confirming to the restarted window (a safety backup of what is there included) | 3.1–3.3 s |

An earlier measurement of 46.9 s for the backup was the validation harness, not Noto: it clicked the save dialog's button with a sent message, the dialog's own COM call was refused inside it (*an outgoing call cannot be made since the application is dispatching an input-synchronous call*), and the run waited on the error box. The harness now posts the click. Nothing in the backup itself was slow: it reads and hashes each byte once and never re-compresses a picture.

## Consequences

- **Positive:** the backup is exact (it is the database) and safe to take at any time; restore is validated, confirmed, undoable and never half-applied; exports are plain Markdown that round-trips through import with text unchanged.
- **Negative:**
  - Restore needs a restart (it replaces the open database). It is announced in the confirmation, and takes as long as the safety backup of what is there now.
  - Import cannot keep identifiers, so anything that refers to a note by id (none in v0.1) would not follow it.
  - Automatic, scheduled backups (I8) and a user-chosen backup folder (I11) are not in this change; `backups\` holds only the restore safety copies.

## Verification

- **Core:** manifest parse and refusal by kind; names, collisions, reserved names; reference rewriting.
- **Infrastructure:** backup of empty, one-note, many-note, Unicode, CRLF, large and image workspaces; restore of each, byte-identical content, order, pins, folds, bin and settings; corrupt, truncated, tampered (hash), missing-entry, path-escaping, newer-format and newer-schema archives refused with nothing changed; a failure mid-apply rolls back; repeated backup → restore; backup → restore → export; export → import with text identical; import collisions, missing images, invalid UTF-8; cancellation leaving nothing.
- **Mutation:** the validation, hashing, path, version and rollback branches.
- **Runtime:** `Invoke-Portability.ps1` drives each flow through Windows' own file dialogs on a frozen build, with timings for a large workspace. Windows hosts those dialogs in a process of its own whose controls offer UI Automation no patterns: the harness finds them by ownership (their root owner is Noto's window) and gives their name box its text and their button its click by the controls' own messages.
