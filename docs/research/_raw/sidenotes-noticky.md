# Competitor Research: Apptorium SideNotes & Noticky

**Researched:** 2026-09-14
**Researcher:** automated web research (WebSearch + WebFetch)
**For:** Noto (Windows notes app) — product decision matrix

## Evidence labels

- **CONFIRMED** — stated on official site, official docs, or App Store / Setapp listing. URL cited.
- **INFERRED** — reasonably deduced from screenshots, reviews, comparison pages, or adjacent statements. Not directly stated.
- **UNKNOWN** — could not confirm. Do not treat as a feature gap OR a feature.

## Source-bias warning

Several comparison pages (`noticky.app/en/alternative/sidenotes`, `noticky.app/en/blog/noticky-vs-sidenotes-mac`, `noticky.app/en/blog/best-sticky-note-apps-mac`) are **published by Noticky's own vendor**. Their claims about SideNotes are marked INFERRED at best, and several contradict SideNotes' own official site (flagged inline below).

A GitHub repo `SideNotes-OSX/SideNotes-Mac` surfaced in search. It is **not** linked from apptorium.com and appears to be an unofficial mirror or SEO/scam listing. **Nothing from it is used in this document.**

---

# 1. Apptorium SideNotes

**Observed:** 2026-09-14
**Version observed:** 1.6.5 on Mac App Store, released ~6 days prior to observation (so ~early Sept 2026). Site markets "v1.6" feature set.
**Vendor:** Apptorium (Poland-based indie; also makes Workspaces)

## 1.1 Platform, pricing, licensing

| Item | Value | Label | Source |
|---|---|---|---|
| Platform | macOS only (desktop) | CONFIRMED | https://www.apptorium.com/sidenotes |
| macOS requirement | macOS 13 or later | CONFIRMED | https://www.apptorium.com/sidenotes/buy |
| Architecture | Apple Silicon + Intel | CONFIRMED | https://www.apptorium.com/sidenotes |
| Windows version | Does not exist | CONFIRMED (absence of any Windows offering across all vendor pages) | https://www.apptorium.com/ |
| Price (direct + MAS) | $19.99 one-time | CONFIRMED | https://apps.apple.com/us/app/sidenotes/id1441958036 |
| Subscription | None | CONFIRMED | https://www.apptorium.com/sidenotes/buy |
| License scope | 1 user, **up to 5 Macs** | CONFIRMED | https://www.apptorium.com/sidenotes/buy |
| Major upgrades | **Paid separately.** "Free updates within the current major version" only | CONFIRMED | https://www.apptorium.com/sidenotes/buy |
| Trial | Free trial from website; MAS listing references a 30-day trial on developer site | CONFIRMED | https://apps.apple.com/us/app/sidenotes/id1441958036 |
| Refunds | 14-day money-back guarantee (direct purchase, Paddle checkout) | CONFIRMED | https://www.apptorium.com/sidenotes/buy |
| Student discount | Yes, via Student App Centre | CONFIRMED | https://www.apptorium.com/sidenotes/buy |
| Setapp | Included in Setapp subscription ($14.99/mo, 7-day trial) | CONFIRMED | https://setapp.com/apps/sidenotes |
| Mobile | **SideNotes Mobile sold separately**, iOS/iPadOS 17+ | CONFIRMED | https://www.apptorium.com/sidenotes |
| App size | 15 MB (MAS) / 35.5 MB (Setapp listing) | CONFIRMED | MAS + Setapp listings (discrepancy likely universal vs thinned binary) |
| Localization | 9 languages | CONFIRMED | https://setapp.com/apps/sidenotes |
| In-app purchases | None | CONFIRMED | https://apps.apple.com/us/app/sidenotes/id1441958036 |

**Note on pricing model:** the paid-major-upgrade model is the most commercially notable thing here. Noticky's marketing frames it as "$19.99 **per major version**." That framing is INFERRED-hostile but consistent with Apptorium's own wording.

## 1.2 Core interaction model

**This is the defining characteristic: SideNotes is a single sidebar drawer, not individual note windows.**

- CONFIRMED — SideNotes "covers one side of your Mac's screen with notes"; it is an overlay panel anchored to a screen edge. Source: https://setapp.com/apps/sidenotes
- CONFIRMED — the panel stays above other windows, **including full-screen apps and Stage Manager layouts**. Source: https://www.apptorium.com/sidenotes
- CONFIRMED — three ways to toggle the panel:
  1. **Keyboard shortcut** (customizable; site shows `⌃⌥⌘` as an example binding)
  2. **Open Bar** — a thin clickable strip on the screen edge
  3. **Hot Side** — move the mouse cursor to the screen edge to auto-reveal
  Sources: https://www.apptorium.com/sidenotes , https://www.apptorium.com/sidenotes/features
- CONFIRMED — a menu bar icon is also available as an entry point. Source: https://www.apptorium.com/sidenotes/features
- CONFIRMED — `Command-Return` hides SideNotes (documented tip "How to Hide SideNotes using Command-Return"). Source: https://www.apptorium.com/sidenotes/tips
- INFERRED — an edge **two-finger trackpad gesture** also opens the panel, and **cannot be disabled**. Evidence: App Store review (Aosijfdgiwe, 2024-10-17) complaining gestures "sometimes will conflict with other gestures." Source: https://apps.apple.com/us/app/sidenotes-screen-edge-notes/id1441958036?see-all=reviews

**Individual floating notes: UNKNOWN / likely absent.** No official page mentions detaching a note into its own always-on-top window. The competitor comparison asserts SideNotes "opens a panel above fullscreen but doesn't keep individual notes persistent" — INFERRED (vendor-biased source), but consistent with the absence of any official claim to the contrary.

## 1.3 Sidebar / edge docking behavior

| Behavior | Detail | Label | Source |
|---|---|---|---|
| Edges supported | **Left and right only.** "Move the app easily between the left and the right side of your screen" | CONFIRMED | https://www.apptorium.com/sidenotes/features |
| Top / bottom edge | Not offered | INFERRED (only left/right ever mentioned) | — |
| Hot Side (edge reveal) | Cursor to screen edge shows/hides the panel | CONFIRMED | https://www.apptorium.com/sidenotes/tips/how-to-hide-open-bar |
| Hot **corner** | Not a documented feature — it is edge-based ("Hot Side"), not corner-based | INFERRED | — |
| Open Bar auto-hide | 3 options under **Settings > General > Hide Open Bar**: `never` (default), `when mouse is inactive`, `always` — `always` only selectable when Hot Side or Menubar Icon mode is active | CONFIRMED | https://www.apptorium.com/sidenotes/tips/how-to-hide-open-bar |
| Auto-hide delay tunable | Not exposed; "after a few seconds of mouse cursor inactivity" is fixed | INFERRED | same as above |
| Keyboard-only mode | Not cleanly available — user complaint: "lack of a keyboard trigger only option" (verotheelf, 2020-11-21); a later reviewer called the forced choice "between a visible bar that can also open up accidentally and another icon on my menu bar... a big pet peeve" | CONFIRMED (as user complaints) | MAS reviews |
| Panel width | Resizable — documented tip "How to Change Window Size?" | CONFIRMED | https://www.apptorium.com/sidenotes/tips |
| Exact width range / px | UNKNOWN | UNKNOWN | — |
| Does it reserve desktop space (like a dock) or overlay? | Overlay on top of windows | CONFIRMED | https://www.apptorium.com/sidenotes |
| Multi-monitor behavior | **UNKNOWN.** No official doc; tips index has no multi-monitor article | UNKNOWN | https://www.apptorium.com/sidenotes/tips |

## 1.4 Note organization

- CONFIRMED — **Folders.** "Organize your folders the way you like." Folders group notes; notes can be moved between folders. Source: https://www.apptorium.com/sidenotes/features
- UNKNOWN — **Nested folders.** The Noticky comparison claims "nested folders," but Apptorium's own copy never says nested/sub-folders. Treat depth as UNKNOWN.
- CONFIRMED — **Pinning.** "Keep your most important notes and folders at the top." Both notes *and* folders pin. Source: https://www.apptorium.com/sidenotes/features
- CONFIRMED — **Manual ordering.** "Easily move notes and folders up or down by drag & drop"; keyboard tip "How to Move a Note to the Top or Bottom Quickly?" Sources: features page, tips page
- CONFIRMED — **Note colors.** Customizable, with multiple display options. Source: features page
  - CONFIRMED (as complaint) — colors render as an **edge stripe**, not a full-note background. User request: "allow the whole note to be a light color instead of just a line" (CareyAndrew, 2019-11-21). Unclear whether later versions addressed this.
- CONFIRMED — **Folding.** "Collapse long notes to save screen space." Source: features page
- **Tags: UNKNOWN / likely absent.** Apptorium's official pages never mention tags. The vendor-biased comparison table lists SideNotes as "Folders instead" of tags. INFERRED: no tag system.
- **Archive: UNKNOWN.** No archive or trash feature documented.
- CONFIRMED — **Folder switching shortcuts** exist ("How to Switch Between Folders Quickly?", "How to Switch Quickly to the Last Folder?"). Source: tips page

## 1.5 Editor

| Capability | Status | Label | Source |
|---|---|---|---|
| Markdown | Yes, with "invisible markup rendering" (markup hidden once rendered) | CONFIRMED | https://www.apptorium.com/sidenotes |
| Three editor modes | **Standard (markdown), Plain Text, Code** | CONFIRMED | https://setapp.com/apps/sidenotes |
| Code / syntax highlighting | Monospaced font for code; comparison page claims syntax-highlighted snippets | CONFIRMED (monospace) / INFERRED (highlighting) | Setapp listing; noticky.app comparison |
| Checklists / tasks | Yes ("Tasks", checkbox functionality) | CONFIRMED | features page, Setapp |
| Images | Yes ("Pictures"); drag & drop images in | CONFIRMED | features page |
| File & folder shortcuts | Yes — "File & Folder Shortcuts" (links to files/folders on disk) | CONFIRMED | https://www.apptorium.com/sidenotes/features |
| PDF / arbitrary attachments | INFERRED yes (comparison page asserts "images, PDFs, sketches") | INFERRED | noticky.app/en/alternative/sidenotes |
| Hex color swatches | Yes — `#rrggbb` colors render as swatches | CONFIRMED | features page |
| Tables | **UNKNOWN** — never mentioned | UNKNOWN | — |
| Quick formatting toolbar | Yes, appears on text selection (v1.6) | CONFIRMED | https://www.apptorium.com/sidenotes |
| Auto-link from pasted URL | Yes (added v1.6.4) | CONFIRMED | MAS "What's New" |
| Soft line breaks | Yes (added v1.6.4) | CONFIRMED | MAS "What's New" |
| Font / size / dark mode | Customizable | CONFIRMED | features page |
| Themes | Custom or pre-made themes | CONFIRMED | features page |
| Text import | Drag & drop text files in | CONFIRMED | features page |
| Printing | **Was unsupported**; developer replied "Now, it supports printing" to a 2024-10-01 review | CONFIRMED | MAS reviews |

## 1.6 Floating / always-on-top notes

- CONFIRMED — the **panel** is always-on-top and survives full-screen apps and Stage Manager. Source: https://www.apptorium.com/sidenotes
- INFERRED — **individual notes cannot be torn off into separate floating windows.** No official mention anywhere. This is the single clearest architectural difference vs Noticky.

## 1.7 Contextual behavior (app/window binding)

- **UNKNOWN / almost certainly absent.** No Apptorium page mentions binding notes to applications, windows, projects, or workspaces. The tips index has no article on app-specific behavior.
- Note: Apptorium's *other* product, Workspaces, is the project-context app. SideNotes does not appear to inherit that.

## 1.8 Capture

- CONFIRMED — four creation paths listed officially: `+` button, **Drag and Drop**, **Paste Clipboard Content**, **Use a Global Shortcut**. Source: https://www.apptorium.com/sidenotes/features
- CONFIRMED — **Global Shortcuts "work with SideNotes even when its window is hidden."** Source: features page
- CONFIRMED — screenshots are a stated use case ("project materials, snippets, screenshots, links"). Source: search result quoting product copy.
- **UNKNOWN** — no built-in screen-region capture tool documented (unlike Noticky's Sticky Screenshot). Capture appears to rely on macOS screenshot + paste/drag.
- CONFIRMED — third-party capture integrations shipped: **Alfred, Hookmark, PopClip, Dropzone, Raycast**. Source: https://www.apptorium.com/sidenotes

## 1.9 Search

- CONFIRMED — built-in search exists; documented tip "How to Access Search Quickly?" (keyboard shortcut). Source: https://www.apptorium.com/sidenotes/tips
- CONFIRMED — search results can **launch a website** directly ("How to Launch a Website from Search Results?"). Source: tips page
- CONFIRMED — search is used as a **text snippet retrieval mechanism** ("How To Use Text Snippets?"). Source: tips page
- CONFIRMED — **AppleScript API can search notes**. Source: features page
- CONFIRMED — **Alfred workflow** provides external search. Source: features page
- UNKNOWN — fuzzy vs literal matching, whether search covers note bodies vs titles, scoped vs global search.

## 1.10 Sync, privacy, encryption

- CONFIRMED — **iCloud sync**: "Automatically synced across Mac, iPhone, and iPad." Source: features page
- CONFIRMED — **automatic backups**, with local and iCloud storage options. Source: features page
- CONFIRMED — **notes stored locally; no data collection or analytics.** Source: https://www.apptorium.com/sidenotes
- CONFIRMED (user report) — sync is fast: "syncing speed between my Mac and phone is incredibly fast" (Cspain11, 2023-04-14). Source: MAS reviews
- **No account system** — INFERRED (iCloud-only, no sign-up mentioned anywhere).
- **Per-note lock / biometric / encryption at rest: UNKNOWN, likely absent.** Never mentioned. The vendor-biased table marks Touch ID lock as absent for SideNotes.
- No Windows/Android/web sync path exists.

## 1.11 Multi-monitor, shortcuts, automation

- **Multi-monitor: UNKNOWN.** Not documented.
- CONFIRMED — global shortcuts work while the window is hidden. Source: features page
- CONFIRMED — extensive in-app keyboard shortcuts: panel toggle, search, folder switching, move note to top/bottom, `Command-Return` to hide. Source: tips page
- CONFIRMED — **automation surface is strong**, and is SideNotes' standout technical asset:
  - **Apple Shortcuts actions** — "Create notes and folders and integrate SideNotes with other apps"
  - **AppleScript API** — "Create and modify notes and folders, search for them"
  - **URL scheme API** — notes and folders have URLs ("Note and Folder URLs")
  - Source: https://www.apptorium.com/sidenotes/features , https://www.apptorium.com/sidenotes
- CONFIRMED — **Share Extension** to Messages, Notes, Reminders (and Dropshare per Setapp). Source: features page, Setapp
- CONFIRMED — **export notes to images**. Source: features page
- CONFIRMED — pre-built integrations: Alfred, Hookmark, PopClip, Dropzone, Raycast. Source: https://www.apptorium.com/sidenotes

## 1.12 Reception, strengths, weaknesses

**Ratings observed 2026-09-14:**
- Mac App Store: **4.6 / 5 from 48 ratings** (low volume — most sales likely direct/Setapp). Source: https://apps.apple.com/us/app/sidenotes-screen-edge-notes/id1441958036?see-all=reviews
- Setapp: **97% approval from 1,470 reviews** (much higher sample). Source: https://setapp.com/apps/sidenotes

**Strengths (CONFIRMED from reviews/sources):**
- Speed of access — "The speed and ease with which I can access it... make it my top note-taking app"
- Polish and native macOS feel — third-party writeups describe it as "polished, reliable, and feels perfectly at home on macOS"
- Folder organization depth — positioned by comparison sources as "best for organized reference notes"
- Fast iCloud sync
- Deep automation (AppleScript + Shortcuts + URL scheme) — rare in this category
- Mature: shipping since ~2018/2019 (earliest reviews 2019)

**Weaknesses / complaints (CONFIRMED as user statements):**
| Complaint | Source review | Date | Status |
|---|---|---|---|
| No keyboard-trigger-only mode; forced to have Open Bar *or* menu bar icon | verotheelf; also an unnamed reviewer calling it "a big pet peeve" | 2020-11-21 + later | Partially addressed via Hide Open Bar options |
| Edge two-finger gesture conflicts with other gestures and can't be disabled | Aosijfdgiwe | 2024-10-17 | Unresolved as of that date |
| No printing | mmcwatters | 2024-10-01 | **Fixed** — dev replied "Now, it supports printing" |
| Note color is only an edge stripe, not full background | CareyAndrew | 2019-11-21 | Unknown |
| No mobile app (at the time) | faith0704 | 2021-04-05 | **Fixed** — SideNotes Mobile shipped (sold separately) |
| Mobile app lacks the desktop's ease of use; wants widgets/Shortcuts/Siri | iOS reviews | — | Ongoing |
| Paid major upgrades | structural | — | By design |

**Structural weaknesses (INFERRED, for Noto's matrix):**
- No tags — folders only, so no cross-cutting organization
- No per-note floating windows — everything lives in one drawer
- No app/window context binding
- No per-note encryption or lock
- Mobile is a separate $ purchase
- macOS-only, iCloud-only — zero cross-platform story

---

# 2. Noticky

**Observed:** 2026-09-14
**Version observed:** macOS **1.9.0**, updated ~4 days prior to observation (≈ 2026-09-10); MAS listing elsewhere shows last-update 2026-08-30.
**Developer:** Leonidas Mbuembue Nyunyi (solo indie)

## 2.1 Platform, pricing, licensing

| Item | Value | Label | Source |
|---|---|---|---|
| Platform | macOS + iOS/iPadOS (separate apps) | CONFIRMED | https://www.noticky.app/en , https://www.noticky.app/en/ios |
| macOS requirement | **macOS 15 Sequoia or later** (aggressive — excludes older Macs) | CONFIRMED | https://www.noticky.app/en |
| Tech stack | Native Swift / AppKit / SwiftUI. Explicitly **not Electron** | CONFIRMED | https://www.noticky.app/en/blog/what-is-noticky |
| Windows version | Does not exist | CONFIRMED (absence) | — |
| Mac price | **$9.99 one-time** on official site; MAS listing showed $9.99 at time of check, though some third-party listings quote $6.99 (likely a past sale or regional price) | CONFIRMED w/ discrepancy | https://www.noticky.app/en ; https://apps.apple.com/us/app/noticky/id6770181778 |
| Subscription | None. "No subscription, no account required, no telemetry" | CONFIRMED | https://www.noticky.app/en |
| Future updates | "All future updates included" — **no paid major upgrades** | CONFIRMED | https://www.noticky.app/en |
| Distribution | Mac App Store, direct download, **Setapp** | CONFIRMED | https://www.noticky.app/en |
| iPhone/iPad app | Shipped. Free tier = 1 wall, max 8 notes. **Noticky Pro $4.99 one-time** = unlimited walls, up to 64 notes/wall, all templates | CONFIRMED | https://www.noticky.app/en/ios |
| Mac + iOS bundled? | **No** — "each app is sold separately (Mac $9.99 + iPhone $4.99)"; "iCloud syncs your notes, not your purchases" | CONFIRMED | https://www.noticky.app/en/ios |
| In-app purchases (Mac) | None | CONFIRMED | MAS listing |
| App size | 9.4 MB | CONFIRMED | MAS listing |
| Rating | **Insufficient ratings to display an average** on MAS — app is new / low volume | CONFIRMED | MAS listing |

**Key commercial contrast:** Noticky is half SideNotes' price *and* includes all future major versions. That is a real differentiator, not marketing.

## 2.2 Core interaction model

**Noticky is the inverse of SideNotes: many individual floating note windows, not one drawer.**

- CONFIRMED — **Menu bar agent. No Dock icon, no App Switcher presence.** Source: https://www.noticky.app/en/blog/what-is-noticky
- CONFIRMED — **Always on Top** is the headline feature: "Your note floats above everything — even fullscreen apps." Achieved via specialized window layering. Source: https://www.noticky.app/en
- CONFIRMED — **Quick Capture**: press `⌘⇧N` from anywhere, type or drop in images, `Return` saves and closes; `⌘Return` opens the note; `⌥Return` continues capturing. Source: https://www.noticky.app/en
- CONFIRMED — **Quick Access slots**: nine keyboard slots. "One shortcut brings back the same note, even when it is closed, hidden, or docked." Source: https://www.noticky.app/en
- CONFIRMED — **Show/Hide All** toggle: `⌘⇧H`. Source: https://www.noticky.app/en
- CONFIRMED — **Layout Modes**: Free placement / Stack cascade / Grid tile, switched with `⌘⇧1` / `⌘⇧2` / `⌘⇧3`. Source: https://www.noticky.app/en
- CONFIRMED — **Note collapse** and **inactive fade** (notes dim when not focused). Source: https://www.noticky.app/en
- CONFIRMED — **Open on launch** option. Source: https://www.noticky.app/en

## 2.3 Edge docking behavior

- CONFIRMED — **Edge Dock**: "Dock a note to the left or right edge as a compact tab. Reveal it when you need it without leaving the full note on screen." Source: https://www.noticky.app/en
- **Edges: left and right only** — CONFIRMED (top/bottom never mentioned).
- **Per-note docking**, not a single app-wide sidebar — INFERRED from wording ("Dock *a note*"), high confidence.
- **Auto-hide / hover-reveal semantics: UNKNOWN.** "Reveal it when you need it" does not specify hover vs click vs hotkey.
- **Dock tab width / note width: UNKNOWN.**
- **Hot corner: UNKNOWN / not mentioned.**
- **Multi-monitor behavior: UNKNOWN.** No documentation found despite targeted search.

**This is the closest structural analogue to SideNotes in Noticky's feature set, but it is a secondary feature there, not the core model.**

## 2.4 Note organization

- CONFIRMED — **Smart Tags** with color coding and **instant search**. This is the primary organization axis. Source: https://www.noticky.app/en , https://www.noticky.app/en/blog/what-is-noticky
- CONFIRMED (by vendor comparison, and consistent with absence elsewhere) — **No folders.** Tags are multi-label; SideNotes is hierarchical folders. Source: https://www.noticky.app/en/alternative/sidenotes
- **Nesting: absent** — INFERRED (tag-based system, no hierarchy mentioned).
- CONFIRMED — **Custom colors** per note. Source: https://www.noticky.app/en
- CONFIRMED — **Note skins**: Receipt, Blueprint, Terminal, Cork. Source: https://www.noticky.app/en/blog/what-is-noticky
- CONFIRMED — **Templates** supported. Source: https://www.noticky.app/en
- CONFIRMED — **30-day Trash** for recovery. Source: https://www.noticky.app/en
- **Archive (distinct from trash): UNKNOWN.** Note: the MCP integration description mentions "archive" as an available operation, so an archive state INFERRED to exist.
- **Manual ordering / pinning within a list: N/A** — notes are spatially placed (Free) or auto-arranged (Stack/Tile), so ordering is positional rather than list-based. INFERRED.
- CONFIRMED — **RTL language support**. Source: https://www.noticky.app/en

## 2.5 Editor

| Capability | Status | Label | Source |
|---|---|---|---|
| Markdown | **WYSIWYG live-rendered.** "Type Markdown, see it rendered instantly. Headers, bold, lists, code blocks — all live-rendered as you type" | CONFIRMED | https://www.noticky.app/en |
| Code blocks | Yes, via Markdown | CONFIRMED | https://www.noticky.app/en |
| Syntax highlighting in code blocks | UNKNOWN | UNKNOWN | — |
| Checklists | Yes, **with progress tracking** | CONFIRMED | https://www.noticky.app/en/blog/what-is-noticky |
| Checklist ↔ Apple Reminders sync | Yes — "Apple Reminders integration with checklist synchronization" | CONFIRMED | https://www.noticky.app/en |
| Calendar mentions | Yes — "Apple Calendar event mentions and previews" | CONFIRMED | https://www.noticky.app/en |
| Images | Yes — drag-and-drop image support | CONFIRMED | https://www.noticky.app/en |
| Tables | **UNKNOWN** — never mentioned | UNKNOWN | — |
| File links / folder shortcuts | **UNKNOWN / likely absent** — vendor comparison marks "File Attachments: No" for Noticky vs "Yes" for SideNotes | INFERRED absent | https://www.noticky.app/en/blog/noticky-vs-sidenotes-mac |
| General attachments (PDF etc.) | Likely absent — images only | INFERRED | same |
| Export | PDF, Markdown, plain text (`.pdf` / `.md` / `.txt`) | CONFIRMED | https://www.noticky.app/en |
| Print | UNKNOWN | UNKNOWN | — |

## 2.6 Floating / always-on-top notes

This is Noticky's core product thesis.

- CONFIRMED — notes float above **every** window including fullscreen apps, via specialized window layering. Source: https://www.noticky.app/en
- CONFIRMED — **Floating Image Overlays**: "Turn screenshots or clipboard images into floating overlays. Adjust **opacity**, **lock them in place**, **click through**, **round the corners**." Source: https://www.noticky.app/en
  - Click-through (mouse events pass to the window beneath) is a genuinely differentiated capability.
- CONFIRMED — **Inactive fade**: notes de-emphasize when not focused, reducing visual noise. Source: https://www.noticky.app/en
- CONFIRMED — app-aware notes appear **"without stealing focus."** Source: https://www.noticky.app/en

## 2.7 Contextual behavior (app/window binding)

**Noticky has this; SideNotes does not. Most important functional differentiator.**

- CONFIRMED — **App-Aware Notes**: "Link a note to a Mac app. It appears when that app becomes active" and automatically hides when you switch away, **without stealing focus**. Source: https://www.noticky.app/en
- Binding granularity is **per-application**, not per-window or per-document — INFERRED from wording ("Link a note to a Mac app"), high confidence.
- **Multiple notes bound to one app: UNKNOWN.**
- **Per-window / per-URL / per-project binding: UNKNOWN, likely absent.**

## 2.8 Capture

- CONFIRMED — **Quick Capture** global hotkey `⌘⇧N` from anywhere; type or drop images; `Return` = save+close, `⌘Return` = open note, `⌥Return` = keep capturing. Source: https://www.noticky.app/en
- CONFIRMED — **Sticky Screenshot**: "Capture any area of your screen and pin it as a floating reference instantly." Built-in region capture — SideNotes has no equivalent. Source: https://www.noticky.app/en
- CONFIRMED — **clipboard images** become floating overlays. Source: https://www.noticky.app/en
- CONFIRMED — **drag-and-drop images** into notes. Source: https://www.noticky.app/en
- CONFIRMED — **PopClip support** (text-selection capture from other apps). Source: https://www.noticky.app/en (features list)
- CONFIRMED — nine **Quick Access** keyboard slots for recalling specific notes. Source: https://www.noticky.app/en

## 2.9 Search

- CONFIRMED — **"Smart Tags with instant search."** Source: https://www.noticky.app/en
- CONFIRMED — MCP integration exposes a **search** operation over notes. Source: https://apps.apple.com/us/app/noticky/id6770181778
- UNKNOWN — full-text body search vs tag/title only; fuzzy matching; search UI/shortcut.

## 2.10 Sync, privacy, encryption

- CONFIRMED — **iCloud sync across Macs via CloudKit**, "end-to-end encrypted — Apple handles the security." Source: https://www.noticky.app/en , https://www.noticky.app/en/blog/what-is-noticky
  - Caveat worth recording: CloudKit private-DB E2EE is only truly end-to-end with Apple **Advanced Data Protection** enabled. The claim is vendor phrasing, not an independent audit.
- CONFIRMED — **No account required.** Source: https://www.noticky.app/en
- CONFIRMED — **No telemetry.** "Notes are stored locally + iCloud... We never see your data." Source: https://www.noticky.app/en
- CONFIRMED — **Touch ID biometric lock per note.** Source: https://www.noticky.app/en
- CONFIRMED — **Screen Sharing Privacy**: notes can be hidden from screenshots, screen recordings, and screen sharing, "Powered by Hide From Screen Capture technology" (i.e. macOS `sharingType` / window-capture exclusion). Source: https://www.noticky.app/en
  - Validated by a user review: "they automatically hide themselves whenever I share my screen." Source: MAS listing
- CONFIRMED — iOS app is "fully offline by default; iCloud sync is optional." Source: https://www.noticky.app/en/ios
- CONFIRMED — Mac↔iPhone sync via Apple Continuity/iCloud; **purchases do not transfer**. Source: https://www.noticky.app/en/ios

## 2.11 Multi-monitor, shortcuts, automation

- **Multi-monitor: UNKNOWN.** Targeted search found no Noticky-specific multi-monitor documentation.
- CONFIRMED — documented keyboard shortcuts:
  - `⌘⇧N` quick capture
  - `⌘⇧H` show/hide all
  - `⌘⇧1` / `⌘⇧2` / `⌘⇧3` layout modes (Free / Stack / Tile)
  - `Return` / `⌘Return` / `⌥Return` in capture
  - 9 Quick Access note slots
  - Source: https://www.noticky.app/en
- CONFIRMED — **Apple Shortcuts** support: create, append, read, link, open. Source: https://www.noticky.app/en
- **AppleScript: UNKNOWN / likely absent.** The vendor's own comparison table marks "Shortcuts/AppleScript" as a SideNotes advantage and a Noticky gap — notable because it is a self-admitted weakness, which raises its credibility. INFERRED absent.
- **URL scheme: UNKNOWN** (Shortcuts "link" action may imply note URLs).
- CONFIRMED — **MCP server / AI assistant integration** (marked NEW): "connects your favorite MCP-compatible AI assistant to securely search, create, update, and organize your Noticky notes" — search, read, create, update, archive, organize, with configurable permissions, setup from Settings, revocable at any time. Source: https://apps.apple.com/us/app/noticky/id6770181778 , https://www.noticky.app/en
  - **This is genuinely ahead of the category.** No SideNotes equivalent.
- CONFIRMED — **PopClip** extension. Source: https://www.noticky.app/en

## 2.12 Reception, strengths, weaknesses

**Ratings observed 2026-09-14:** MAS shows **insufficient ratings for an average** — the app is new and low-volume. No Reddit/HN/forum threads of substance surfaced. **Independent critical coverage is effectively nonexistent.** This is itself a finding: nearly all favorable Noticky comparisons are self-published on noticky.app.

**Strengths (CONFIRMED):**
- Fullscreen persistence — the one thing Apple Stickies and most competitors cannot do
- Fast global capture (`⌘⇧N`)
- App-aware contextual notes (unique in this pair)
- Screen-sharing privacy / capture exclusion
- Touch ID per-note lock
- Sticky Screenshot + click-through floating image overlays
- MCP/AI integration — first-mover in this category
- Price ($9.99, all future updates included)
- Small native binary (9.4 MB), no Electron

**User praise (CONFIRMED quotes):**
- "Definitely the best always on top app. Amazingly useful. So many features."
- "Exactly what my mac needed" — pinned reminders support focus; "they automatically hide themselves whenever I share my screen"
- "A Smart Tool for Organizing Study"
- Praised for keeping notes visible in fullscreen apps like Logic Pro

**Weaknesses (mix of CONFIRMED and INFERRED):**
| Weakness | Label | Note |
|---|---|---|
| **macOS 15 Sequoia minimum** — cuts off a large installed base | CONFIRMED | Hard requirement |
| **No folders** — tags only; poor fit for large structured libraries | INFERRED (vendor-admitted) | vendor comparison table |
| **No AppleScript** | INFERRED (vendor-admitted) | vendor comparison table |
| **No file attachments** beyond images | INFERRED (vendor-admitted) | vendor comparison table |
| **No custom themes** (skins are preset) | INFERRED (vendor-admitted) | vendor comparison table |
| **Mac and iPhone sold separately**; iOS free tier is heavily capped (1 wall / 8 notes) | CONFIRMED | noticky.app/en/ios |
| **iOS Pro caps notes at 64/wall** | CONFIRMED | noticky.app/en/ios |
| **Very little independent validation** — no rating average, no third-party reviews of substance | CONFIRMED (by absence) | MAS, search |
| **Solo developer** — bus-factor / longevity risk | INFERRED | MAS developer field |
| Multi-monitor behavior undocumented | UNKNOWN | — |

---

# 3. Head-to-head matrix

| Dimension | SideNotes | Noticky |
|---|---|---|
| **Core model** | ONE sidebar drawer at a screen edge | MANY individual floating windows |
| Platform | macOS 13+ | macOS 15+ |
| Mac price | $19.99, **paid major upgrades** | $9.99, **all updates free** |
| License | 1 user / 5 Macs | UNKNOWN (MAS/Setapp standard) |
| Setapp | Yes | Yes |
| Mobile | SideNotes Mobile, separate purchase, iOS 17+ | Noticky iOS, separate purchase, free tier capped, $4.99 Pro |
| Edge docking | Core model; left/right; Hot Side reveal; Open Bar with 3 auto-hide modes; resizable width | Secondary feature; per-note tab; left/right; reveal semantics UNKNOWN |
| Always-on-top individual notes | INFERRED no | **Yes — core feature** |
| Survives fullscreen | Yes (the panel) | Yes (each note) |
| **App/window binding** | **No** | **Yes — App-Aware Notes, per app, no focus steal** |
| Organization | **Folders** + pin + manual order + fold + colors | **Smart Tags** + colors + skins + templates + spatial layouts |
| Nesting | UNKNOWN | No |
| Tags | INFERRED no | Yes |
| Trash / archive | UNKNOWN | 30-day trash; archive INFERRED |
| Markdown | Yes, invisible-markup render; 3 modes (std/plain/code) | Yes, WYSIWYG live render |
| Checklists | Yes | Yes + progress + **Reminders sync** |
| Tables | UNKNOWN | UNKNOWN |
| Images | Yes | Yes |
| File/folder links + attachments | **Yes** | INFERRED no (images only) |
| Hex color swatches | **Yes** | UNKNOWN |
| Region screenshot capture | No built-in (INFERRED) | **Yes — Sticky Screenshot** |
| Floating image overlays w/ opacity + click-through + lock | No | **Yes** |
| Global capture hotkey | Yes (works while hidden) | Yes `⌘⇧N`, richer capture flow |
| Per-note keyboard recall | UNKNOWN | **Yes — 9 Quick Access slots** |
| Search | Yes + AppleScript search + Alfred; launch URLs from results | "Instant search" over Smart Tags |
| Sync | iCloud, Mac+iPhone+iPad, auto backups | iCloud/CloudKit across Macs, claimed E2EE |
| Account required | No | No |
| Telemetry | None claimed | None claimed |
| Per-note lock | **No** | **Yes — Touch ID** |
| Hide from screen capture/sharing | UNKNOWN | **Yes** |
| Apple Shortcuts | Yes | Yes |
| AppleScript | **Yes** | INFERRED no |
| URL scheme | **Yes — note & folder URLs** | UNKNOWN |
| MCP / AI integration | No | **Yes** |
| Third-party integrations | Alfred, Hookmark, PopClip, Dropzone, Raycast | PopClip |
| Export | Notes to images; Share Extension | PDF / MD / TXT |
| Themes | Custom + pre-made | Preset skins only |
| Multi-monitor | UNKNOWN | UNKNOWN |
| Maturity | ~2018/2019 onward; 4.6/5 (48 MAS) + 97% (1,470 Setapp) | New; insufficient MAS ratings |
| Binary size | 15 MB | 9.4 MB |

---

# 4. Implications for Noto (Windows)

Framing only — not researched claims.

1. **The two products occupy opposite poles of the same problem.** SideNotes = one persistent edge drawer optimized for *retrieval* of an organized library. Noticky = many floating windows optimized for *keeping context visible*. Noto must pick a pole or deliberately ship both modes. Shipping both is the differentiated position, and neither incumbent does it.

2. **Nobody in this pair serves Windows.** There is no Mac→Windows migration path from either. Noto's competition on Windows is Notezilla, Stickies, Microsoft Sticky Notes — a much weaker field.

3. **Contextual app binding is the highest-signal feature.** Noticky proves demand; SideNotes lacks it entirely. On Windows the equivalent is binding a note to a process/window class/title, which is technically tractable via `SetWinEventHook` / foreground-window events.

4. **Both are missing tables, and both are vague on multi-monitor.** Two cheap wins.

5. **Organization axis is an either/or in both products** — SideNotes has folders and no tags; Noticky has tags and no folders. Shipping both, with folders for structure and tags for cross-cutting, is a clean advantage.

6. **Automation is SideNotes' moat and Noticky's admitted gap.** On Windows the analogue is a CLI, a URL scheme, and a local HTTP/MCP endpoint. Noticky's MCP server shows where the category is heading — worth matching early.

7. **Screen-capture exclusion (`WDA_EXCLUDEFROMCAPTURE` on Windows) is a real, cheap, differentiated feature** that Noticky validates demand for, with corroborating user praise.

8. **Pricing anchor:** $9.99–$19.99 one-time is the category norm. Paid major upgrades (SideNotes) draw structural criticism; "all future updates included" (Noticky) is used as a selling point.

9. **The Open Bar complaint is the clearest UX lesson in this research.** SideNotes users repeatedly asked for keyboard-only activation and for the edge gesture to be disableable. Noto should make every activation surface — edge strip, hover reveal, gesture, tray icon, hotkey — independently toggleable from day one.

---

# 5. Source index

**SideNotes (official)**
- https://www.apptorium.com/sidenotes
- https://www.apptorium.com/sidenotes/features
- https://www.apptorium.com/sidenotes/buy
- https://www.apptorium.com/sidenotes/tips
- https://www.apptorium.com/sidenotes/tips/how-to-hide-open-bar
- https://www.apptorium.com/

**SideNotes (third-party)**
- https://apps.apple.com/us/app/sidenotes/id1441958036
- https://apps.apple.com/us/app/sidenotes-screen-edge-notes/id1441958036?see-all=reviews
- https://setapp.com/apps/sidenotes

**Noticky (official — vendor, treat comparisons as biased)**
- https://www.noticky.app/en
- https://www.noticky.app/en/ios
- https://www.noticky.app/en/blog/what-is-noticky
- https://www.noticky.app/en/blog/best-sticky-note-apps-mac (2026-05-16)
- https://www.noticky.app/en/blog/noticky-vs-sidenotes-mac
- https://www.noticky.app/en/alternative/sidenotes

**Noticky (third-party)**
- https://apps.apple.com/us/app/noticky/id6770181778
- https://macappsdaily.com/apps/noticky-app (directory listing, not a review)

**Rejected as unreliable**
- `github.com/SideNotes-OSX/SideNotes-Mac` — not linked from apptorium.com; appears to be an unofficial mirror or SEO listing. Not used.
