# ADR-022 — The `noto://` link and the command line

**Status:** Accepted
**Date:** 2026-11-16
**Related:**
- ADR-008 (packaging: Velopack's install, update and uninstall hooks), ADR-010 (commands: one implementation per operation, many entry points), ADR-013 (single instance: the activation pipe and its reserved `Uri` kind), ADR-019 (links in a note open only on the user's click, under an allow-list)
- Parity rows H5, H3, C2, C7, C16, E6 (#192); parity principle 10 (untrusted input, links and paths)

---

## Context

SideNotes has a URL scheme with two capabilities, `sidenotes://open/<uuid>` and `add-note-with-text`, and an AppleScript dictionary. Noto had neither: folders, notes and Search could not be reached from a script, a launcher or a link. H5 (the scheme) is a MUST; H3 (scripting) is an ADAPT whose outcome is "drive Noto from outside".

ADR-010 already decided that every mutation is a command, so a new entry point dispatches the existing commands rather than reimplementing them. ADR-013 already decided how a second launch reaches the running Noto, and reserved a `Uri` kind in its protocol for exactly this.

## Problem

1. What can a link ask for, and how is it written?
2. A link can come from **any web page**. What is it allowed to do, and what is refused?
3. How does a link or a command line reach a Noto that is already running, and one that is not?
4. Who registers the scheme with Windows, and who removes it?

## Options considered

| Option | Verdict |
|---|---|
| **A closed grammar of five verbs, parsed by one function, run through the window's existing paths** | **Chosen.** |
| A general scripting surface (every command reachable by name) | Every destructive command becomes reachable from a web page. The outcome H3 asks for is met by the five verbs plus the CLI. |
| A separate CLI grammar beside the link grammar | Two parsers drift apart; a rule fixed in one is missing in the other. |
| A new IPC channel for the requests | ADR-013's pipe already carries one validated request with a reply, and reserved the kind. |
| Registering the scheme at every start | A debug build or a portable copy would take the scheme over by running. |
| Registering machine-wide (`HKLM`) | Needs elevation, which Noto never has (ADR-007 §8), and reaches other users. |

## Decision

### 1. Grammar

```
  noto://new?text=<text>[&folder=<id-or-name>]   a note (SideNotes' add-note-with-text)
  noto://open/<id-or-name>                        a folder's notes (SideNotes' open/<uuid>)
  noto://folders                                  the folder list (C7)
  noto://new-folder?name=<name>                   a folder (C2)
  noto://search?q=<query>                         Search with the query in its box (E6)
```

- The scheme, the verbs and the parameter names ignore case. A verb is matched only in ASCII, never through a Unicode case mapping.
- Values are percent-decoded as UTF-8; unescaped characters, other scripts included, are taken as they are. `+` is a plus, not a space (RFC 3986, not HTML forms), so `C++` survives.
- One trailing `/` is allowed, since browsers add one. Empty `&&` pairs are skipped.
- A folder reference is an id first, else the first folder in display order with that name, ignoring case. Names are not unique (Case G), so **Copy link copies the id**.
- `text` and `q` may be empty: an empty note, or Search as Ctrl+F opens it.

The **command line** mirrors it, and every switch is turned into its link and read by the same parser:

```
  Noto.exe --new-note <text> [--folder <id-or-name>]
           --open-folder <id-or-name>
           --folders
           --new-folder <name>
           --search <query>
           noto://...            (how Windows passes a link to the registered handler)
```

A switch's value is the next argument, whatever it says. Other arguments (`--data-root=`, `--login`, `--smoke-test`) are passed over. Two requests on one command line are refused.

Code: `Noto.Core.Automation` — `NotoUri.Parse` and `NotoUri.Format`, `NotoCommandLine.Parse`, `AutomationRequest`. Pure, and unit tested exhaustively.

### 2. Trust model

A link can be clicked on any web page, so **a link may only create or show**. The grammar has no verb that deletes, moves, renames, overwrites, restores, exports, reads a file or runs anything, and a guard test keeps the window's handler free of those paths.

What is refused, with a trace naming the reason (never the input), and never a crash:

| Input | Reason |
|---|---|
| not `noto://` | `NotNoto` |
| a fragment, a bad `%` escape, a parameter without `=`, a path where none belongs, `/` inside a folder segment | `Malformed` |
| any verb but the five | `UnknownVerb` |
| a parameter the verb does not take, or one given twice; `--folder` without `--new-note` | `UnknownArgument` |
| a required parameter missing | `MissingArgument` |
| over the caps below | `TooLong` |
| a lone surrogate, or escapes that are not UTF-8 (overlong forms and encoded surrogates included) | `InvalidText` |
| a name or folder reference with nothing left once normalised | `EmptyName` |
| two requests on one command line | `MoreThanOne` |

**Caps.** A link is read at all only up to 64 Ki characters. A note's text is capped at **16 Ki characters** once cleaned; a name, a folder reference and a query at **256**.

**Cleaning.** A note's text loses every control character except newline and tab (so a CRLF becomes LF), and is otherwise stored exactly as given: plain text, never interpreted. A `<script>` in it is text; a link in it opens only on the user's click, under ADR-019's allow-list. Names, folder references and queries are one line, normalised as every folder name is (`FolderName.Normalise`). A reference that looks like a path (`..\..\Windows`) is a name to look for among the folders and nothing more.

**Nothing happens out of sight.** Noto shows itself (a launch: shown, restored or brought forward, never hidden) before it does any request. A refused request is a plain launch: Noto is shown and nothing else happens.

**Two places that do not run a request.** A start at sign-in (`--login`, ADR-015) ignores one, since it never shows anything. While the welcome tour is open (a new workspace, ADR-021), a request is refused (`reason=Welcome`): the tour is the user's first steps, as it is for the new-note shortcuts.

### 3. Routing

```
  first start:   command line -> NotoCommandLine.Parse -> owner -> window created -> folder list's first load -> RunAutomation
  second start:  command line -> NotoCommandLine.Parse -> ActivationRequest.For(request) = (Uri, NotoUri.Format(request))
                   -> pipe -> ActivationEnvelope.Decode (NotoUri.Parse again, at the boundary)
                   -> queue (at most 16 waiting) -> UI thread: launch (shows Noto), then each request in order -> RunAutomation
```

`RunAutomation` (`MainWindow.Automation.cs`) leaves Settings and the Recycle Bin, then runs the window's existing paths:

| Request | Path |
|---|---|
| new | the new-note shortcut's path (`NewNoteFromShortcut`), entering the named folder first: the placement setting and the save of the card being edited apply as to Ctrl+N; the note is created holding the text (`CreateNote`) |
| open | entering a folder, as Search opens one (`OpenFromSearch`): the card being edited is saved first |
| folders | leaving Search and the open folder (saving first) for the folder list |
| new-folder | `CreateFolder`, the new folder selected on the folder list |
| search | the search shortcut's path (`OpenSearchFromShortcut`), the query put in the box |

A folder that is not found is said on the surface the user is looking at, and traced (`reason=NoSuchFolder`).

**The protocol.** ADR-013 reserved `Uri` (kind 2) with an undefined payload. It is now defined: **the payload is a link `NotoUri.Parse` accepts**, in `NotoUri.Format`'s form; anything else is `BadPayload`, and the second launch is told `Rejected`. The command line travels as its link, so `Cli` (kind 3) stays reserved and rejected. The version stays 1: a v0.2 Noto rejects kind 2 as it always did. The largest message grows from 8 KiB to **64 KiB** (the `uint16` length's reach), so the longest request always fits: `Format` escapes only ASCII, so a character costs at most three bytes, and 3 × (16 Ki + 256) is under 64 KiB. A test encodes the worst cases.

Requests are not folded the way launches are (ADR-013's `PendingLaunch`): each asks for something of its own, so they wait in order beside the launch. The queue is bounded at 16, and one beyond that is dropped with a trace; its launch still shows Noto.

### 4. Registration

```
  HKCU\Software\Classes\noto
      (default)        = "URL:Noto"
      URL Protocol     = ""
      shell\open\command
          (default)    = "<install folder>\current\Noto.exe" "%1"
```

- **Written by Velopack's install and update hooks only** (`OnAfterInstallFastCallback`, `OnAfterUpdateFastCallback`, ADR-008), for the installed copy. An install takes the scheme over from whatever had it.
- **Removed by the uninstall hook only when it opens this copy** (`ProtocolRegistration.RemoveIfThis`), as the `Run` value is (ADR-015). A scheme that opens another program is left alone.
- **A build or a portable copy never registers.** Only an installed copy runs a hook. Its command line works all the same.
- **Per user.** `HKEY_LOCAL_MACHINE` is never touched; the registry seam (`IUserRegistry`) cannot name it.
- **Never fatal.** A hook that cannot write leaves Noto installed without the scheme.
- The command passes the link as one quoted argument and nothing else.

Unpackaged protocol activation needs no package identity: the per-user `Classes` key is all Windows reads. ADR-008's "protocol activation requires identity" concerned the packaged path.

Code: `ProtocolRegistration` (`Noto.Platform.Windows`). Its tests run against a fake registry.

### 5. Copy link (C16)

A folder's context menu has **Copy link**, which copies `noto://open/<folder-id>` and says "Link copied." on the status line. A clipboard another app holds open copies nothing, and says so.

## Rationale

- **One parser** is the only way the link and the command line stay the same product. Turning a switch into its link costs one line per verb.
- **A closed allow-list** is the only safe reading of a string that a web page wrote. Each verb that is added later is a decision, recorded here.
- **Validating at the pipe** keeps ADR-013's rule that a malformed request never reaches the owner's UI thread; validating again in the app costs nothing.
- **The window's own paths** mean ADR-010 holds: a note made by a link is made by the same code as one made by Ctrl+N.

## Consequences

### Positive

- H5's two capabilities, plus C2, C7 and E6 from a link and the command line, and C16.
- Scripts, launchers and other apps can drive Noto with no new channel and no new permissions.
- The scheme follows the installed copy through updates and leaves with it.

### Negative

- **A link is one-way.** It reports nothing back: a script cannot learn a new note's id. H3's read-side (listing, querying) is not covered.
- **A link from a web page can make a note or a folder.** It cannot remove or change anything, and Noto shows itself, but a page can add clutter if the user accepts the browser's prompt.
- **Command lines are visible** to other processes of the same user (the process list). Text passed with `--new-note` is as private as that, which is inside ADR-013's trust boundary.
- The envelope's maximum grew to 64 KiB. Still bounded; still one read.
- A link handed to a running Noto from before this change is rejected by it, and the second launch says the running Noto is a different version (ADR-013's exit 3). Restarting Noto fixes it.
- A link that arrives during the welcome tour is dropped.

## Non-goals

- Reading from Noto (list, export, query results) and a PowerShell module (H4).
- Note links (`noto://note/<id>`), and opening a note at a position.
- Opening a `noto://` link from inside a note: ADR-019's allow-list does not include it.
- Any destructive verb.

## Future reconsideration criteria

- **A read-side automation surface is wanted (H3, H4):** it needs a reply channel, which a link does not have; a named-pipe or COM surface, with its own ADR.
- **Note links are wanted:** add the verb here, with its allow-list entry and its own reference rule.
- **Noto ships as a sparse package (ADR-008):** declare the protocol in the manifest and stop writing the registry.
- **A verb that changes or removes data is proposed:** it must not be reachable from a web page; it needs a confirmation the page cannot give, or a channel the page cannot reach.
