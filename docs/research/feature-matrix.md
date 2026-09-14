# Feature Decision Matrix

**Research date:** 2026-09-14
**Status:** Master decision record for the initial product scope
**Derived from:** [`competitive-analysis.md`](competitive-analysis.md) and [`_raw/`](_raw/)
**Constrained by:** [`../product/principles.md`](../product/principles.md), [`../product/mvp.md`](../product/mvp.md)

---

## How to read this document

One row per feature. Competitor columns record **what the research found**, not
what a product plausibly has. The Noto column records a decision, and every
decision is traceable to a principle, an ADR, or the MVP scope document.

Cell values:

| Value | Meaning |
| ----- | ------- |
| ✅ | CONFIRMED present — vendor site, docs, or store listing |
| ❌ | CONFIRMED or strongly-INFERRED absent |
| ? | UNKNOWN — could not verify. **Not** a gap and **not** a feature. |
| ~ | Partial / qualified — the qualifier says how |
| *text* | Short qualifier where the nuance matters (`title-match`, `OneNote only`) |

Two labelling caveats carry through from the research and must not be forgotten
when reading any row:

1. **@/Anchored claims are vendor-only.** No reviews, no community, no repo, no
   independent coverage of any kind. Every ✅ in the Anchored column is a
   marketing claim, not verified behavior. The `?` count in that column is high
   because the vendor documents anchoring and nothing else.
2. **TSNotes data is weak.** Both `tsnotes.app` and `tomsparknotes.com` return
   HTTP 403 to automated requests. Its entire column comes from search-result
   snippets. Most of it is `?`, and the few ✅ values should be treated as
   provisional until someone opens the site in a browser.

A third, smaller caveat: several Simple Sticky Notes and Zhorn feature pages
404'd, so parts of those columns are reconstructed from landing pages and
third-party reviews.

Columns are abbreviated: **SN** SideNotes · **Ntk** Noticky · **Anc** @/Anchored
· **Nzl** Notezilla · **Zhr** Zhorn Stickies · **MS** Microsoft Sticky Notes
(new OneNote-hosted experience unless noted) · **SSN** Simple Sticky Notes ·
**TSN** TSNotes.

SideNotes and Noticky are macOS-only. They are in the matrix because they set
the interaction-model bar, not because they compete for the same users.

---

## Decision codes

| Code | Meaning |
| ---- | ------- |
| **KEEP** | A competitor does this well. Noto does the same thing, at the same level. |
| **IMPROVE** | Exists in the market but shallow or fragile. Noto does it properly. |
| **COMBINE** | Two products each have half. Noto ships both halves. |
| **INNOVATE** | No competitor in the researched set does this. Noto goes first. |
| **DEFER** | Right idea, wrong time. A milestone is named. |
| **REJECT** | Not building it. A principle is named. |

## Priority

| Level | Meaning |
| ----- | ------- |
| **P0** | Core. The product is not itself without it. |
| **P1** | Important. Shipped in the MVP band, but the thesis survives a slip. |
| **P2** | Useful. Post-MVP unless it falls out cheaply. |
| **P3** | Optional. Only if it costs almost nothing. |

## Complexity

| Size | Rough shape |
| ---- | ----------- |
| **XS** | Hours. One API call, one setting, one column. |
| **S** | Days. Contained in one layer. |
| **M** | A week or two. Crosses layers, needs tests. |
| **L** | Weeks. New subsystem, real design work. |
| **XL** | A milestone of its own. Unsolved problems inside it. |

---

## 1. Note model & organization

| Feature | SN | Ntk | Anc | Nzl | Zhr | MS | SSN | TSN | Noto | Pri | Cx |
| ------- | -- | --- | --- | --- | --- | -- | --- | --- | ---- | --- | -- |
| Create / edit / delete notes | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ? | KEEP | P0 | XS |
| Folders / notebooks | ✅ | ❌ | ? | ✅ *memoboards* | ❌ | ❌ | ✅ | ? | COMBINE | P0 | S |
| Nested folders | ? | ❌ | ? | ? | ❌ | ❌ | ? | ? | DEFER *1 level only* | P2 | S |
| Tags | ❌ | ✅ | ? | ✅ | ❌ | ❌ | ❌ | ✅ *hashtags* | COMBINE | P0 | S |
| Per-note color | ✅ *stripe* | ✅ | ✅ *6 palettes* | ✅ | ✅ | ✅ | ✅ | ? | KEEP | P1 | XS |
| Pin to top | ✅ | ❌ *spatial* | ? | ? | ❌ | ~ *on-top pin* | ? | ? | KEEP | P1 | XS |
| Archive | ? | ~ *INFERRED* | ? | ? | ❌ | ❌ | ❌ | ? | KEEP | P1 | XS |
| Recycle bin / trash | ? | ✅ *30-day* | ? | ? | ❌ | ❌ | ✅ | ? | KEEP | P0 | S |
| Manual ordering / drag-drop | ✅ | ~ *positional* | ❌ | ? | ✅ *Manage↔desktop* | ❌ | ? | ? | KEEP | P1 | S |
| Note folding / collapse | ✅ | ✅ | ? | ? *roll-up ?* | ? | ❌ | ? | ? | DEFER *M8* | P3 | S |
| Templates | ❌ | ✅ | ❌ | ? | ❌ | ❌ | ❌ | ? | REJECT | P3 | M |
| Skins / themed note styles | ✅ *themes* | ✅ *4 skins* | ✅ *light/dark* | ✅ | ✅ | ❌ | ✅ | ✅ *8 themes* | REJECT *system theme only* | P3 | M |
| Backlinks / wikilinks / daily notes | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ | REJECT | — | L |

**Reasoning.** The organization axis is an either/or across the entire set:
SideNotes has folders and no tags, Noticky has tags and no folders, Notezilla is
the only product with both and it is the one the vendor itself admits looks
dated. Shipping folders *and* tags is cheap and nobody has it in a product with
modern craft — hence COMBINE at P0. Nesting is capped at one level per the MVP:
adding depth later is easy, removing it is not.

Archive and recycle bin are separate states on purpose. Recycle bin is
recoverable deletion; archive is "not now". Noticky is the only researched
product that appears to have both, and even there the archive state is INFERRED
from its MCP operation list rather than documented.

Three rejections. **Templates** fails principle 1 — it gives the user more to do
once they arrive rather than helping them see the right thing sooner.
**Skins** fails principle 8 and principle 9: Noto follows the system theme and
Mica, and every skin is a rendering path to keep working across Windows
updates. **Backlinks, wikilinks, daily notes** are a knowledge base, explicitly
rejected in `mvp.md`; TSNotes is the only product in the set that has them, and
TSNotes is also the product we could not verify.

---

## 2. Editor & content

| Feature | SN | Ntk | Anc | Nzl | Zhr | MS | SSN | TSN | Noto | Pri | Cx |
| ------- | -- | --- | --- | --- | --- | -- | --- | --- | ---- | --- | -- |
| **Markdown** | ✅ *macOS* | ✅ *macOS* | ? | ❌ | ❌ | ❌ *zero in OneNote* | ❌ | ? | **INNOVATE** | P0 | M |
| Live-styled markdown rendering | ✅ *invisible markup* | ✅ *WYSIWYG* | ? | — | — | — | — | ? | DEFER *M8* | P2 | L |
| Plain-source markdown editing | ✅ *plain mode* | ❌ | ? | — | — | — | — | ? | KEEP *MVP editor* | P0 | S |
| Rich text (non-markdown) | ❌ | ❌ | ? | ✅ | ✅ | ✅ | ✅ | ? | REJECT | — | L |
| Checklists / task lists | ✅ | ✅ *+progress* | ? | ✅ | ❌ | ? | ✅ | ? | KEEP *via markdown* | P0 | XS |
| Code blocks | ✅ *code mode* | ✅ | ? | ❌ | ❌ | ❌ | ❌ | ? | INNOVATE *on Windows* | P1 | S |
| Syntax highlighting | ~ *monospace* | ? | ? | ❌ | ❌ | ❌ | ❌ | ? | DEFER *M8* | P3 | M |
| Tables | ? | ? | ? | ❌ | ❌ | ? | ❌ | ? | DEFER *post-MVP* | P2 | L |
| Images in notes | ✅ | ✅ | ? | ✅ | ✅ | ✅ | ❌ | ? | DEFER *post-MVP* | P2 | M |
| File attachments | ✅ | ❌ | ? | ✅ *file links* | ❌ | ❌ | ❌ | ? | DEFER *post-MVP* | P2 | M |
| Links, auto-link pasted URL | ✅ | ✅ | ? | ✅ *website URL* | ? | ? | ? | ? | KEEP | P1 | XS |
| Hex color swatches | ✅ | ? | ❌ | ❌ | ❌ | ❌ | ❌ | ? | REJECT | P3 | S |
| Auto-save every keystroke | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ? | KEEP | P0 | S |
| Spell check | ? | ? | ? | ✅ | ? | ✅ | ? | ? | DEFER *M8* | P2 | S |
| Print | ✅ *added 2024* | ? | ❌ | ? | ? | ? | ✅ | ? | REJECT | P3 | M |
| Font / size control | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ *legacy cannot* | ✅ | ? | DEFER *M8* | P2 | S |

**Reasoning.** Markdown is the single clearest INNOVATE in the document. Not one
Windows product in the set supports it; OneNote has zero native markdown support
as of Feb 2026 despite thousands of requests. Both macOS leaders have it, which
tells you it is table stakes for the target user and simply has not crossed to
Windows. ADR-004 scopes the MVP subset — headings, bold, italic, inline code,
code blocks, lists, task lists, links, quotes — and defers tables and images
with their own justifications required.

Checklists cost nothing because markdown task lists come free with the parser.
That matters: checklists are table stakes in this category (Notezilla, Simple
Sticky Notes, both macOS products), and Noto gets them as a side effect rather
than as a feature.

Rich text is rejected outright. Markdown is the storage format (ADR-004);
supporting a second content model is a second organizational paradigm in
disguise and fails principle 9.

Tables, images and attachments are DEFER rather than REJECT because ADR-004
explicitly anticipates them being requested and lists the extension path. They
are not in the MVP because each needs its own justification and, in the case of
images, an attachment model that does not yet exist.

Print and hex swatches are rejected on principle 9 — a maintenance line item
each, serving a use Windows already covers or nobody asked for.

---

## 3. Workspace / sidebar

| Feature | SN | Ntk | Anc | Nzl | Zhr | MS | SSN | TSN | Noto | Pri | Cx |
| ------- | -- | --- | --- | --- | --- | -- | --- | --- | ---- | --- | -- |
| **Edge-docked sidebar** | ✅ *core, macOS* | ~ *per-note tab* | ❌ | ❌ | ❌ | ❌ | ❌ | ? | **INNOVATE** *on Windows* | P0 | L |
| Left / right edge choice | ✅ | ✅ | — | — | — | — | — | ? | KEEP | P0 | S |
| Top / bottom edge | ❌ | ❌ | — | — | — | — | — | ? | REJECT | P3 | M |
| Auto-hide with slide-in | ✅ *Hot Side* | ~ *reveal, semantics ?* | — | — | — | — | — | ? | KEEP | P0 | M |
| Adjustable width, remembered | ✅ | ? | — | — | — | — | — | ? | KEEP | P0 | S |
| Overlay vs. reserved work area | ✅ *overlay* | ? | — | — | — | — | — | ? | KEEP *overlay* | P0 | M |
| Global summon / dismiss hotkey | ✅ | ✅ | ✅ *Ctrl+Alt+A* | ✅ | ? | ✅ *Win+Alt+S* | ? | ? | KEEP | P0 | S |
| Manager window / note list | — | — | ~ *window picker only* | ✅ | ✅ | ~ | ✅ | ? | COMBINE *sidebar is the list* | P0 | M |
| Folder navigation in sidebar | ✅ | ❌ | ❌ | — | — | — | — | ? | KEEP | P0 | S |
| Drag & drop within the list | ✅ | ~ | ❌ | ? | ✅ | ❌ | ? | ? | KEEP | P1 | M |
| **Complete keyboard navigation** | ❌ *loudest complaint* | ~ *9 slots* | ? | ~ | ? | ? | ? | ? | **INNOVATE** | P0 | M |
| Every activation surface disableable | ❌ *complaint since 2020* | ? | ? | ? | ? | ❌ | ? | ? | **INNOVATE** | P0 | S |
| Survives full-screen apps | ✅ | ✅ | ? | ? | ? | ? | ? | ? | KEEP | P1 | M |
| Trackpad / touch edge gesture | ✅ *cannot disable* | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ? | REJECT | — | M |

**Reasoning.** The sidebar is the product's primary surface and it does not
exist on Windows in any form. All eight Windows products in the set are
"floating notes plus a manager window"; SideNotes proves the model works and has
been refining it since 2018. This is the highest-value INNOVATE after markdown,
and it is an interaction-model transplant rather than an invention — which is
what makes it tractable.

ADR-007 settles the mechanism: a snapped topmost window, not a Win32 AppBar,
because a reserved work area persists until logoff and a crashed AppBar leaves
the user's desktop permanently wrong. Overlay is therefore the decision, and it
matches SideNotes' behavior anyway.

Two INNOVATE rows here come straight out of user complaints rather than
competitor features. SideNotes users have asked for keyboard-only activation
since 2020 and for the edge gesture to be disableable since 2024; neither is
resolved. Principle 7 exists because of those two reviews. Making every
activation surface independently disableable is an S-sized piece of work that
buys a differentiator the incumbent has declined to ship for six years.

Top/bottom edge is rejected: neither macOS product offers it, no one asked, and
it doubles the docking test matrix. The trackpad gesture is rejected on the
same evidence that produced principle 7 — it is the feature whose failure taught
us the rule.

---

## 4. Floating notes

| Feature | SN | Ntk | Anc | Nzl | Zhr | MS | SSN | TSN | Noto | Pri | Cx |
| ------- | -- | --- | --- | --- | --- | -- | --- | --- | ---- | --- | -- |
| Detach note to its own window | ❌ | ✅ *core* | ✅ | ✅ | ✅ | ✅ | ✅ | ? | COMBINE *sidebar + floating* | P0 | M |
| Always-on-top | ✅ *panel only* | ✅ | ✅ | ✅ *Ctrl+Q* | ? | ✅ *new only; legacy cannot* | ? | ? | KEEP | P0 | S |
| Resize and move | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ? | KEEP | P0 | S |
| Position persisted across restarts | ? | ✅ | ✅ | ✅ | ✅ *even through reboots* | ? | ? | ? | KEEP | P0 | S |
| Opacity | ❌ | ✅ *overlays* | ❌ | ✅ | ? | ❌ | ✅ | ? | KEEP *XAML, ADR-007* | P1 | S |
| Lock position / content | ❌ | ✅ | ❌ | ✅ *password lock* | ❌ | ❌ | ✅ | ? | KEEP | P1 | XS |
| Click-through ghost mode | ❌ | ✅ *image overlays* | ❌ | ❌ | ❌ | ❌ | ❌ | ? | KEEP *whole-window only* | P1 | M |
| Per-pixel click-through | ❌ | ? | ❌ | ❌ | ❌ | ❌ | ❌ | ? | REJECT *impossible, ADR-007* | — | — |
| Multiple floating notes at once | — | ✅ | ✅ *6 per window cap* | ✅ | ✅ | ✅ | ✅ | ? | KEEP *no cap* | P0 | S |
| Layout modes (stack / grid / free) | ❌ | ✅ *⌘⇧1-3* | ~ *6 edge slots* | ❌ | ❌ | ❌ | ❌ | ? | REJECT | P3 | M |
| Inactive fade | ❌ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ? | DEFER *M8* | P3 | S |
| Snap to screen edges / each other | ❌ | ❌ | ❌ | ? | ✅ | ❌ | ? | ? | DEFER *M8* | P3 | S |
| Quick-recall keyboard slots | ❌ | ✅ *9 slots* | ❌ | ❌ | ❌ | ❌ | ❌ | ? | DEFER *M7* | P2 | S |

**Reasoning.** SideNotes and Noticky sit at opposite poles of the same problem —
one drawer optimized for retrieval, many windows optimized for keeping context
visible. Neither ships both. Shipping both is the COMBINE, and it is the reason
"detach to floating note" is P0 rather than a nice-to-have: without it Noto is a
sidebar, and the sidebar alone is the half of the product that does not prove
the thesis.

WindowTop is the feasibility proof for this whole section. Always-on-top,
per-window opacity and click-through are all in its *free* tier, against
arbitrary foreign windows, which is strictly harder than doing it to your own.
ADR-007 then constrains the implementation: opacity via XAML rather than
layered windows, and whole-window click-through only, because per-pixel
input pass-through is not achievable in the framework. That is why per-pixel is
REJECT rather than DEFER — it is not a scheduling decision.

Anchored's six-notes-per-window cap is an arbitrary structural ceiling and Noto
does not copy it.

Layout modes are rejected on principle 9: three arranging behaviors is a
preference surface added instead of a decision, and the sidebar already solves
"I have too many notes on screen".

---

## 5. Context & binding

| Feature | SN | Ntk | Anc | Nzl | Zhr | MS | SSN | TSN | Noto | Pri | Cx |
| ------- | -- | --- | --- | --- | --- | -- | --- | --- | ---- | --- | -- |
| Bind note to an application | ❌ | ✅ *per-app* | ❌ *INFERRED* | ~ *via title* | ~ *"application"* | ❌ | ❌ | ❌ | **IMPROVE** *deterministic* | P0 | L |
| Bind note to one live window | ❌ | ❌ | ✅ *core claim* | ~ *via title* | ? | ❌ | ❌ | ❌ | DEFER *M5 / v0.5* | P0 | XL |
| Bind note to a document / file path | ❌ | ❌ | ❌ | ~ *title only* | ~ *"document"* | ❌ | ❌ | ❌ | DEFER *post-v1* | P1 | XL |
| Bind note to a URL / browser tab | ❌ | ❌ | ❌ | ~ *title only* | ~ *"web site"* | ~ *screenshot source* | ❌ | ❌ | DEFER *post-v1* | P1 | XL |
| **User authors a title match pattern** | — | — | — | ✅ *`*Google Search*`* | ? | — | — | — | **REJECT** *principle 5* | — | — |
| One-gesture manual binding | — | ✅ | ✅ *click-to-anchor* | ✅ *Ctrl+Shift+W* | ? | ❌ | ❌ | ❌ | KEEP | P0 | S |
| Show / hide on focus change | ❌ | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ | KEEP | P0 | M |
| Show without stealing focus | — | ✅ *explicit* | ? | ? | ? | — | — | — | KEEP | P0 | M |
| Note follows window live as it moves | ❌ | ❌ | ✅ *headline claim* | ❌ | ❌ | ❌ | ❌ | ❌ | DEFER *M5, FTO check first* | P1 | XL |
| Re-anchor after target app restart | — | ? | ✅ *claim* | ~ *title* | ? | — | — | — | DEFER *M5, ADR-006* | P1 | XL |
| `detached` as a first-class state | — | ❌ | ❌ *hides silently* | ❌ | ❌ | ❌ | ❌ | ❌ | **INNOVATE** | P0 | M |
| Confidence floor — show nothing when unsure | — | ❌ | ❌ *AI guesses instead* | ❌ | ❌ | ❌ | ❌ | ❌ | **INNOVATE** | P0 | M |
| AI reads the window to infer context | ❌ | ❌ | ✅ *Atlas* | ❌ | ❌ | ❌ | ❌ | ❌ | **REJECT** *principle 6* | — | XL |
| Correct identity for packaged apps (AUMID) | — | — | ? | ? | ? | — | — | — | **INNOVATE** | P0 | M |

**Reasoning.** This is the section the product exists for, and it is the one
where the research most changed the plan. Contextual notes on Windows are *not*
greenfield — Notezilla and Zhorn have shipped attach-to-window for years. That
is good news twice over: it proves demand, and it proves the shallow version is
beatable, because Notezilla's documented mechanism is window-title string
matching with `*` wildcards that the user hand-authors and maintains.

Principle 5 ranks the approaches, and Notezilla's model is explicitly the bottom
rung: configuration wearing a context costume. That is why "user authors a title
match pattern" is the only REJECT in this matrix that is a *competitor's
flagship feature*.

The ladder from ADR-005 maps onto the deferral schedule:

```
  app ---------- window ---------- document/tab/URL ---------- selection
   ^                ^                      ^
   |                |                      |
  MVP             M5/v0.5              post-v1                never
  v0.4            (ADR-006)            (UI Automation)
  CHEAP           DURABLE IDENTITY     RESEARCH PROBLEM
  DETERMINISTIC   IS UNSOLVED
```

Application-level binding is IMPROVE, not KEEP, because Noticky's per-app
binding is the right idea on the wrong OS and Notezilla's is the right OS with
the wrong mechanism. Noto resolves the foreground application via
`EVENT_SYSTEM_FOREGROUND` with correct AUMID handling for packaged apps —
deterministic, explainable in one sentence, and no pattern for the user to
maintain.

Window-level binding is DEFER and not REJECT, but the reason matters. It is
Anchored's entire product and the more impressive demo; it is deferred because
durable identity across restarts has no clean answer (ADR-006), and because
Anchored's own refusal to describe its mechanism *is* the tell that the
mechanism has failure modes. There is also a commercial flag: Anchored asserts
"Patent Pending" on the anchoring concept. Self-asserted and unexamined, with
substantial prior art, but the freedom-to-operate check must happen before live
window-following becomes a headline claim.

Two INNOVATE rows are absences elsewhere rather than features. **`detached` as
a first-class state** — every competitor that binds a note simply hides it when
the target goes away, which is indistinguishable from the note being lost.
**The confidence floor** — Anchored's answer to "the anchor doesn't know what
it's looking at" is to bolt on an AI that reads the window. Noto's answer is to
show nothing and say nothing (principle 6). Atlas is therefore REJECT: it is a
workaround for coarse binding, sold as an AI feature, and principle 6 forbids
using inference where identity is the actual requirement.

---

## 6. Capture

| Feature | SN | Ntk | Anc | Nzl | Zhr | MS | SSN | TSN | Noto | Pri | Cx |
| ------- | -- | --- | --- | --- | --- | -- | --- | --- | ---- | --- | -- |
| Global quick-capture hotkey | ✅ *works when hidden* | ✅ *⌘⇧N* | ? | ✅ | ? | ✅ *Win+Alt+S* | ? | ? | KEEP | P0 | M |
| Return focus to previous app after capture | ? | ✅ | ? | ? | ? | ? | ? | ? | KEEP | P0 | S |
| Clipboard capture | ✅ | ✅ *images* | ? | ✅ *paste images* | ? | ✅ | ? | ? | KEEP | P0 | S |
| Drag & drop text into a note | ✅ | ✅ | ? | ? | ✅ | ? | ? | ? | KEEP | P0 | S |
| Drag & drop files into a note | ✅ | ❌ | ? | ✅ *file links* | ? | ? | ? | ? | KEEP *as link* | P1 | S |
| Region screenshot to note | ❌ | ✅ *Sticky Screenshot* | ? | ❌ | ❌ | ✅ *one-click* | ❌ | ? | DEFER *M6* | P1 | L |
| Image paste as floating overlay | ❌ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ? | DEFER *M6* | P2 | M |
| OCR over captured images | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ *feeds search* | ❌ | ? | DEFER *post-v1* | P3 | L |
| Automatic source capture / recall | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ | ❌ | ? | IMPROVE *= context binding* | P0 | — |
| Capture from text selection in other apps | ✅ *PopClip etc.* | ✅ *PopClip* | ❌ | ❌ | ❌ | ❌ | ❌ | ? | DEFER *M7* | P2 | M |

**Reasoning.** Capture is the one place Microsoft leads. Screenshot, OCR-powered
search, and automatic source capture-and-recall is the strongest capture flow in
the researched set, and no third-party product on Windows has *any* capture
tooling — no clipboard watcher, no snip-to-note, no OCR.

Noto takes the cheap paths in the MVP and defers the subsystem. Hotkey, clipboard
and drag-drop cover the common cases and are each S-sized; `Windows.Graphics.Capture`
works but region capture is a whole subsystem with its own permissions, DPI and
multi-monitor problems, so it goes to M6.

The "automatic source capture and recall" row is worth reading carefully.
Microsoft records where a screenshot came from and surfaces the note again when
you return to that source. That is the same instinct as contextual notes,
implemented narrowly through one feature. Noto does not build it as a capture
feature at all — it is the general case of context binding, so the complexity
cell is empty rather than sized.

---

## 7. Search

| Feature | SN | Ntk | Anc | Nzl | Zhr | MS | SSN | TSN | Noto | Pri | Cx |
| ------- | -- | --- | --- | --- | --- | -- | --- | --- | ---- | --- | -- |
| Search exists at all | ✅ | ✅ | ? *probable gap* | ✅ | ? | ✅ | ✅ | ? | KEEP | P0 | M |
| Full-text over note bodies | ? | ? | ? | ✅ *by content* | ? | ✅ | ✅ | ? | IMPROVE *FTS5* | P0 | M |
| Search over titles and tags | ? | ✅ *tags* | ? | ✅ | ? | ? | ✅ *title* | ? | KEEP | P0 | S |
| Results as you type | ? | ✅ *"instant"* | ? | ✅ *find-as-you-type* | ? | ? | ? | ? | KEEP | P0 | S |
| Keyboard-first search UI | ✅ *shortcut* | ? | ? | ✅ *hotkey* | ? | ✅ *Ctrl+F* | ? | ? | KEEP | P0 | S |
| Filter by folder | ? | — | ? | ? | ? | ❌ | ? | ? | COMBINE | P1 | S |
| Filter by tag | ❌ | ✅ | ? | ✅ | ❌ | ❌ | ❌ | ? | COMBINE | P1 | S |
| Search inside images (OCR) | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ | ❌ | ? | DEFER *post-v1* | P3 | L |
| Launch a URL from a search result | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ? | DEFER *M8* | P3 | XS |

**Reasoning.** Search is table stakes and almost everyone has some version of it,
so most rows are KEEP. The interesting cell is Anchored's column: search is
never mentioned on any page of the vendor site. With 200 notes across 80 windows
there is no confirmed way to find anything. That is the difference between an
anchoring engine and a notes app, and it is the reason search is P0 in a product
whose headline is context.

IMPROVE on full-text is about the performance budget rather than the feature.
Principle 3 requires search results "as fast as typing", which is a measured
requirement; SQLite FTS5 (ADR-003) is how it is met. Nobody in the set publishes
a search latency claim, so this is a quality differentiator that will only be
visible in use.

Folder and tag filters are COMBINE for the same reason the organization axis is —
no researched product can filter by both because no researched product has both.

---

## 8. Windows integration

| Feature | Anc | Nzl | Zhr | MS | SSN | TSN | Noto | Pri | Cx |
| ------- | --- | --- | --- | -- | --- | --- | ---- | --- | -- |
| System tray | ? | ~ *INFERRED* | ✅ | ✅ | ~ *INFERRED* | ? | KEEP | P0 | S |
| Run at login | ? | ~ *INFERRED* | ✅ | ✅ | ~ *INFERRED* | ? | KEEP | P0 | XS |
| Global hotkeys | ✅ *Ctrl+Alt+A only* | ✅ | ? | ✅ | ? | ? | IMPROVE *all remappable* | P0 | M |
| Multi-monitor correctness | ✅ *claim* | ? | ✅ *fixed 10.1c* | ❌ *Dock-to-Desktop broken* | ? | ? | **IMPROVE** | P0 | L |
| **Per-monitor DPI, including mixed DPI** | ? | ? | ? | ? | ? | ? | **INNOVATE** | P0 | L |
| Virtual desktop awareness | ? | ? | ? | ? | ? | ? | **INNOVATE** | P1 | L |
| Mica / system theme / Win11 design | ✅ *native claim* | ❌ *dated* | ❌ *dated* | ~ | ~ | ? | IMPROVE | P1 | M |
| Windows 10 support | ✅ | ✅ | ✅ | ✅ | ✅ | ? | KEEP *10 22H2 + 11 24H2* | P0 | M |
| ARM64 build | ? | ❌ | ❌ *32-bit* | ✅ | ✅ | ? | DEFER *post-v1* | P3 | M |
| Portable / no-registry install | ? | ✅ | ✅ *no registry writes* | ❌ | ❌ | ? | DEFER *post-v1* | P3 | M |
| URI protocol handler (`noto://`) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | DEFER *M7* | P2 | M |
| File Explorer integration | ❌ | ~ *folder notes* | ~ *folders* | ❌ | ❌ | ❌ | DEFER *M7* | P2 | M |
| Windows Search integration | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | DEFER *M7* | P2 | M |
| Toast notifications | ❌ | ✅ *reminders* | ✅ *alarms* | ❌ | ✅ *alarms* | ✅ | REJECT *principle 2* | — | S |
| Windows Hello unlock | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | DEFER *M7* | P2 | M |
| Group Policy / enterprise admin | ❌ | ❌ | ✅ | ~ | ❌ | ❌ | REJECT | — | M |
| Auto-update | ~ *self-hosted, INFERRED* | ✅ | ✅ | ✅ | ✅ | ✅ *lifetime* | DEFER *M8* | P1 | M |

SideNotes and Noticky are omitted from this table — they are macOS products and
every cell would be `—`.

**Reasoning.** This table is mostly `?`, and the `?`s are the finding. Virtual
desktops, per-monitor DPI, Snap Layouts and Mica are undocumented across the
entire Windows set. Microsoft's own new Sticky Notes has a *known* defect where
Dock-to-Desktop fails on extended monitors.

That is why mixed-DPI multi-monitor is an INNOVATE at P0 rather than a quality
chore. WindowTop's public tracker — 126 issues on a mature, actively maintained
product — has high-DPI and mixed-DPI multi-monitor as its single dominant
recurring defect theme. This is precisely where this class of software breaks,
it is in Noto's test matrix from the start (ADR-007), and shipping it correctly
is a differentiator that no competitor can currently claim because none of them
even documents the behavior.

Two rejections. **Toast notifications** fail principle 2 — the default state of
Noto is invisible, and a notification is by definition something the user did
not invoke. This is downstream of rejecting reminders (§11). **Group Policy**
fails principle 9: Zhorn has it and it is genuinely distinctive, but it is an
enterprise administration surface maintained by very few people for a product
with no enterprise story.

---

## 9. Privacy & security

| Feature | SN | Ntk | Anc | Nzl | Zhr | MS | SSN | TSN | Noto | Pri | Cx |
| ------- | -- | --- | --- | --- | --- | -- | --- | --- | ---- | --- | -- |
| Per-note lock | ❌ | ✅ *Touch ID* | ❌ | ✅ | ❌ | ❌ | ✅ *password* | ✅ | KEEP | P1 | M |
| Master password | ❌ | ❌ | ❌ | ✅ | ❌ | ❌ | ? | ? | REJECT *principle 9* | P3 | M |
| Encryption at rest | ❌ | ❌ | ? | ~ *locked notes* | ❌ | ❌ | ? | ✅ *per-note AES-256* | DEFER *post-v1* | P2 | L |
| **Screen-capture exclusion** | ? | ✅ *validated by review* | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | **INNOVATE** *on Windows* | P1 | XS |
| No telemetry | ✅ | ✅ | ? *never addressed* | ? | ✅ *by absence* | ❌ *cloud-first* | ? | ✅ | KEEP | P0 | XS |
| No account required | ✅ | ✅ | ✅ | ~ *sync tier* | ✅ | ❌ *likely required* | ✅ | ✅ | KEEP | P0 | — |
| Never log note content or paths | ? | ? | ? | ? | ? | ? | ? | ? | **INNOVATE** *by commitment* | P0 | S |
| Biometric unlock | ❌ *macOS* | ✅ *Touch ID* | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | DEFER *M7, Hello* | P2 | M |
| Published privacy policy | ✅ | ✅ | ❌ *no page found* | ? | ? | ✅ | ? | ? | KEEP | P1 | XS |

**Reasoning.** Screen-capture exclusion is the cheapest differentiator in the
entire document — one call to `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`
— and it is validated by an actual user review of Noticky rather than by
marketing: "they automatically hide themselves whenever I share my screen." No
Windows product in the set has it. XS complexity, P1 priority, direct service to
principle 10.

ADR-007 attaches one constraint that must survive into the UI copy: it is
described as "hide from screen sharing" and **never** as security. It is a
presentation affinity, not a protection boundary, and promising otherwise would
violate principle 10 by encouraging users to trust it with things it cannot
protect.

Encryption at rest is DEFER rather than KEEP because of an open verification
item: whether Notezilla encrypts local at-rest storage by default is still
unknown, and that answer informs Noto's own decision. TSNotes has per-note
AES-256, but TSNotes is the 403 column.

"Never log note content or paths" is marked INNOVATE not because it is technically
novel but because it is an explicit written commitment (principle 10) that no
researched vendor makes. Anchored has no privacy policy page at all, and its
Atlas layer reads arbitrary window contents by an undisclosed mechanism.

Master password is rejected: per-note lock covers the use, and a second
credential is a second thing to lose and a second recovery flow to maintain.

---

## 10. Sync & data

| Feature | SN | Ntk | Anc | Nzl | Zhr | MS | SSN | TSN | Noto | Pri | Cx |
| ------- | -- | --- | --- | --- | --- | -- | --- | --- | ---- | --- | -- |
| Works fully offline, forever | ✅ | ✅ | ✅ | ~ | ✅ | ❌ | ✅ | ✅ | KEEP *non-negotiable* | P0 | — |
| Notes readable on disk in a findable format | ? | ? | ? *location unstated* | ? | ✅ *db file* | ❌ | ? | ✅ *plain JSON* | KEEP | P0 | S |
| Vendor cloud sync | ✅ *iCloud* | ✅ *CloudKit* | ❌ | ✅ *paid* | ❌ | ✅ *MS account* | ❌ | ❌ | **REJECT** *ADR-002* | — | XL |
| BYO-folder / cloud-drive sync | ❌ | ❌ | ~ *planned phase 5* | ❌ | ❌ | ❌ | ❌ | ❌ | DEFER *post-v1* | P2 | XL |
| Peer-to-peer / LAN transfer | ❌ | ❌ | ❌ | ✅ | ✅ *TCP/SMTP* | ❌ | ❌ | ❌ | REJECT | — | L |
| Export to markdown files | ❌ *images* | ✅ *md/pdf/txt* | ? | ? | ❌ | ❌ | ✅ *txt/rtf* | ? | KEEP | P0 | S |
| Backup / restore | ✅ *automatic* | ? | ? | ? | ? | ? | ✅ | ? | KEEP | P0 | S |
| Mobile client | ✅ *separate $* | ✅ *separate $* | ❌ | ✅ *free* | ❌ | ✅ | ❌ | ❌ | **REJECT** *mvp.md* | — | XL |
| Web client | ❌ | ❌ | ❌ | ✅ | ❌ | ✅ | ❌ | ❌ | **REJECT** *mvp.md* | — | XL |
| Cross-platform desktop | ❌ | ❌ | ❌ | ❌ | ❌ | ~ *Mac lacks it* | ❌ | ✅ | **REJECT** *principle 8* | — | XL |
| Collaboration / sharing | ❌ | ❌ | ~ *planned* | ✅ *LAN/email* | ✅ | ✅ | ~ *social share* | ❌ | REJECT *principle 9* | — | XL |

**Reasoning.** The market-level finding is that local-first *and* encrypted *and*
synced does not exist: you get sync (Notezilla paid, Microsoft cloud-mandated) or
you get local and encrypted (TSNotes, Zhorn), never both.

Noto does not resolve that tension in v1; it picks a side. Principle 4 makes
local-first non-negotiable and ADR-002 rejects accounts and backends outright.
Any future sync is optional, additive, bring-your-own-storage, and must never
become the path of least resistance — which is why BYO-folder sync is DEFER at
P2 while vendor cloud sync is a flat REJECT. Anchored's planned phase 5 is
exactly the BYO-drive model, and it is worth noting that shipping two AI/interop
phases before sync tells you where that vendor's priorities are.

Mobile, web and cross-platform are rejected in `mvp.md` and by principle 8. The
entire value proposition is Windows integration — window tracking, edge docking,
DPI, capture exclusion — which is precisely what a cross-platform wrapper is
worst at, and there is no portability benefit to trade against.

Export to markdown files is P0 and worth stating as a data-ownership feature
rather than a convenience: principle 4 promises that if Noto disappears
tomorrow, the user's data does not.

---

## 11. Automation & extensibility

| Feature | SN | Ntk | Anc | Nzl | Zhr | MS | SSN | TSN | Noto | Pri | Cx |
| ------- | -- | --- | --- | --- | --- | -- | --- | --- | ---- | --- | -- |
| Scripting API | ✅ *AppleScript* | ❌ *vendor-admitted* | ❌ | ❌ | ✅ *public API* | ❌ | ❌ | ❌ | DEFER *post-v1* | P2 | L |
| URL scheme | ✅ *note & folder URLs* | ? | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | DEFER *M7* | P2 | M |
| CLI | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | DEFER *post-v1* | P3 | M |
| OS automation framework hooks | ✅ *Shortcuts* | ✅ *Shortcuts* | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | REJECT *no Windows analogue* | — | — |
| Third-party launcher integrations | ✅ *Alfred, Raycast, +3* | ✅ *PopClip* | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | DEFER *post-v1* | P3 | M |
| MCP / AI assistant surface | ❌ | ✅ *revocable perms* | ✅ *phase 6* | ❌ | ❌ | ❌ | ❌ | ❌ | DEFER *post-v1* | P2 | L |
| On-device AI reading the screen | ❌ | ❌ | ✅ *Atlas* | ❌ | ❌ | ❌ | ❌ | ❌ | **REJECT** *principle 6* | — | XL |
| Plugin system | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | **REJECT** *principle 9* | — | XL |

**Reasoning.** Automation is SideNotes' moat — AppleScript plus Shortcuts plus a
URL scheme is rare in this category — and it is Noticky's self-admitted gap. On
Windows there is almost nothing: Zhorn's public API is the only automation
surface in the entire Windows set.

That is a real opening, but not an MVP one. `mvp.md` puts the URI protocol at
M7, and everything heavier waits for post-v1. The exception is the shape of the
deferral: MCP is DEFER rather than REJECT because Noticky shipping a revocable,
permissioned MCP server is genuinely category-first and worth tracking, and
Anchored shipped connectors in phase 6. Both are evidence the category is moving
there.

Two rejections that look similar but are not. **Atlas-style window-reading AI** is
rejected on principle 6 — it is inference substituting for identity, and Noto's
whole trust argument is that it can explain in one sentence why a note appeared.
**Plugins** are rejected on principle 9 — the maintenance liability exceeds the
benefit for a product maintained by very few people across Windows updates.
Neither rejection is about AI or extensibility being bad ideas; both are about
what Noto can be accountable for.

OS automation hooks are marked REJECT with no complexity because there is no
Windows analogue to Shortcuts to integrate with. It is not a decision so much as
an absence of a thing to decide about.

---

## 12. Reminders & misc

| Feature | SN | Ntk | Anc | Nzl | Zhr | MS | SSN | TSN | Noto | Pri | Cx |
| ------- | -- | --- | --- | --- | --- | -- | --- | --- | ---- | --- | -- |
| Reminders / alarms | ❌ | ~ *Apple Reminders sync* | ❌ | ✅ *+email, snooze* | ✅ | ❌ | ✅ | ✅ | **DEFER** *post-v1* | P3 | L |
| Recurring reminders | ❌ | ? | ❌ | ✅ | ✅ *daily/weekly/monthly* | ❌ | ? | ? | DEFER *post-v1* | P3 | L |
| Sleep / wake-until-date | ❌ | ❌ | ❌ | ~ *snooze* | ✅ | ❌ | ❌ | ? | REJECT *principle 1* | — | M |
| Task management / sub-tasks | ❌ | ~ *checklist progress* | ❌ | ✅ | ❌ | ❌ | ✅ *checkboxes* | ? | REJECT *principle 1* | — | L |
| Calendar integration | ❌ | ✅ *event mentions* | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | REJECT *principle 1* | — | L |
| Word count | ? | ? | ? | ✅ | ? | ? | ? | ? | REJECT *principle 9* | P3 | XS |
| RTL / Unicode text support | ? | ✅ | ? | ? | ✅ | ✅ | ? | ? | KEEP *platform default* | P1 | XS |
| Accessibility / screen reader | ? | ? | ? *never mentioned* | ? | ✅ *improved 10.2a* | ~ | ? | ? | KEEP *v1.0 hardening* | P1 | M |
| Localization | ✅ *9 languages* | ✅ | ? | ? | ✅ | ✅ | ✅ | ? | DEFER *post-v1* | P3 | M |

**Reasoning.** Reminders are the largest rejection in this document and the one
most likely to be relitigated, so the reasoning is worth stating fully.

Four of eight products have them, Zhorn's sleep/wake-until-date model is
genuinely good, and Microsoft *not* having them is called out as a notable gap
in the research. The case for building them is real.

The case against is principle 1: reminders make Noto more capable but not more
present. A reminder is something the user has to do once they arrive, not
something that helps them see the right thing sooner. Worse, reminders pull the
product toward task management, which is a different product with different
competitors, and they require notifications, which principle 2 forbids by
default. `mvp.md` records this as "post-v1, if ever" — the "if ever" is the
honest part.

Note the ordering consequence: rejecting reminders is what makes toast
notifications rejectable in §8. They are one decision, not two.

Markdown task lists remain available. A user who wants a checkbox gets a
checkbox; what they do not get is Noto telling them about it later.

---

## Decision summary

Counting all 143 feature rows across the twelve categories:

```
  KEEP       50  ##################################################
  DEFER      39  #######################################
  REJECT     30  ##############################
  INNOVATE   12  ############
  COMBINE     6  ######
  IMPROVE     6  ######
```

By priority, for the 113 rows that are being built (KEEP / IMPROVE / COMBINE /
INNOVATE / DEFER):

```
  P0   54    core        the product is not itself without these
  P1   27    important   MVP band, thesis survives a slip
  P2   20    useful      post-MVP
  P3   12    optional    only if nearly free
```

The shape to notice: **KEEP is the largest group by a wide margin.** Most of
what Noto does is table stakes done properly. The differentiation is
concentrated in twelve INNOVATE rows and six IMPROVE rows, and it is worth
being honest that a product is not made of its differentiators — it is made of
the fifty rows where it simply has to not be worse than a 2010-era freeware
sticky note.

The second thing to notice: **30 rejections against 12 innovations.** That
ratio is the point of principle 9, and it is the number that should be defended
when scope pressure arrives.

### The INNOVATE list — where Noto has no competition

Twelve rows, clustering into four claims:

| # | Feature | Why nobody has it | Pri | Cx |
| - | ------- | ----------------- | --- | -- |
| 1 | **Markdown** | Zero of the Windows set; OneNote has none as of Feb 2026 despite thousands of requests | P0 | M |
| 2 | Code blocks on Windows | Follows from markdown; no Windows competitor has them | P1 | S |
| 3 | **Edge-docked sidebar on Windows** | All eight Windows products are floating notes + a manager window | P0 | L |
| 4 | Complete keyboard navigation | SideNotes' loudest complaint, unresolved since 2020 | P0 | M |
| 5 | Every activation surface disableable | SideNotes' second-loudest complaint, unresolved since 2024 | P0 | S |
| 6 | **`detached` as a first-class state** | Every binding competitor just hides the note, indistinguishably from losing it | P0 | M |
| 7 | **Confidence floor — show nothing when unsure** | Anchored bolts on an AI instead; the rest guess or don't try | P0 | M |
| 8 | Correct AUMID identity for packaged apps | Undocumented everywhere; `ApplicationFrameHost.exe` is the trap | P0 | M |
| 9 | Mixed-DPI multi-monitor correctness | Undocumented across the entire set; Microsoft's own is broken on extended monitors | P0 | L |
| 10 | Virtual desktop awareness | Undocumented across the entire set | P1 | L |
| 11 | Screen-capture exclusion on Windows | Noticky has it on macOS; no Windows product does. One API call. | P1 | XS |
| 12 | Never log note content or file paths | No researched vendor makes the commitment; Anchored has no policy page | P0 | S |

Grouped, the four claims are:

```
  MARKDOWN            1, 2      a table-stakes feature with zero Windows competition
  EDGE SIDEBAR        3         a proven macOS model absent from Windows entirely
  DETERMINISTIC       6, 7, 8   context you can explain in one sentence
  CONTEXT
  KEYBOARD-FIRST      4, 5      built from six years of a competitor's user complaints
  + QUALITY           9, 10, 11, 12
```

Rows 9 through 12 are the quiet ones. They are not demo features, and three of
the four are invisible when they work. They are also where WindowTop's 126-issue
tracker says this class of software actually fails, which makes them the
difference between a product that survives two years of Windows updates and one
that does not.

### The REJECT list — with the principle that says no

| Rejected | Principle | Reason |
| -------- | --------- | ------ |
| Reminders, recurring, sleep/wake, sub-tasks, calendar | **1** | Capability without presence. Drifts to task management. |
| Templates | **1** | More to do on arrival, not seeing the right thing sooner. |
| Toast notifications | **2** | Appears without being invoked. Downstream of rejecting reminders. |
| User-authored title match patterns | **5** | Configuration wearing a context costume. Notezilla's model, explicitly. |
| AI reading the window to infer context (Atlas) | **6** | Inference substituting for identity. Unexplainable notes destroy trust. |
| Trackpad / touch edge gesture | **7** | The exact feature whose undisableable version produced principle 7. |
| Cross-platform desktop | **8** | The entire value is Windows integration; wrappers are worst at exactly that. |
| Mobile clients, web client | **8**, mvp.md | Contradicts the Windows-native focus. |
| Rich text as a second content model | **9** | A second paradigm alongside markdown. |
| Skins / themed note styles | **8**, **9** | System theme and Mica instead; each skin is a rendering path to maintain. |
| Note layout modes (stack / grid / free) | **9** | A preference added instead of a decision. |
| Top / bottom edge docking | **9** | Doubles the docking test matrix, nobody asked. |
| Print, hex swatches, word count | **9** | Maintenance line items serving no evidenced need. |
| Master password | **9** | Per-note lock covers it; a second credential is a second recovery flow. |
| Group Policy / enterprise admin | **9** | Distinctive in Zhorn, but no enterprise story to serve. |
| Plugin system | **9** | Maintenance liability far exceeding benefit. |
| Peer-to-peer / LAN / email note transfer | **9** | A networking stack for a use nobody has asked Noto for. |
| Vendor cloud sync, accounts | **4**, ADR-002 | Core must never require an account or a backend. |
| Collaboration, sharing, multi-user | **9**, vision | Not the product. |
| Backlinks, wikilinks, daily notes, graph view | **1**, mvp.md | That is a knowledge base. |
| Per-pixel click-through | ADR-007 | Not technically achievable in the framework. Not a scheduling choice. |
| OS automation framework hooks | — | No Windows analogue to Shortcuts exists to integrate with. |

Two rejections deserve a second look before v1, and both are flagged rather than
buried: **reminders** (four of eight competitors have them, and `mvp.md` says
"if ever" rather than "never") and **encryption at rest**, which is DEFER not
REJECT but depends on an unresolved verification item about Notezilla.

### The DEFER list — with the milestone

| Deferred to | Features |
| ----------- | -------- |
| **M5 / v0.5** | Window-level binding · window following · re-anchor after target restart |
| **M6** | Region screenshot capture · image paste as floating overlay |
| **M7** | URI protocol handler · File Explorer integration · Windows Search integration · Windows Hello unlock · quick-recall keyboard slots · capture from text selection in other apps |
| **M8** | Live-styled markdown editing · syntax highlighting · spell check · font and size control · note folding · inactive fade · snap to edges · launch URL from search result · auto-update |
| **Post-MVP** | Nested folders beyond one level · tables · images in notes · file attachments |
| **Post-v1** | Document / URL-level context · OCR · encryption at rest · BYO-folder sync · scripting API · CLI · MCP surface · third-party launcher integrations · ARM64 · portable install · localization |

Three deferrals carry conditions rather than just dates:

- **Window following (M5)** is gated on the @/Anchored freedom-to-operate check.
  The "Patent Pending" claim is self-asserted, unexamined, and sits alongside
  substantial prior art — but it must be resolved before this becomes a headline
  claim, not after.
- **Window-level binding (M5)** is gated on v0.4 validating the thesis. If
  application-level context does not feel valuable in daily use, the hardest
  engineering in the plan should not be started.
- **Document / URL-level context (post-v1)** is the real differentiator and is
  deferred precisely *because* it is. It needs its own research and ADR, not a
  rushed MVP slot.

---

## Dependencies

What blocks what. An arrow means the target cannot ship correctly until the
source does.

```
                        ┌─────────────────────────┐
                        │  FOUNDATION (v0.1)      │
                        │  shell · SQLite · tray  │
                        │  settings · logging     │
                        └───────────┬─────────────┘
                                    │
              ┌─────────────────────┼─────────────────────┐
              v                     v                     v
      ┌───────────────┐    ┌────────────────┐    ┌────────────────┐
      │ NOTE MODEL    │    │ MARKDOWN       │    │ WINDOW         │
      │ folders·tags  │    │ EDITOR         │    │ PRESENTATION   │
      │ colors·pin    │    │ (ADR-004)      │    │ (ADR-007)      │
      │ archive·bin   │    │                │    │ topmost·DPI    │
      └───┬───────┬───┘    └────┬──────┬────┘    └───┬────────┬───┘
          │       │             │      │             │        │
          │       │             │      └─ task lists │        │
          │       │             │         (free)     │        │
          │       │             v                    v        v
          │       │        ┌─────────┐      ┌────────────┐  ┌──────────┐
          │       └───────>│ EXPORT  │      │  SIDEBAR   │  │ FLOATING │
          │                │ to .md  │      │  edge dock │  │  NOTES   │
          │                └─────────┘      │  auto-hide │  │ on-top   │
          │                                 │  width     │  │ opacity  │
          v                                 └──────┬─────┘  │ lock     │
   ┌─────────────┐                                 │        │ click-thru│
   │ SEARCH FTS5 │                                 │        └────┬─────┘
   │ (ADR-003)   │                                 │             │
   └──────┬──────┘                                 │             │
          │                                        │             │
          │  folder + tag filters                  │             │
          └────────────────┬───────────────────────┘             │
                           v                                     │
                  ┌────────────────────┐                         │
                  │ KEYBOARD NAV       │<────────────────────────┘
                  │ every action       │
                  │ every surface off  │
                  └─────────┬──────────┘
                            │
                            v
                  ┌────────────────────┐        ┌──────────────────┐
                  │ QUICK CAPTURE      │───────>│ CLIPBOARD        │
                  │ global hotkey      │        │ DRAG & DROP      │
                  └────────────────────┘        └──────────────────┘
```

The context chain is separate and strictly sequential:

```
  EVENT_SYSTEM_FOREGROUND hook
            │
            v
  AUMID identity for packaged apps ──── (without this, every UWP app
            │                            resolves to ApplicationFrameHost.exe
            │                            and app binding is silently wrong)
            v
  CONTEXT RESOLVER (ADR-005) ─────┐
            │                     │
            v                     v
  CONFIDENCE FLOOR          `detached` STATE (ADR-006)
            │                     │
            └──────────┬──────────┘
                       v
            APP-LEVEL BINDING  ......... v0.4  THE THESIS
                       │
                       │  requires: floating notes (surface to show)
                       │  requires: show without stealing focus
                       v
            DURABLE WINDOW IDENTITY (ADR-006)  ... unsolved
                       │
                       ├── gate: FTO check on "Patent Pending"
                       ├── gate: v0.4 validated the thesis
                       v
            WINDOW-LEVEL BINDING  ...... v0.5
                       │
                       v
            WINDOW FOLLOWING (live move/resize)
                       │
                       │  requires: mixed-DPI correctness first —
                       │  this is where the category breaks
                       v
            DOCUMENT / URL BINDING (UI Automation) ... post-v1
```

The non-obvious edges, stated plainly:

| Blocker | Blocks | Why |
| ------- | ------ | --- |
| AUMID identity | all app binding | Every packaged app otherwise resolves to `ApplicationFrameHost.exe`. Binding would be silently wrong, which is worse than absent (principle 6). |
| Floating notes | app-level binding | A bound note needs a surface to appear on that is not the sidebar. |
| Show-without-stealing-focus | app-level binding | A note that steals focus when an app activates makes the feature actively hostile. |
| Mixed-DPI correctness | window following | Continuous edge-docking against a foreign window across a DPI boundary is the documented failure mode of this entire software class. |
| Confidence floor | every binding rung | Each finer rung adds a way to be wrong. The floor has to exist before the rungs do, not after. |
| Markdown parser | task lists, code blocks, export | All three fall out of it; none needs separate work. |
| Folders **and** tags | folder + tag search filters | The filters are only a differentiator because both axes exist. |
| Keyboard nav | activation-surface toggles | Turning off the edge trigger is only safe once every action has a keyboard path. |
| Recycle bin | archive | Same state machine; building archive first means building it twice. |
| Capture exclusion | nothing | One API call, no dependencies. It can ship any time — which is why it should ship early. |

---

## Contradictions found

Reviewed against `principles.md` and `mvp.md`. Three items where the source
documents are in tension or where this matrix had to make a call that is not
strictly derivable from them. None is resolved silently.

### 1. Checklists are rejected as task management but shipped as markdown

`mvp.md` lists **task lists** as in-scope MVP markdown, and §12 of this matrix
rejects task management on principle 1. ADR-004 resolves this by noting task
lists "come free" with the markdown parser, and the distinction held here is
that a checkbox is content whereas a reminder is behavior.

This is a real line, but it is thin, and it will be pushed on. The predictable
requests are checklist progress indicators (Noticky has them), sorting by
completion (Notezilla has it), and "remind me about unchecked items". Each of
those crosses from content into task management. **The line should be written
down before the first such request, not during it.**

### 2. Reminders — resolved to DEFER

**This was a genuine contradiction and has been corrected.** `mvp.md` places
reminders under *Deferred to later milestones* with the target "post-v1, if
ever"; this matrix originally recorded REJECT.

They are not the same decision. DEFER says the milestone is not yet; REJECT
says the principle forbids it. Four of the eight researched products ship
reminders, and Zhorn's sleep/wake model is genuinely good — so this is not a
decision to make by accident.

**Resolution: DEFER, priority P3.** Principle 1 rules out reminders as a
*first-class system* — a scheduling engine with recurrence, snooze and
escalation is task management, and that is rejected. It does not rule out a
simple per-note reminder post-v1, should evidence justify one.

The matrix rows have been updated accordingly. The REJECT in §12 applies to
task management as a subsystem, not to the existence of any reminder.

### 3. Screen-capture exclusion is P1 here and MVP-scoped in `mvp.md`

`mvp.md` places capture exclusion in the MVP under *Privacy*, alongside per-note
lock and no-telemetry. This matrix rates it P1 rather than P0.

Not a contradiction in scope — it is in the MVP either way, and at XS complexity
the priority is close to academic. It is recorded because the rating reflects a
judgement `mvp.md` does not make: the product's thesis survives without capture
exclusion, whereas it does not survive without markdown, the sidebar, or app
binding. If the preference is that privacy features are P0 by definition, this
row should be raised.

### Non-contradictions, checked and clear

- **Anchored's six-note-per-window cap** is not copied. No source document
  requires a cap, and principle 9 does not argue for one.
- **Overlay rather than reserved work area** for the sidebar. `mvp.md` says
  "snapped topmost window", ADR-007 gives the reason. Consistent.
- **Encryption at rest deferred while per-note lock ships.** `mvp.md` scopes
  per-note lock to the MVP and does not mention at-rest encryption, and open
  verification item 4 (does Notezilla encrypt locally by default?) is still open.
  Consistent, and correctly sequenced.
- **MCP deferred rather than rejected.** `mvp.md` lists the AI/MCP surface under
  *Rejected for v1 entirely*, but with the qualifier "Post-v1 at the earliest".
  This matrix records DEFER to post-v1, which matches the qualifier. The
  separate, harder REJECT — AI as the *mechanism for context resolution* — is
  recorded independently in §5 and §11 and is consistent with ADR-005 and
  principle 6.
