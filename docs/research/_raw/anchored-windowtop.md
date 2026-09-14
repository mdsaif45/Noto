# Competitor research — @/Anchored and WindowTop

Research date: 2026-09-14
For: Noto (Windows notes app)

Label key: **CONFIRMED** = stated on a cited source. **INFERRED** = reasoned from confirmed facts, not stated. **UNKNOWN** = could not verify.

> Caveat on source quality: nearly every CONFIRMED claim about @/Anchored comes from **the vendor's own marketing site**. There is no independent review, no Reddit/HN/Product Hunt thread, no AlternativeTo page, no press coverage found. Treat Anchored's capability claims as *marketing claims*, not verified behavior. WindowTop, by contrast, has a public GitHub issue tracker and years of third-party coverage.

---

## 1. @/Anchored (atanchored.com)

### 1.1 Identity and platform

| Item | Value | Label | Source |
|---|---|---|---|
| Product name | "@/Anchored" (stylized) | CONFIRMED | https://atanchored.com/ |
| Canonical domain | atanchored.com; contact `hello@at-anchored.com` | CONFIRMED | https://atanchored.com/ |
| Platform | **Windows 10 and 11 only** | CONFIRMED | https://atanchored.com/ |
| macOS / Linux / mobile | Not offered, not mentioned on roadmap | CONFIRMED (absence) | https://atanchored.com/ |
| App type | Native Windows app ("Native Windows app, spring animations, real typography") | CONFIRMED | https://atanchored.com/ |
| Installer | `updates/Anchored_0.11.19_x64-setup.exe` — x64 only | CONFIRMED | https://atanchored.com/ |
| Version at time of research | 0.11.19 — **pre-1.0, early access** | CONFIRMED | https://atanchored.com/ |
| ARM64 support | — | UNKNOWN |
| Memory footprint | "~50 MB RAM" claimed | CONFIRMED (vendor claim) | https://atanchored.com/ |
| Tech stack | Installer naming (`Anchored_0.11.19_x64-setup.exe`) is the Tauri/NSIS convention; "spring animations, real typography" suggests a web-render UI layer. Not stated anywhere. | INFERRED (low confidence) |

### 1.2 Pricing and licensing

| Item | Value | Label | Source |
|---|---|---|---|
| Current price | **Free** during early access, all features | CONFIRMED | https://atanchored.com/ |
| Account required | **No account required** | CONFIRMED | https://atanchored.com/ |
| Future model | Anchoring system stays free; paid **"Atlas" tier** for AI features | CONFIRMED | https://atanchored.com/ |
| Donation perk | "Lifetime premium key" to anyone who donates any amount during early access | CONFIRMED | https://atanchored.com/ |
| Actual future price point | — | UNKNOWN |
| IP posture | "**Patent Pending**" stated on site | CONFIRMED | https://atanchored.com/ |
| Open source | No repo found; no license named | UNKNOWN (assume proprietary) |

> ⚠️ **Patent Pending on the anchoring concept is the single most important commercial finding for Noto.** The claim is unverified (no application number published, and "patent pending" is self-asserted and unexamined), but it signals intent to assert. Scope is UNKNOWN. Worth a real prior-art/FTO check before Noto ships a headline "note follows the window" feature — see §4.

### 1.3 What "anchoring" actually means

This is the core question. The answer from all vendor sources is consistent:

**An anchor binds a note to a specific live OS window — not to an application, not to a URL, not to a document, not to a file path, not to a project folder.**

Verbatim claims:

- "Anchor a note to any window. It moves when the window moves, hides when it hides, comes back when it returns." — CONFIRMED, https://atanchored.com/
- "Identifies that *specific* window — not just a spot on your screen" — CONFIRMED, https://atanchored.com/blog/why-sticky-notes-dont-follow-windows
- "Tracks the window's position, size, and state in real time" — CONFIRMED, same
- "Works with everything: Code editors, browsers, spreadsheets, games — if it has a window" — CONFIRMED, https://atanchored.com/

The granularity ladder, and where Anchored sits:

```
COARSE                                                              FINE
  |                                                                   |
  app ----------- window ----------- document/tab/URL ----------- selection
                    ^
                    |
              @/Anchored is HERE
              (one live OS window)

  AnchorNotes (mac)   Anchored          Anchored Notes (Chrome ext)
  = app-level         = window-level    = URL/site/tab-level
```

Explicitly **not** supported, as far as can be confirmed:

| Binding target | Supported? | Label |
|---|---|---|
| A specific OS window | Yes | CONFIRMED |
| An application (all its windows) | Not claimed anywhere | INFERRED not supported |
| A browser tab or URL | Not claimed anywhere | INFERRED not supported |
| A document or file path | Not claimed anywhere | INFERRED not supported |
| A project / folder | Not claimed anywhere | INFERRED not supported |
| A code symbol, line, or region | Not claimed anywhere | INFERRED not supported |

> This is the **single biggest gap**. A browser with 40 tabs is one window. A VS Code instance with 12 files open is one window. Anchored gives you one anchor point for all of it — six note slots, but all bound to the same window, with no notion of *which tab* or *which file* is in front. Anchored's own Atlas AI feature exists partly to paper over this: it reads the window's pixel/text content to figure out what you're actually looking at, because the anchor itself doesn't know.

### 1.4 Does the note follow the window?

**Yes — claimed as live, continuous following, not just show/hide.** This is Anchored's headline differentiator.

| Behavior | Claim | Label | Source |
|---|---|---|---|
| Window moves → note moves | Yes, "Follows live as you drag & resize" | CONFIRMED (vendor claim) | https://atanchored.com/ compare table |
| Window resizes → note repositions | Yes | CONFIRMED (vendor claim) | https://atanchored.com/ |
| Real-time vs. on-drop | "the only app here that does it in real time" | CONFIRMED (vendor claim) | https://atanchored.com/compare |
| Docking model | Notes dock to the **window's edges — six slots** | CONFIRMED | https://atanchored.com/ |
| Max notes per window | **6** | CONFIRMED | https://atanchored.com/ |
| Free-floating (unanchored) notes | Also supported | CONFIRMED | https://atanchored.com/ |
| Anchor UI modes | "Click-to-anchor, bracket and slide-tab modes" | CONFIRMED (terms only; behavior not explained) | https://atanchored.com/ |
| Anchor picker | "Pick a window from the command center, and the note locks to it" | CONFIRMED | https://atanchored.com/blog/keep-notes-above-other-windows-windows-11 |

The site's own taxonomy (useful framing for Noto):

```
screen-pinned  -> note sits at a fixed screen spot, ignores everything
                  (Windows Sticky Notes, Zhorn Stickies)
z-pinned       -> note stays above other windows, follows nothing
                  (WindowTop + any notes app)
window-anchored-> note bound to one window; moves/hides/returns with it
                  (@/Anchored)
```
CONFIRMED, https://atanchored.com/blog/keep-notes-above-other-windows-windows-11

### 1.5 How does it detect context?

**UNKNOWN — and deliberately so.** This is the most conspicuous hole in all vendor material.

- The site says it "identifies that *specific* window" but **never states the mechanism**. CONFIRMED absence across homepage, /compare, and all three blog posts.
- Whether it uses HWND, process name/PID, window class, window title, UI Automation, or a composite fingerprint: **UNKNOWN**.
- No mention of URL detection, file-path detection, or document-identity detection anywhere: CONFIRMED absence.

What can be inferred:

- Live position/size tracking on Windows is done either by polling `GetWindowRect` on a timer or by a `SetWinEventHook` on `EVENT_OBJECT_LOCATIONCHANGE`. The "real time, follows live as you drag" claim points to the WinEvent hook path (polling visibly lags during drag). **INFERRED**, not stated.
- Re-anchoring after a restart cannot use HWND — handles are not stable across process restarts. It must persist some durable identifier: process/executable name, window class, window title, or a combination. **INFERRED**, not stated.
- Title-based matching is the most likely durable key given "reopen the window next week, the note is there" — but titles are volatile (browsers and editors rewrite the title per tab/file/dirty-state). Whatever they do here almost certainly has failure modes they don't advertise. **INFERRED**.

> For Noto: the mechanism Anchored won't describe *is* the hard engineering problem. Durable window identity across restarts is the real moat, not the visual following.

### 1.6 Window state changes

| Event | Claimed behavior | Label | Source |
|---|---|---|---|
| Minimize | "Hides the note when the window is minimized or closed" | CONFIRMED (vendor claim) | https://atanchored.com/blog/why-sticky-notes-dont-follow-windows |
| Close | Note hides | CONFIRMED (vendor claim) | same |
| Window returns | "Brings the note back and re-anchors it when the window returns" | CONFIRMED (vendor claim) | same |
| App restart | "Remembers the relationship across restarts — close the app, reopen the window next week, the note is there" | CONFIRMED (vendor claim) | same |
| Anchored itself restarts | "Persistent memory across restarts" | CONFIRMED (vendor claim) | https://atanchored.com/ |
| Monitor change | "Follows the window across monitors when you drag it to another screen" | CONFIRMED (vendor claim) | same |
| **Maximize** | Never mentioned. Presumably the note re-docks to the maximized edges, but unstated. | **UNKNOWN** |
| **Snap / Aero Snap / FancyZones** | Never mentioned | **UNKNOWN** |
| **Virtual desktop switch** | Never mentioned anywhere in any source | **UNKNOWN** |
| **Occlusion (window behind another window)** | Never mentioned — does the note stay on top, or z-order with its host? | **UNKNOWN** |
| **Fullscreen / exclusive-fullscreen games** | "games" listed as supported, but fullscreen behavior unstated | **UNKNOWN** |
| **Window on a monitor that is disconnected** | Never mentioned | **UNKNOWN** |

> The UNKNOWN column here is the practical test plan for evaluating Anchored hands-on, and the gap list for Noto. Virtual desktops and occlusion/z-order are the two most likely places a window-anchored notes app falls apart, and Anchored is silent on both.

### 1.7 Editor, organization, search, capture

| Capability | Status | Label | Source |
|---|---|---|---|
| Auto-save | "Every keystroke saved instantly" | CONFIRMED | https://atanchored.com/ |
| Themes | Light/dark, **six color palettes** | CONFIRMED | https://atanchored.com/ |
| **Markdown** | Anchored's own /compare table has a "Markdown support" row — but the row's value for Anchored was not resolvable from the fetched content | **UNKNOWN** |
| **Rich text** | Never mentioned | **UNKNOWN** (likely plain text given the sticky-note framing) |
| **Search** | Never mentioned on any page | **UNKNOWN — probable gap** |
| **Tags** | Never mentioned | **UNKNOWN — probable gap** |
| **Folders / notebooks** | Never mentioned | **UNKNOWN — probable gap** |
| **Note list / global index** | Never mentioned; there is a "command center" but it is described only as a *window picker* | **UNKNOWN — probable gap** |
| **Export** | Never mentioned | **UNKNOWN** |
| **Images / attachments** | Never mentioned | **UNKNOWN** |
| **Checklists / todos** | Never mentioned | **UNKNOWN** |
| **Web/screenshot capture** | Never mentioned | **UNKNOWN** |
| **Backlinks / note linking** | Never mentioned | **UNKNOWN** |

> The product is effectively **anchoring + a plain sticky note + an AI**. Everything a notes app normally has — search, tags, organization, export — is either absent or undocumented. For a user with 200 notes across 80 windows, there is no confirmed way to find anything. Anchored is an *anchoring engine*, not yet a notes app.

### 1.8 Sync and data

| Item | Value | Label | Source |
|---|---|---|---|
| Sync today | **None.** Local-first. | CONFIRMED | https://atanchored.com/ |
| Sync planned | Phase 5 (Planned): "Optional sync through your own cloud drive. Share notes between @/Anchored users." | CONFIRMED | https://atanchored.com/ |
| Sync model | BYO cloud drive (Dropbox/OneDrive-style file sync), not a vendor backend | CONFIRMED (as planned) | https://atanchored.com/ |
| Works offline | Yes | CONFIRMED | https://atanchored.com/ |
| On-disk data location / format | Never stated | **UNKNOWN** |
| Encryption at rest | Never mentioned | **UNKNOWN** |

### 1.9 Atlas (the AI layer)

| Item | Value | Label | Source |
|---|---|---|---|
| What it does | "Reads the anchored window" and answers questions about it | CONFIRMED | https://atanchored.com/ |
| Example claim | "if your note is on a quarterly report, Atlas reads the quarterly report" | CONFIRMED | https://atanchored.com/compare |
| Locality | Runs locally/offline; "Private, on-device AI" | CONFIRMED (vendor claim) | https://atanchored.com/ |
| Model backends | **Any OpenAI-compatible local server** — LM Studio, Ollama, llama.cpp. No API key required. Cloud optional. | CONFIRMED | https://atanchored.com/ |
| Streaming | "streaming answers" | CONFIRMED | https://atanchored.com/ |
| Proactive mode | "Suggest" mode — proactive suggestions, problem detection while working | CONFIRMED | https://atanchored.com/ |
| Write-back | Can draft text or fix content **in the anchored window**, user approval required | CONFIRMED | https://atanchored.com/ |
| Window control | "Can operate window controls (scoped to that window only)" | CONFIRMED | https://atanchored.com/ |
| Permission model | Per-capability: **Off / Ask / Allow** | CONFIRMED | https://atanchored.com/ |
| **How it reads window content** | OCR? UI Automation? Accessibility tree? Screen capture? **Never stated.** | **UNKNOWN** |
| MCP | Phase 6 shipped (v0.11.9): "Connectors: Claude or MCP clients read notes" — "Claude — or any MCP client — can read your notes and leave one anchored to the window it's about" | CONFIRMED | https://atanchored.com/ |

> Atlas is the strategic tell. Because the anchor is window-level and therefore context-blind, Anchored bolts an AI on top to *infer* the context the anchor can't represent. It's a workaround for the coarse binding, sold as a feature. Also note: reading arbitrary window contents is a significant privacy/permission surface, and the mechanism is undisclosed.

### 1.10 Privacy

- "Private by default · Atlas runs locally · Your window content and notes never leave your machine unless you deliberately choose a cloud model" — CONFIRMED (vendor claim), https://atanchored.com/
- No account required — CONFIRMED.
- No privacy policy page found on the site — CONFIRMED absence.
- Telemetry/analytics: **UNKNOWN**, never addressed.
- Auto-update: installer path is under `/updates/`, implying a self-hosted updater. **INFERRED**.

### 1.11 Multi-monitor, DPI, shortcuts, Windows support

| Item | Value | Label | Source |
|---|---|---|---|
| Multi-monitor | Supported; notes follow across screens | CONFIRMED (vendor claim) | https://atanchored.com/ |
| Mixed-DPI monitors | **Never mentioned** | **UNKNOWN** — likely the hardest correctness problem for edge-docked notes |
| Per-monitor DPI awareness | **Never mentioned** | **UNKNOWN** |
| Keyboard shortcut | **`Ctrl+Alt+A`** — opens/activates (the command center) | CONFIRMED | https://atanchored.com/blog/keep-notes-above-other-windows-windows-11 |
| Other shortcuts / remapping | **Never documented** | **UNKNOWN** |
| Windows versions | 10 and 11 | CONFIRMED | https://atanchored.com/ |
| Accessibility | Never mentioned | **UNKNOWN** |

### 1.12 Roadmap (verbatim status)

| Phase | Name | Status | Content |
|---|---|---|---|
| 1 | Foundation | Shipped | Window anchoring, multi-monitor, persistent memory |
| 2 | Production | Shipped | Native Windows app, animations, typography, themes |
| 3 | Intelligence | Shipped (v0.11) | Atlas AI — reads windows, local/cloud, suggestions, edits |
| 4 | Open | Shipped (v0.11.4) | Point Atlas at any OpenAI-compatible local server |
| 5 | **Sync** | **Planned** | Cloud drive sync, offline-first sharing |
| 6 | Open, part two | Shipped (v0.11.9) | Connectors: Claude / MCP clients read notes |

CONFIRMED, https://atanchored.com/

> Note the ordering: they shipped *two* AI/interop phases before sync, and search/organization isn't a phase at all. Priorities are anchoring + AI, not notes management.

### 1.13 How Anchored positions itself

Its own comparison sets (both CONFIRMED, https://atanchored.com/ and https://atanchored.com/compare):

Homepage table vs: Windows Sticky Notes, Zhorn Stickies, Notezilla.
/compare page vs: Microsoft Sticky Notes, Notezilla, **WindowTop**, Zhorn Stickies, UpNote.

Rows used (homepage): attaches note to a window · follows live as you drag & resize · hides & returns with the window · remembers its window after a restart · follows across monitors · notes dock to window edges (six slots) · AI that reads the window · private on-device AI · works offline · no account required · platforms · price.

Competitor prices as stated by Anchored (CONFIRMED as *their* claims, https://atanchored.com/compare):
- Microsoft Sticky Notes — Free (built-in)
- Notezilla — $20 one-time
- WindowTop — Free / $10 lifetime ← **Anchored's number is wrong; actual is $19, see §2.2**
- Zhorn Stickies — Free
- UpNote — Free (50 notes) / $1.99 per month
- Anchored — Free (early access)

### 1.14 Reviews, complaints, limitations

**There are none. This is a finding in itself.**

- No AlternativeTo listing found — CONFIRMED absence.
- No Reddit, Hacker News, or Product Hunt thread found — CONFIRMED absence.
- No press, blog, or YouTube coverage found — CONFIRMED absence.
- No public issue tracker or repo found — CONFIRMED absence.
- No user reviews anywhere — CONFIRMED absence.

Searches that returned only unrelated products: `atanchored.com`, `at-anchored.com`, `"@/Anchored"`, `"anchor a note to any window"`. The name collides badly with at least five unrelated products — **Anchored Notes** (Chrome ext, URL/site/page/tab-scoped, 4.8 stars), **Anchored** (anchored.site, web highlights), **AnchorNotes** (macOS, *app*-scoped contextual notes), **Anchor** (open-source self-hosted Keep clone), **TackNote** (macOS, pins notes to window/tab/document/folder/chat/task), and **Anchor Dock** (Windows 11 launcher).

> Conclusions: (a) Anchored has effectively **zero market presence and zero validation** — a pre-1.0 solo/small project. (b) Every capability claim above is unverified vendor copy. (c) The name is a **severe SEO and discovery liability** — worth noting if Noto ever considers similar naming. (d) The adjacent-product list shows the *concept* has been implemented repeatedly on macOS and in the browser; the Windows window-anchored niche is where it's thin. (e) The "Patent Pending" claim sits alongside substantial visible prior art (TackNote, AnchorNotes), which is relevant to §4.

---

## 2. WindowTop (windowtop.info)

Relevant to Noto not as a competitor but as **proof of what is feasible with Windows-native window manipulation from an external process** — and as a catalogue of the bugs you'll hit doing it.

### 2.1 What it is

A Windows window-management utility that manipulates *other applications'* windows from outside. CONFIRMED, https://windowtop.info/

Feature set (CONFIRMED, https://windowtop.info/ and https://windowtop.info/faq-frequently-asked-questions/):

| Feature | Description | Tier |
|---|---|---|
| Always on Top | Force any window to stay above others, with customizable colored border (adjustable frame color + thickness) | Free |
| Opacity / transparency | Per-window opacity control | Free |
| **Click-through** | Interact with content *behind* a transparent window | Free |
| Hotkeys | Global hotkeys | Free |
| Basic dark mode | Invert/darken any app's window | Free |
| Window shrinking | Scale a window down (very limited in free) | Free (limited) |
| Picture-in-Picture (PiP) | Floating, still-interactive miniature of any window | **Pro** |
| Crop | Crop a PiP to a region, with automatic element detection (v5.29+) | **Pro** |
| Smart dark mode | Dark mode with image filtering (doesn't invert photos) | **Pro** |
| Glass mode | Adjustable blur, separate opacity for background / text / images; NVIDIA CUDA optimization | **Pro** |
| Anchors | Single-click instant window access, faster than ALT+TAB, touchscreen compatible; auto-repositions to avoid covering text/images | **Pro** (anchor window picker) |
| Save window configurations | Persist per-window settings | **Pro** |
| Whitelist / blacklist programs | Per-app rules | **Pro** |

> Terminology warning: **WindowTop's "Anchors" ≠ Anchored's "anchoring."** WindowTop anchors are edge-of-screen quick-access tabs for *switching to* a window. Anchored's anchors bind *a note to* a window. Different concepts, same word.

### 2.2 Pricing (correcting Anchored's claim)

| Item | Value | Label | Source |
|---|---|---|---|
| Pro price | **$19.00 one-time** (Anchored's site says "$10 lifetime" — **wrong**) | CONFIRMED | https://windowtop.info/purchase/ |
| Seats | **3 computers per user** per key | CONFIRMED | https://windowtop.info/purchase/ |
| Lifetime? | Yes — permanent for all 5.X.X, "no plans to develop 6.X.X versions" | CONFIRMED | https://windowtop.info/faq-frequently-asked-questions/ |
| Trial | 30-day full-feature trial | CONFIRMED (third-party) | https://windowtop.en.softonic.com/ |
| Free tier restriction | **Personal, non-commercial use only** | CONFIRMED | https://windowtop.info/faq-frequently-asked-questions/ |
| Multi-user | Separate subscription license; updates for 1 year after last payment | CONFIRMED | https://windowtop.info/windowtop-multi-user-license/ |
| Other channels | Microsoft Store, Lizhi.io (China), APSGO (APAC) | CONFIRMED | https://windowtop.info/purchase/ |
| Key recovery | Vendor explicitly disclaims indefinite retention of purchase records — "users accept responsibility for not losing their key" | CONFIRMED | https://windowtop.info/purchase/ |

Note there is a $5.99 figure circulating in older third-party coverage; the vendor's own purchase page says $19.00. Treat $19 as current.

### 2.3 Feasibility signals for Noto

What WindowTop's existence **proves is achievable** from an external Windows process against arbitrary third-party windows:

```
external process
      |
      +-- force z-order (always-on-top)            -> CONFIRMED feasible, free tier
      +-- per-window opacity                       -> CONFIRMED feasible, free tier
      +-- click-through (input passthrough)        -> CONFIRMED feasible, free tier
      +-- draw a border/frame around someone       -> CONFIRMED feasible, free tier
      |     else's window (color + thickness)
      +-- live mirror a window (PiP), interactive  -> CONFIRMED feasible, Pro
      +-- crop that mirror to a sub-region,        -> CONFIRMED feasible, Pro
      |     with automatic element detection
      +-- recolor/invert another app's rendering   -> CONFIRMED feasible, but FRAGILE (§2.4)
      +-- blur/composite behind a window           -> CONFIRMED feasible, GPU-dependent
      +-- scale/shrink a foreign window            -> CONFIRMED feasible
      +-- persist per-window settings across runs  -> CONFIRMED feasible ("save window configurations")
      +-- per-app whitelist/blacklist              -> CONFIRMED feasible
      +-- global hotkeys                           -> CONFIRMED feasible
```

Implementation techniques: **UNKNOWN**. WindowTop is closed-source (the GitHub org hosts only an issue tracker and releases, not source). The APIs behind always-on-top (`SetWindowPos` + `HWND_TOPMOST`), opacity (`SetLayeredWindowAttributes`), and click-through (`WS_EX_TRANSPARENT`) are standard and well known, but that WindowTop uses them specifically is INFERRED, not confirmed.

Notably, WindowTop has **no** live position-following of a target window (it doesn't need it) — so it does **not** demonstrate feasibility of Anchored's core trick. That remains the unproven-by-third-party part. INFERRED.

### 2.4 Known issues and limitations (the valuable part)

From the public tracker — 126 issues total. CONFIRMED, https://github.com/WindowTop/WindowTop-App/issues

| Theme | Evidence | Label |
|---|---|---|
| **High-DPI / scaling breakage** | #499 dark mode "not aligned properly when using system scaling on a high DPI monitor" | CONFIRMED |
| **Mixed-DPI multi-monitor breakage** | #293 "Wrong windows position with dark mode activated with several display device" — windows become larger and misaligned. Release notes show fixes for PiP taskbar preview on a secondary monitor with different DPI scaling, and for a bug where different DPI scaling temporarily changed a window's DPI. | CONFIRMED |
| **Stability** | #500 "Frequent crashes" on 5.32.6 and older | CONFIRMED |
| **Per-app incompatibility** | #494 dark mode fails on WinCVS; #99 dark/glass mode breaks apps that show popups on keypress and AutoHotkey functions | CONFIRMED |
| **Timing / startup races** | #86 "fails to enable dark-mode when it start by Windows"; #482 dark mode switching takes seconds to apply | CONFIRMED |
| **Window positioning corruption** | #485 "Window Stuck Underneath Desktop" | CONFIRMED |
| **Installer conflicts** | #490 Microsoft Store vs installer detection conflict | CONFIRMED |
| **Opacity defaults** | #497 default opacity level bug | CONFIRMED |
| **GPU dependence** | Glass mode "works better on NVIDIA GPUs," "may work slowly on 4K" | CONFIRMED, https://windowtop.info/ |
| **OS-version dependence** | Dark mode: "Stable" on Windows 11, "May be unstable" on Windows 10 | CONFIRMED, https://windowtop.info/ |

> **Lesson for Noto, stated plainly: mixed-DPI multi-monitor and high-DPI scaling is where external window manipulation reliably breaks.** It is the #1 recurring theme across a mature, actively maintained product's issue tracker. Anchored says nothing about DPI at all — which, given it does *continuous edge-docking* against foreign windows (strictly harder than WindowTop's one-shot operations), is either an undiscovered problem or an undisclosed one.

### 2.5 WindowTop: multi-monitor, DPI, shortcuts, privacy, OS support

| Item | Value | Label | Source |
|---|---|---|---|
| Windows versions | **Windows 7, 8, 10, 11 — 64-bit only**. Dark mode and glass mode require Windows 10 minimum. | CONFIRMED | https://windowtop.info/faq-frequently-asked-questions/ |
| Multi-monitor | Works, but with documented mixed-DPI defects (§2.4). No formal support statement on the site. | CONFIRMED (defects) / UNKNOWN (official stance) |
| DPI | No official statement. Known defects. | UNKNOWN / CONFIRMED defects |
| Keyboard shortcuts | Hotkeys exist and are a free-tier feature. **Specific default bindings not documented on the site.** | CONFIRMED (existence) / UNKNOWN (specifics) |
| Privacy policy | Not surfaced in the FAQ | UNKNOWN |
| Telemetry | Never addressed | UNKNOWN |
| Distribution | Installer, Microsoft Store, or **portable** version; auto or manual updates | CONFIRMED | https://windowtop.info/faq-frequently-asked-questions/ |
| Support channels | Reddit community (preferred for feature requests) + contact form | CONFIRMED | https://windowtop.info/faq-frequently-asked-questions/ |

---

## 3. Side-by-side

| Dimension | @/Anchored | WindowTop |
|---|---|---|
| Category | Notes app with window binding | Window utility |
| Platform | Win 10/11, x64 | Win 7/8/10/11, x64 |
| Maturity | v0.11.19, pre-1.0, early access | v5.3x, years of releases |
| Price | Free (early access); AI tier later | Free tier / $19 lifetime Pro, 3 seats |
| Independent validation | **None found** | Extensive — public tracker, Reddit, Softpedia, MS Store |
| Source of all claims | Vendor marketing only | Vendor + public issue tracker |
| Follows a window live | Claimed yes | No (doesn't attempt it) |
| Manipulates foreign windows | Reads content (Atlas); docks notes to edges | Yes, extensively |
| Always-on-top | Implicit; not described as a feature | Core feature |
| Opacity / click-through | Not mentioned | Yes, both, free tier |
| AI | Atlas, local-first, MCP | None |
| Sync | None (planned, BYO drive) | N/A |
| Search / tags / organization | Not documented | N/A |
| DPI handling | Not mentioned | Known defects, actively patched |
| Documented shortcuts | `Ctrl+Alt+A` only | Hotkeys exist, undocumented |

---

## 4. Gaps Anchored leaves (Noto opportunity surface)

Ordered roughly by strategic weight.

1. **Window-level binding is too coarse.** One browser = one anchor, regardless of 40 tabs. One editor = one anchor, regardless of 12 files. Anchored has no confirmed concept of tab, URL, document, file path, or project. A finer binding — *note follows the file/URL/tab/project, surfaced in whatever window currently shows it* — is a genuinely different and more useful product, not a feature gap to be patched. INFERRED from CONFIRMED absence of any such claim.

2. **It is not a notes app yet.** No confirmed search, tags, folders, note list, export, backlinks, images, or checklists. At scale the user cannot find their own notes. Roadmap doesn't schedule any of it. CONFIRMED absence.

3. **No sync.** Local-only today; planned as BYO-cloud-drive file sync, which historically conflicts badly on multi-device edits. CONFIRMED.

4. **Six notes per window, hard cap.** CONFIRMED. An arbitrary structural ceiling.

5. **Silent on the hard cases.** Virtual desktops, maximize, Aero Snap/FancyZones, occlusion/z-order versus the host window, disconnected monitors, mixed DPI — **all UNKNOWN, none addressed anywhere**. WindowTop's tracker proves mixed-DPI multi-monitor is exactly where this class of software breaks. Either Anchored hasn't solved these or hasn't hit them. Verifying them hands-on is the highest-value next research step and likely the clearest quality differentiator for Noto.

6. **Undisclosed identity mechanism = undisclosed failure modes.** Re-anchoring across restarts cannot use HWND; whatever durable key they use (title? exe? class?) has predictable breakages — renamed files, changed tab titles, multiple instances of the same app, portable vs installed binaries. INFERRED.

7. **AI as a substitute for structure.** Atlas reads the window to infer what the anchor can't express. Noto can get comparable context *deterministically and for free* by binding to the right entity in the first place — no model, no latency, no privacy surface, no GPU.

8. **Zero market presence.** No reviews, no community, no coverage, no repo. Low switching costs for users; no incumbent moat. Also means no validated demand — the niche is unproven, not just unserved. CONFIRMED absence.

9. **Name is a liability.** Collides with ≥5 unrelated products. Undiscoverable by search. CONFIRMED.

10. **"Patent Pending" is the one real risk to Noto.** Self-asserted, scope UNKNOWN, no published application found. Substantial visible prior art exists (TackNote on macOS pins notes to window/tab/document/folder/chat/task; AnchorNotes on macOS binds notes to apps; Anchored Notes Chrome extension scopes notes global/site/page/tab). Do not let this block product thinking, but get a prior-art/FTO opinion before making live window-following the headline claim.

### Cross-platform note

Window/app/document-scoped contextual notes are already **solved on macOS** (TackNote, AnchorNotes) and **in the browser** (Anchored Notes, anchored.site). Windows is the thin market. Those products are also the best sources of both design prior art and the prior art relevant to point 10. CONFIRMED via search results (see §1.14 sources).

---

## 5. Verification checklist (unresolved — requires hands-on testing)

Install `Anchored_0.11.19_x64-setup.exe` in a VM and test:

- [ ] Anchor identity mechanism — rename the target window's document/title; does the anchor survive?
- [ ] Two instances of the same app open — does the note pick the right one?
- [ ] Restart the target app — does re-anchor actually work, and how fast?
- [ ] **Virtual desktop switch** — does the note follow, hide, or orphan?
- [ ] **Maximize / restore / Aero Snap**
- [ ] **Occlusion** — host window sent behind another; does the note stay on top or go with it?
- [ ] **Mixed-DPI multi-monitor** — drag host between a 100% and a 150% display
- [ ] Monitor disconnect with host on it
- [ ] Fullscreen game / exclusive fullscreen
- [ ] Does a search function exist anywhere in the UI?
- [ ] Markdown support yes/no
- [ ] On-disk data location and format
- [ ] Outbound network traffic with Atlas off (telemetry check)
- [ ] How Atlas reads window content (OCR vs UI Automation vs capture) — check for accessibility/capture permission prompts
- [ ] Full hotkey list beyond `Ctrl+Alt+A`
- [ ] CPU cost during sustained window drag (reveals hook vs. polling)

---

## Sources

- https://atanchored.com/
- https://atanchored.com/compare
- https://atanchored.com/blog/
- https://atanchored.com/blog/why-sticky-notes-dont-follow-windows
- https://atanchored.com/blog/anchored-vs-windowtop
- https://atanchored.com/blog/keep-notes-above-other-windows-windows-11
- https://windowtop.info/
- https://windowtop.info/purchase/
- https://windowtop.info/faq-frequently-asked-questions/
- https://windowtop.info/windowtop-multi-user-license/
- https://github.com/WindowTop/WindowTop-App/issues
- https://github.com/WindowTop/WindowTop-App/issues/86
- https://github.com/WindowTop/WindowTop-App/issues/293
- https://github.com/WindowTop/WindowTop-App/releases
- https://windowtop.en.softonic.com/
- https://tacknote.app/ (adjacent prior art, macOS)
- https://anchornotes.to/ (adjacent prior art, macOS)
- https://chromewebstore.google.com/detail/anchored-notes-sticky-not/dnmmgfkolmlieeempmfjghddbcehijgc (adjacent prior art, browser)
- https://anchored.site/ (name collision)
