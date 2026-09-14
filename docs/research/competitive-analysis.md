# Competitive Analysis

**Research date:** 2026-09-14
**Status:** Complete for the initial product decision
**Raw source material:** [`_raw/`](_raw/)

---

## How to read this document

Every factual claim about a competitor is labelled:

| Label | Meaning |
| ----- | ------- |
| **CONFIRMED** | Stated on the vendor's own site, documentation, or store listing. Source URL recorded in the raw research files. |
| **INFERRED** | Reasoned from confirmed facts or observed screenshots. Not stated by the vendor. |
| **UNKNOWN** | Could not be verified. Recorded as unknown rather than guessed. |

Two caveats that affect how much weight to give specific sections:

1. **@/Anchored has no independent coverage.** No reviews, no Reddit or Hacker
   News threads, no AlternativeTo entry. Every capability claim comes from the
   vendor's marketing site. Treat them as *claims*, not verified behavior.
2. **TSNotes could not be fetched.** Both `tsnotes.app` and `tomsparknotes.com`
   returned HTTP 403 to automated requests. That section is assembled from
   search snippets and is the weakest in this document.

Comparison pages published by a vendor about a competitor were treated as
marketing. Where Noticky's comparison pages admit Noticky's *own* gaps, those
admissions were treated as more credible than its claims about SideNotes.

---

## The nine products

| Product | Platform | Price | Category |
| ------- | -------- | ----- | -------- |
| SideNotes | macOS 13+ | $19.99, paid major upgrades | Edge sidebar notes |
| Noticky | macOS 15+ | $9.99, free updates | Floating + app-aware notes |
| @/Anchored | Windows 10/11 | Free (early access), paid AI tier planned | Window-anchored notes |
| Notezilla | Windows 10/11 (+mobile, web) | $29.95 once, or $19.95/yr with sync | Full sticky notes suite |
| Zhorn Stickies | Windows | Free | Lightweight sticky notes |
| Microsoft Sticky Notes | Windows | Free | Sticky notes (two products, see below) |
| TSNotes | Win / Mac / Linux | $10 once | Local markdown-ish notes |
| Simple Sticky Notes | Windows 7–11, ARM64 | Free, incl. commercial | Sticky notes |
| WindowTop | Windows | $19, 3 seats | Window utility (not notes) |

---

## 1. The market is split, and the gap is in the middle

The single most important finding:

```
  NOTES APP QUALITY
     high |
          |  SideNotes *          * Noticky
          |  (macOS)                (macOS)
          |
          |                            +--------------+
          |  Notezilla *-------*       |  UNOCCUPIED  |
          |                    |       |              |
          |  Simple Sticky *   |       |  Noto aims   |
          |  MS Sticky     *   |       |  here        |
          |  Zhorn         *---*       +--------------+
          |         (window-title
          |          matching)   * @/Anchored
      low |                        (no confirmed search,
          |                         tags, or folders)
          +-------------------------------------------------
            none        app-level     window-level     finer
                       CONTEXT AWARENESS
```

- **Good notes apps with good context are macOS-only.** SideNotes and Noticky
  are the two strongest products researched, and neither ships on Windows.
- **The Windows product built around context is not a notes app.** @/Anchored's
  anchoring is its entire proposition; research could not confirm it has search,
  tags, folders, or a note list.
- **The Windows products that *are* notes apps have shallow context.**

---

## 2. Contextual notes on Windows are not greenfield

This corrects an assumption worth stating plainly: **two Windows products have
attached notes to windows for years.**

| Product | Claim | Mechanism |
| ------- | ----- | --------- |
| Notezilla | "Stick notes to webpages, documents, programs, apps, folders, or any window." CONFIRMED | **Window-title string matching with `*` wildcards.** CONFIRMED — vendor help explicitly says "Notezilla uses the title of the window to show or hide a particular sticky note." |
| Zhorn Stickies | "Attached to an application, web site, document or folder so they only show when it's on screen." CONFIRMED | **UNKNOWN** — undocumented. INFERRED to be title-based. |
| @/Anchored | Note follows a window live as it moves and resizes. CONFIRMED (vendor claim) | **UNKNOWN** — deliberately never disclosed. |

This is good news, not bad news:

- it **proves demand** — the idea is not exotic, and a paid product has shipped
  it for years
- it **proves the shallow version is beatable** — title matching is fragile by
  construction

### Why title matching is weak

Notezilla's documented model asks the user to author a pattern such as
`*Google Search*` or `*Microsoft Outlook`. This means:

- no true document, file-path or URL identity
- false positives when unrelated windows share a title substring
- silent breakage when an application changes its title format
- no per-tab granularity beyond what the browser puts in the title bar
- **the user has to hand-craft and maintain wildcards**

That last point is the important one. It is configuration presented as context.

### The three approaches, and the unoccupied fourth

```
  user writes a title pattern      Notezilla, Zhorn     fragile, manual
  read the window with an AI        @/Anchored Atlas     probabilistic, heavy
  bind once, to one live window     @/Anchored core      coarse (40 tabs = 1)
  --------------------------------------------------------------------------
  resolve the real document         UNOCCUPIED           Noto's opportunity
  deterministically
```

@/Anchored's own AI layer is the tell. "Atlas reads the anchored window" to work
out what you are actually looking at — because the anchor itself does not know.
That is a workaround for coarse binding, sold as an AI feature.

---

## 3. What the whole category fails to do

Across all eight notes products:

| Missing | Detail |
| ------- | ------ |
| **Markdown** | Not one Windows product in this set supports it. OneNote still has "zero native markdown support" as of Feb 2026 despite years of requests. CONFIRMED |
| **Edge-docked sidebar** | All Windows products are floating notes plus a manager window. The SideNotes model does not exist on Windows. CONFIRMED (absence) |
| **Robust context identity** | Nobody binds to a URL, process, or document path. The two that try use title matching. CONFIRMED |
| **Local-first *and* encrypted *and* synced** | You get sync or local encryption, never both. CONFIRMED |
| **Capture tooling** | No clipboard watcher, snip-to-note, or OCR in any third-party product. Only Microsoft has capture. CONFIRMED |
| **Automation surface** | Only Zhorn has an API. No CLI, URL scheme or agent integration anywhere on Windows. CONFIRMED |
| **Modern Windows integration** | Virtual desktops, per-monitor DPI, Snap Layouts, Mica — undocumented across the board. Microsoft's own Dock-to-Desktop is broken on extended monitors. CONFIRMED |
| Tables, code blocks, note linking | Largely absent | 

**The Windows sticky-notes category looks like 2010.** That is the opening.

---

## 4. Product-by-product

### 4.1 SideNotes (macOS) — the sidebar benchmark

The reference implementation of the edge-drawer model, mature since 2018.

**Strengths:** the interaction model itself — pull from the edge, browse
folders, close, return to work. Folders. AppleScript, URL scheme and Shortcuts
automation.

**Weaknesses (repeated across years of user complaints, CONFIRMED):**

- the **edge trackpad gesture cannot be disabled** and conflicts with other
  gestures
- users want **keyboard-only activation** and do not have it
- no tags (folders only)
- paid major upgrades

> **Design rule for Noto, taken directly from this:** every activation surface
> — edge hover, hotkey, tray, gesture — must be independently disableable, and
> everything must be reachable from the keyboard. This is now principle 7.

### 4.2 Noticky (macOS) — the strongest feature ideas

Newer, smaller, and the source of the most interesting individual features.

Three worth stealing:

1. **App-aware notes.** A note binds to a Mac application; it appears when that
   app activates and hides when you switch away, **without stealing focus**.
   CONFIRMED. The Windows analogue via foreground-window hooks is tractable.
2. **Screen-capture exclusion.** Notes are hidden from screenshots, recordings
   and screen sharing. CONFIRMED, and corroborated by a real user review rather
   than only marketing. The Windows equivalent —
   `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` — is cheap to implement
   and directly serves principle 10.
3. **Sticky Screenshot.** Built-in region capture straight into a floating note.

Also notable: floating image overlays with click-through, opacity and lock; and
an **MCP server** exposing search/create/update/archive to AI assistants with
revocable permissions. That last one is category-first and worth tracking, but
is post-v1 for Noto.

**Weaknesses:** no folders (tags only), no attachments, no AppleScript, macOS 15+
only, essentially no independent reviews yet.

### 4.3 @/Anchored (Windows) — the direct competitor

Version 0.11.19, pre-1.0, free during early access, Windows only. The closest
thing to Noto that exists.

**What it does well (all vendor claims, none independently verified):**

- binds a note to one live OS window
- the note **follows live** as the window is dragged and resized — claimed as
  "the only app here that does it in real time"
- hides on minimize and close, returns and re-anchors when the window returns
- survives restarts and follows the window across monitors
- notes dock to the window's edges, six slots maximum

**Where it is weak:**

| Gap | Status |
| --- | ------ |
| Search | Never mentioned on any page — **probable gap** |
| Tags, folders, note list | Never mentioned — **probable gap** |
| Export, attachments, checklists | Never mentioned |
| Binding granularity | One window. 40 browser tabs share one anchor. |
| Virtual desktops, maximize, Aero Snap, occlusion, mixed DPI | **Entirely undocumented** |
| Market presence | No reviews, no community, no repo |

> With 200 notes across 80 windows there is no confirmed way to find anything.
> **@/Anchored is an anchoring engine, not yet a notes app.**

Its own taxonomy is genuinely useful and worth adopting as vocabulary:

```
  screen-pinned   -> fixed screen position, ignores everything
  z-pinned        -> stays above other windows, follows nothing
  window-anchored -> bound to one window; moves, hides and returns with it
```

**Two commercial flags:**

- The site asserts **"Patent Pending"** on the anchoring concept. Self-asserted,
  unexamined, scope unknown — but it signals intent to assert. Substantial prior
  art exists (Notezilla and Zhorn have shipped window attachment for years;
  TackNote and AnchorNotes on macOS; Anchored Notes as a browser extension).
  **This warrants a real freedom-to-operate check before window-following
  becomes Noto's headline claim.** Tracked as an open question in the vision.
- Anchored's own comparison table misstates WindowTop's price. Minor, but a
  reminder that vendor comparison pages are marketing.

### 4.4 Notezilla (Windows) — the one to beat commercially

The only *complete* product in the Windows set: attach-to-window, memoboards,
tags, checklists, images, reminders with snooze and recurrence, always-on-top,
opacity, encryption, master-password lock, cloud sync, iOS/Android/web clients.

$29.95 once for 2 PCs without sync, or $19.95/yr with it. No free tier.

**Its weakness is design and modernity.** The vendor itself acknowledges the UI
needs work. No markdown, no sidebar, title-based context, and nothing
documented about DPI, multi-monitor or virtual desktops.

Notezilla proves the market will pay for this category. It does not set a high
bar for craft.

### 4.5 Microsoft Sticky Notes — messier than expected

Worth stating carefully, because the current state is confusing and commonly
misreported.

**There are two different products both called Sticky Notes:**

| | Legacy UWP app | New experience |
| --- | --- | --- |
| Version | 6.1.4.0 (Oct 2024) | Built **inside OneNote**, `Win+Alt+S` |
| Status | Feature-frozen | "The supported direction" |
| Always on top | **Cannot** — per Microsoft's own docs | Yes, pin icon |
| Font change | **Cannot** — per Microsoft's own docs | — |
| Capture | — | One-click screenshot, OCR-powered search, automatic source capture and recall |

**On Copilot:** despite widespread assumption, there is **no shipped Copilot
feature in Sticky Notes**. Windows Central stated explicitly that the major
update "has nothing to do with AI… or Copilot" — the intelligence is OCR, not
generative AI. The old Cortana "Insights" is 2016-era and not current.

**The backlash is well documented:** slower, lost notes, forced OneNote
coupling, and users deliberately reverting to the legacy app.

No reminders, no markdown, no password protection, no opacity.

> The most interesting thing here is the **automatic source capture and recall**
> — Microsoft recording where a screenshot came from. It is the same instinct as
> contextual notes, implemented narrowly.

### 4.6 Simple Sticky Notes — the best free option

v6.9, **updated February 2026** — the most actively maintained of the Windows
set. Windows 7–11 including ARM64, free even for commercial use. Notebooks,
search, alarms, checklists, dark mode, password lock.

**Cannot hold images.** No sync. (One review site claims sync; that appears to
be an error.)

### 4.7 Zhorn Stickies — small, old, and quietly interesting

v10.2a (Jan 2025), free, **2.8 MB**, 32-bit, and notably **does not write to the
registry**. Attach-to-window, sleep/wake scheduling, Group Policy support, and a
public API — the only automation surface in the Windows set.

Dated UI, roughly annual releases, no tags, no encryption, no cloud. Its own
front page still advertises "Windows 7/8/10" while the download page says 11.

The 2.8 MB footprint is a useful reminder of what this category *can* cost.

### 4.8 TSNotes — insufficient data

$10 once, Windows/Mac/Linux, local JSON storage, per-note AES-256, hashtags,
wikilinks and daily notes. Absent from every 2026 roundup found.

**Both official domains return HTTP 403 to automated requests.** This section
needs a manual browser visit before any decision depends on it.

### 4.9 WindowTop — not a competitor, but a feasibility proof

A Windows window-management utility ($19, 3 seats), included because it
demonstrates what an external process can do to *other applications' windows*:

- always-on-top, per-window opacity, click-through
- borders drawn on foreign windows
- interactive picture-in-picture mirroring
- **persisted per-window configuration**

It does **not** do live position-following, so it does not validate Anchored's
core trick.

**Its public issue tracker is the genuinely valuable artifact.** With 126
issues, the dominant recurring defect theme is **high-DPI and mixed-DPI
multi-monitor**. That is precisely where this class of software breaks, and it
is a direct warning for Noto: Anchored does *continuous* edge-docking, which is
harder than WindowTop's one-shot operations, and is silent on mixed DPI.

---

## 5. What Noto should take from this

| Decision | From | Rationale |
| -------- | ---- | --------- |
| **KEEP** — edge sidebar as the primary surface | SideNotes | The best interaction model researched, and absent from Windows entirely |
| **KEEP** — floating notes with always-on-top, opacity, lock, click-through | Noticky, WindowTop | Proven feasible from an external process |
| **IMPROVE** — context binding | Notezilla, Anchored | Replace title-pattern matching and coarse window binding with deterministic document-level resolution |
| **COMBINE** — folders *and* tags | SideNotes has folders, Noticky has tags | Neither has both; the combination is cheap |
| **INNOVATE** — markdown | Nobody on Windows | Zero competition on a table-stakes feature for the target user |
| **INNOVATE** — keyboard-first everything | SideNotes' loudest complaint | Direct response to years of user feedback |
| **KEEP** — screen-capture exclusion | Noticky | Cheap, serves principle 10, and users notice it |
| **DEFER** — AI / MCP surface | Noticky, Anchored Atlas | Category-first and interesting, but post-v1; and for Noto, AI is explicitly *not* the answer to context resolution |
| **DEFER** — sync | Notezilla | Post-v1, bring-your-own storage if at all |
| **REJECT** — reminders as a first-class system | Notezilla, Simple Sticky | Drifts toward task management, violates principle 1 |
| **REJECT** — mobile and web clients | Notezilla | Violates the Windows-native focus |

The full row-by-row matrix is in [feature-matrix.md](feature-matrix.md).

---

## 6. Open verification items

Recorded rather than guessed. None currently blocks the MVP decision.

| # | Item | Why it matters |
| - | ---- | -------------- |
| 1 | TSNotes — both domains 403; needs a manual browser visit | Only markdown-adjacent product in the set |
| 2 | @/Anchored — install and test hands-on against the 16-item checklist in [`_raw/anchored-windowtop.md`](_raw/anchored-windowtop.md) | Every capability claim is vendor-only |
| 3 | @/Anchored — "Patent Pending" scope and prior-art check | Could affect the headline feature |
| 4 | Notezilla — whether local at-rest storage is encrypted by default | Informs Noto's own encryption decision |
| 5 | New Sticky Notes — whether it now hard-requires a Microsoft account | Sharpens the local-first contrast |
| 6 | Simple Sticky Notes — several feature pages returned 404 | Minor |

Items 2 and 3 should be done before the Context Engine milestone (M5) begins.
