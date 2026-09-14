# Windows Sticky Notes — Competitor Research (raw)

Research date: **2026-09-14**
Method: WebSearch + WebFetch of vendor sites, Microsoft docs, press coverage, review aggregators.

Confidence labels used throughout:
- **CONFIRMED** — stated on vendor/official page or a dated reputable article, URL cited.
- **INFERRED** — reasonable deduction from confirmed facts or secondary sources; not directly stated.
- **UNKNOWN** — could not verify; do not rely on.

> Caveat: several vendor sub-pages (Simple Sticky Notes `/features`, `/manual`; Zhorn `/features.html`) returned 404 — those product feature lists are reconstructed from the vendor landing/download page plus third-party reviews and are labelled accordingly. TSNotes' own site (`https://tsnotes.app/` and `https://tomsparknotes.com/`) returned **HTTP 403** to automated fetch on both attempts, so TSNotes data is from search-result snippets only and is the weakest section here.

---

## 0. Headline answer: does ANY of them attach notes to applications/windows?

**YES — two of the five do, and this is the single most interesting finding.**

| Product | Attach note to app/window/document/URL | Mechanism |
|---|---|---|
| **Notezilla** | **YES** — flagship feature | Window **title** string matching + `*` wildcards |
| **Zhorn Stickies** | **YES** — long-standing feature | "attached to an application, web site, document or folder" |
| Microsoft Sticky Notes (new) | **PARTIAL** — "source capture" + recall on revisit | Captures source of a screenshot, recalls note when you revisit the source |
| Simple Sticky Notes | NO (not found) | — |
| TSNotes | NO (not found) | — |

Details in each section below. This means contextual notes are **not** greenfield — but the existing implementations are shallow (see §7).

---

## 1. Notezilla (Conceptworld)

Primary: https://www.conceptworld.com/Notezilla/

### Pricing / licensing
- **CONFIRMED** Two purchase models, from https://www.conceptworld.com/Notezilla/BuyNow :
  - **One-time payment: $29.95** — "permanent license", usable on **up to 2 computers** by the same user, **no cross-device sync**.
  - **Subscription: $19.95/year, plus $10.00 for the first year only** — includes cloud sync, install on any number of computers used by a single person, all future versions free.
- **CONFIRMED** On cancellation/expiry of the subscription you retain "a permanent license for Notezilla 9 (Windows)" usable on a single computer. (same URL)
- **CONFIRMED** 30-day money-back guarantee; trial available as direct download; purchase unlocks the existing trial install. (same URL)
- **CONFIRMED** No free tier — trial only. (review aggregate, https://www.capterra.com/p/147902/Notezilla/reviews/ and https://sourceforge.net/software/product/Notezilla/)
- **CONFIRMED** Current major version is **Notezilla 9**. (https://www.conceptworld.com/Support/Upgrade/Notezilla, referenced via search)
- **INFERRED** Paid upgrade between major versions for perpetual holders (an "Upgrade to Notezilla 9" page exists); exact upgrade price **UNKNOWN**.

### Windows support / distribution
- **CONFIRMED** "Windows 11 and Windows 10", 32-bit and 64-bit; a **portable version** exists. (https://www.conceptworld.com/Notezilla/)
- **CONFIRMED** Also distributed via **Microsoft Store** (listing `xp8jj6n539pnlp`, https://apps.microsoft.com/detail/xp8jj6n539pnlp). Store listing metadata (version, size, arch) **UNKNOWN** — page did not render to fetch.
- **INFERRED** Primary distribution is a classic Win32 `.exe` installer from the vendor site; MSIX only via the Store listing.

### Note model
- **CONFIRMED** Desktop sticky notes: "Sticky notes on desktop give you faster access to your to-do lists. Plus, it is easy to take notes on a sticky note while working with other programs." (https://www.conceptworld.com/Notezilla/Features)
- **CONFIRMED** Also a manager/organizer surface ("memoboards") — notes can be moved off-desktop into boards: "Move your sticky notes into folders called memoboards & keep your desktop clean". (https://www.conceptworld.com/Notezilla/)
- **UNKNOWN** Whether there is a persistent docked *sidebar* in the modern sense. Notezilla's non-desktop surface is a memoboard browser window, not an edge-docked panel. Treat "sidebar: no" as **INFERRED**.

### Organization
- **CONFIRMED** **Memoboards** = folders for notes. (vendor site)
- **CONFIRMED** **Tags/labels**: "Assign tags/labels to sticky notes to quickly access related sticky notes". (vendor site)
- **CONFIRMED** **Search**: "Find-as-you-type, search by content, tags & hotkey support". (vendor site)
- **CONFIRMED** Colors, skins, transparency, shadow are per-note properties. (https://www.conceptworld.com/Notezilla/Features — screenshot caption "Color, Skin, Transparency, Shadow, Website URL, Secured Notes")

### Editor
- **CONFIRMED** Rich text: font styles, colors, alignment, word count; spell check. (vendor Features page nav + landing page)
- **CONFIRMED** **Checklists**: "Checklist sticky notes allow you to create a to-do-list with checkboxes"; "instantly tick-off your tasks, sort them or review the pending tasks". (vendor site)
- **CONFIRMED** **Images**: "Insert pictures inside sticky notes. Just copy from any website & paste". (vendor site)
- **CONFIRMED** Can "link files & folders". (vendor site)
- **UNKNOWN** Tables. Not mentioned anywhere. **INFERRED: no table support.**
- **UNKNOWN** Markdown. Not mentioned. **INFERRED: no markdown.**

### Floating behavior
- **CONFIRMED** Always-on-top: "Keep desktop sticky notes always on top of other apps"; dedicated help page https://www.conceptworld.com/Notezilla/HelpTopic/Editing-Sticky-Notes-Stay-On-Top ; shortcut **Ctrl+Q** from inside a note to pin it. Auto-pin rules, pin color and tracking rate configurable via Options. (help page via search snippet)
- **CONFIRMED** Transparency/opacity per note. (Features page caption)
- **CONFIRMED** Lock/secure: "Lock & encrypt sticky notes with a master password"; "Password-protected sticky notes". (vendor site)
- **UNKNOWN** Roll-up (collapse to title bar), snap-to-edge, screen-edge docking. Not documented. **INFERRED: roll-up probably exists (common in this class) but unverified — do not claim.**

### Reminders
- **CONFIRMED** "Set reminders to sticky notes. Never miss an important task or appointment", with **email delivery** and **snooze**. (vendor site)
- **CONFIRMED** Recurring tasks and sub-task management listed by review aggregators. (https://www.softwaresuggest.com/notezilla, https://technologycounter.com/products/notezilla) — **INFERRED** for exact recurrence granularity.

### Contextual / attach-to-window  ← KEY
- **CONFIRMED** "Stick notes to webpages, documents, programs, apps, folders, or any window. Automatically see the right note when you access that website, doc, etc." (https://www.conceptworld.com/Notezilla/)
- **CONFIRMED — mechanism is window-title matching**: "Notezilla uses the title of the window to show or hide a particular sticky note." (https://www.conceptworld.com/Notezilla/HelpTopic/Working-With-Sticky-Notes-Sticking-Notes-Documents-Websites)
- **CONFIRMED** Wildcards: "To match only a part of the window title, double click on any window title that is listed … and prefix or suffix the title with the asterisk (*) wildcard." Documented examples: `*Google Search*`, `*Microsoft Outlook`. (same URL)
- **CONFIRMED** Shortcuts: **Ctrl+W** opens a picker of currently-open windows; **Ctrl+Shift+W** sticks the note directly to the underlying window. (same URL)
- **CONFIRMED** Caveat: "while sticking notes to folders in Windows Explorer is similar to sticking notes to anything else, you might need to configure Windows Explorer a bit to make it work correctly". (https://www.conceptworld.com/Notezilla/Attach-Sticky-Notes-To-Documents-Websites)
- **INFERRED weaknesses of this design**: title-only matching means (a) no true URL/process/document-path identity, (b) false positives when unrelated windows share title substrings, (c) breaks when an app changes its title format, (d) no per-tab granularity beyond whatever the browser puts in the window title, (e) user must hand-craft wildcards. Notezilla itself lists no limitations, so this is analysis, not a quoted claim.

### Capture
- **CONFIRMED** Hotkeys for quick note creation and for sticking to a window. (vendor + help pages)
- **CONFIRMED** Paste images from any website. (vendor site)
- **UNKNOWN** Built-in screenshot tool. Not stated anywhere. **INFERRED: no dedicated screenshot capture.**

### Sync / cloud / mobile
- **CONFIRMED** "Automatically sync sticky notes between your computers"; transferred data is encrypted (vendor describes end-to-end encryption). (vendor site)
- **CONFIRMED** Free mobile apps for **iPhone/iPad and Android**; also **Mac (iOS apps)**; a **web app** (Notezilla.Net, https://www.notezilla.net/Home/Index). (vendor site)
- **CONFIRMED** Sharing: send notes "Across Local Network (LAN)" to another computer, and "Over Internet/Email" to another contact. (Features page)
- **CONFIRMED** Cloud sync is **subscription-gated** (or first year free with some purchases). (BuyNow page; https://www.conceptworld.com/qa/197/notezilla-cloud-sync-licensing-and-data-storage)

### Windows integration
- **INFERRED** System tray + run-at-startup (standard for this app class; screenshots imply it). Not explicitly quoted on the pages fetched.
- **UNKNOWN** Virtual desktop awareness, multi-monitor specifics, DPI/per-monitor-DPI behavior. Nothing documented.

### Privacy / security
- **CONFIRMED** Master-password lock + encryption of notes; encrypted cloud transfer. (vendor site)
- **UNKNOWN** Whether local-at-rest storage is encrypted by default, and the cloud provider/jurisdiction.

### Strengths
- Deepest feature set of the five; genuinely the category leader on Windows. Reviewers call it "the best sticky notes app on Windows desktop". (https://sourceforge.net/software/product/Notezilla/, https://slashdot.org/software/p/Notezilla/)
- Only product with a **first-class, documented attach-to-window** workflow with hotkeys and wildcards.
- Full stack: desktop + mobile + web + LAN sharing.

### Weaknesses / complaints
- **CONFIRMED** UI is dated: "obviously not polished design-wise"; developers have acknowledged they "will improve the UI and make it more intuitive". (review roundups, https://subscribed.fyi/notezilla/review/, https://sourceforge.net/software/product/Notezilla/)
- **CONFIRMED** No free tier (30-day trial only). (Capterra/SourceForge)
- **CONFIRMED** Windows-only desktop (mobile apps exist, but no native macOS/Linux desktop). (https://betterstickies.com/blog/best-sticky-notes-apps-2026)
- **CONFIRMED** Two-axis pricing (perpetual vs subscription + first-year surcharge) is confusing. (BuyNow page structure)

---

## 2. Zhorn Stickies

Primary: https://www.zhornsoftware.co.uk/stickies/

### Pricing / licensing
- **CONFIRMED** "Completely free" — freeware, usable at home and at the office without charge. (vendor site; https://www.zhornsoftware.co.uk/stickies/download.html)
- **CONFIRMED** Donations accepted. (https://betterstickies.com/blog/best-sticky-notes-apps-2026)

### Version / maintenance status
- **CONFIRMED** Latest version **v10.2a, January 2025**. (https://www.zhornsoftware.co.uk/stickies/versions.html)
- **CONFIRMED** Prior releases: v10.1d (April 2023), v10.1c (October 2022) — i.e. **roughly annual or slower** cadence. (same URL)
- **CONFIRMED** Maintained for over two decades by a single developer (Tom Revell). (https://betterstickies.com/blog/betterstickies-vs-zhorn-stickies)
- **NOTE / discrepancy**: the vendor's own front page still says "Stickies works with Windows 7, Windows 8 and Windows 10" (no Win11), while the **download page** says "Stickies runs on Windows 7, 8, 10 and 11. Earlier versions of Windows are no longer supported." Treat **Windows 11 as CONFIRMED-supported** per the download page; the front page is stale.

### Distribution
- **CONFIRMED** `stickies_setup_10_2a.exe` (2875 KB) and `stickies_setup_10_2a.zip` (2584 KB) — installer **and** a zip for manual/portable-style install. (download page)
- **CONFIRMED** Installs to `c:\Program Files (x86)\stickies` → **32-bit binary** running on 64-bit Windows. (download page)
- **CONFIRMED** No MSIX / Microsoft Store distribution mentioned. **INFERRED: not on the Store.**
- **CONFIRMED** Available via winget (`ZhornSoftware.Stickies`, https://winget.ragerworks.com/package/ZhornSoftware.Stickies).

### Note model
- **CONFIRMED** Desktop sticky notes — "yellow rectangular windows" that store "text or images". (vendor site)
- **CONFIRMED** Notes persist in place: "Once on screen, notes will remain where placed until closed, even through reboots". (vendor site)
- **CONFIRMED** A separate **"Manage"** surface exists (v10.1c changelog mentions "drag-and-drop functionality between Manage and Desktop"). **INFERRED**: this is a note-browser window, not an edge-docked sidebar.

### Organization
- **CONFIRMED** "Hierarchical friends list" — this is the **contacts/peers list for sending notes between machines**, NOT a note-folder tree. Do not misread this as note organization.
- **UNKNOWN** Tags. Not mentioned. **INFERRED: no tags.**
- **CONFIRMED** Appearance customization: "Stickies appearance can be customised; fonts, colours and buttons may be changed, and styles saved. Notes can be resized." Plus a **skins** system (https://www.zhornsoftware.co.uk/stickies/skins.pl).
- **UNKNOWN** Full-text search. Not listed on the front page. **INFERRED**: the Manage window likely offers search, but unverified — do not claim.

### Editor
- **CONFIRMED** Text and images; font/colour control; "justified text formatting" added in v10.2a. (versions page)
- **CONFIRMED** "International language, Unicode and RTL text support". (vendor site)
- **UNKNOWN** Checklists, tables, markdown, file attachments. **INFERRED: none of these.**

### Floating behavior
- **CONFIRMED** **Snap**: "Stickies can snap to each other and to the sides of the screen". (vendor site)
- **CONFIRMED** Notes are resizable and persist position across reboots. (vendor site)
- **UNKNOWN** Always-on-top toggle, opacity, roll-up, lock. Not listed on the front page. **INFERRED**: always-on-top almost certainly exists in a 20-year-old sticky app, but it is NOT documented on the pages fetched — do not claim.

### Reminders / sleep
- **CONFIRMED** "Stickies can have alarms set to ensure you notice them at a point you choose". (vendor site)
- **CONFIRMED** **Recurring + snooze-to-date**: "Stickies can be hidden for a certain period, until a specified date and time, or to wake every day, week or month". (vendor site) — this is a genuinely good "sleep until" model.

### Contextual / attach-to-window  ← KEY
- **CONFIRMED** "Stickies can be attached to an application, web site, document or folder **so they only show when it's on screen**". (https://www.zhornsoftware.co.uk/stickies/)
- **CONFIRMED** Third-party comparison notes this as distinctive: Stickies can "attach to windows and documents". (https://betterstickies.com/blog/best-sticky-notes-apps-2026)
- **UNKNOWN** The matching mechanism (title? process? URL?). Not documented on any page fetched. **INFERRED**: window-title based, like Notezilla, given the era and architecture — but unverified.

### Capture
- **UNKNOWN** Screenshot capture, clipboard watcher, global new-note hotkey. Not documented. **INFERRED: minimal; no screenshot tool.**
- **CONFIRMED** Drag-and-drop between Manage and Desktop. (versions page, v10.1c)

### Sync / networking
- **CONFIRMED** "Stickies can be transferred from one machine to another either over a **TCP/IP network connection**, or by using an **SMTP mail server or MAPI client**." (vendor site)
- **CONFIRMED** This is **peer-to-peer / LAN / email transfer, NOT cloud sync**. There is no account, no cloud service, no mobile app.
- **CONFIRMED** v10.1d (April 2023) "removed support for outdated network note formats to enhance security". (versions page)

### Windows integration
- **CONFIRMED** "Small and simple, it writes to a database file, and **does not alter the registry**." (vendor site)
- **CONFIRMED** "AD network administrators can use **Group Policy** to control settings" — genuinely enterprise-friendly. (vendor site)
- **CONFIRMED** **API** "to allow integration with other applications" (https://www.zhornsoftware.co.uk/stickies/api.html); v10.2a added "New API functionality". (versions page)
- **CONFIRMED** Multi-monitor: v10.1c "fixed display issues across multiple monitors" — so multi-monitor is handled, historically buggily. (versions page)
- **CONFIRMED** v10.2a added "improved settings for accessibility features". (versions page)
- **UNKNOWN** Virtual desktop awareness, per-monitor DPI. Nothing documented; **INFERRED: weak, given 32-bit legacy Win32 codebase.**

### Privacy / security
- **CONFIRMED** No encryption or password protection mentioned anywhere on the site. **INFERRED: none.**
- **CONFIRMED** Fully local (database file), no cloud, no account → strong privacy by absence.

### Strengths
- Free, tiny (~2.8 MB), no registry writes, no cloud, no account.
- **Attach-to-window** + **sleep/wake scheduling** are both genuinely good and rare.
- Group Policy + API → unusual enterprise/automation story for a freeware sticky app.
- Two-decade track record; still shipping.

### Weaknesses
- **CONFIRMED** "Dated interface". (https://betterstickies.com/blog/best-sticky-notes-apps-2026)
- **CONFIRMED** 32-bit, Program Files (x86) install; slow release cadence (~yearly).
- **CONFIRMED** Vendor front page still claims Win10 as newest supported OS — poor signalling.
- No cloud sync, no mobile, no tags, no encryption.

---

## 3. Microsoft Sticky Notes  ← the "verify current state" one

Primary doc: https://support.microsoft.com/en-us/windows/apps/stickynotes/get-started-with-sticky-notes

### CRITICAL: there are TWO products both called "Sticky Notes" right now

This is the single biggest correction to any pre-2024 understanding.

```
         Microsoft "Sticky Notes" (2026)
                     |
      +--------------+-----------------+
      |                                |
  LEGACY app                      NEW experience
  UWP, Store 9NBLGGH4QGHW         Win32, built INSIDE OneNote
  v6.1.4.0 (Oct 29 2024)          launched preview Aug 2024, GA 2025
  standalone .exe-less UWP        Win+Alt+S, or button in OneNote
  "compatibility bridge"          "the supported direction"
  syncs via MS account/Outlook    syncs via OneNote/M365 cloud
```

- **CONFIRMED** In **August 2024** a new **Win32-based** Sticky Notes launched as preview, with Windows 11 design, always-on-top pinning, and **integration with Microsoft OneNote**. (https://en.wikipedia.org/wiki/Sticky_Notes)
- **CONFIRMED** Legacy app's latest stable release is **version 6.1.4.0, dated 29 October 2024**. (same URL)
- **CONFIRMED** "As of 2024, Sticky Notes is an integral part of Microsoft OneNote." (same URL)
- **CONFIRMED** The new experience "is developed as a feature within OneNote rather than being its own separate application"; rolled out to all Windows users after a 2024 beta. (https://www.howtogeek.com/onenote-sticky-notes-windows-11-rollout/)
- **CONFIRMED** Microsoft's guidance: "Going forward, new features will be available in the new app, and to access the latest features and enhancements, it's recommended to move to the new Sticky Notes in OneNote." (Microsoft 365 Insider blog / techcommunity, https://techcommunity.microsoft.com/blog/microsoft365insiderblog/introducing-the-new-sticky-notes-app-on-windows/4223819)
- **CONFIRMED** "Microsoft's own support pages make clear that the newer Sticky Notes experience is now the supported direction, while the legacy app is increasingly a **compatibility bridge** rather than the future." (https://windowsforum.com/threads/sticky-notes-refresh-onenote-integration-brings-new-features-and-tradeoffs.395949/)
- **UNKNOWN** Any announced **retirement date** for the legacy UWP app. None found. **INFERRED: legacy app remains installable from the Store today (2026) but is feature-frozen.**

### Copilot / AI — verify carefully
- **CONFIRMED** Coverage explicitly states the biggest update "has nothing to do with AI-powered features or Copilot this time". (https://www.windowscentral.com/software-apps/the-sticky-notes-app-just-got-the-biggest-update-since-microsoft-shipped-windows-11)
- **CONFIRMED** The intelligence that IS shipping is **OCR**, not generative AI: "Optical character recognition (OCR) support for screenshots and images" and "Enhanced search functionality powered by OCR". (https://www.howtogeek.com/onenote-sticky-notes-windows-11-rollout/)
- **CONFIRMED** The *legacy* UWP app historically had "Insights"/Cortana intelligence (stock tickers, flight numbers) — that is **old**, from the 2016 Anniversary Update era, and should not be cited as current. (https://en.wikipedia.org/wiki/Sticky_Notes)
- **CONCLUSION (INFERRED, high confidence): as of Sept 2026 there is no shipped Copilot feature inside Sticky Notes.** Do not claim Copilot integration. Copilot lives in OneNote proper, which is the host app — so a user with M365 Copilot gets Copilot in OneNote, not in the Sticky Notes surface specifically. **UNKNOWN** whether that boundary has moved.

### Pricing / licensing
- **CONFIRMED** Free. (https://apps.microsoft.com/detail/9NBLGGH4QGHW)
- **INFERRED** The new experience requires the **OneNote for Windows app**, which is free to install; a Microsoft 365 subscription is not required for basic OneNote. **UNKNOWN** whether any Sticky Notes sub-feature is M365-gated.
- **UNKNOWN** Whether a Microsoft account is strictly *required* for the new OneNote-hosted experience. Legacy docs say account is "optional but recommended for syncing". **INFERRED**: the new one is "cloud-first, account-based" (https://windowsforum.com/threads/sticky-notes-refresh-onenote-integration-brings-new-features-and-tradeoffs.395949/) → account likely effectively required. Flag as a real uncertainty.

### Distribution
- **CONFIRMED** Legacy: Microsoft Store, `9NBLGGH4QGHW` (UWP/MSIX). (https://apps.microsoft.com/detail/9NBLGGH4QGHW)
- **CONFIRMED** New: shipped inside the OneNote for Windows app (Win32/Store hybrid), plus a Start-menu entry that "launches OneNote directly into Sticky Notes mode". (windowsforum/techcommunity)
- **CONFIRMED** Launch shortcut **Win + Alt + S**. (https://www.howtogeek.com/onenote-sticky-notes-windows-11-rollout/)

### Note model
- **CONFIRMED** Desktop sticky notes + a notes list. New version adds "**pin to desktop** behavior" and "Dock to Desktop". (windowsforum; howtogeek)
- **CONFIRMED** New Start-menu launch path into a Sticky-Notes-only mode of OneNote. (windowsforum)

### Organization
- **CONFIRMED** Colors: "Change the note background color"; color changes sync. (MS support page)
- **CONFIRMED** Search: search box / **Ctrl+F**. (MS support page)
- **CONFIRMED** New version has "Enhanced search functionality powered by OCR" — i.e. **searches text inside images/screenshots**. (howtogeek)
- **UNKNOWN/NO** Folders, notebooks, tags. Not offered in the Sticky Notes surface. **INFERRED: none** (organization happens in OneNote proper).

### Editor
- **CONFIRMED (legacy)** Hard limitation, quoted from Microsoft: "**You cannot currently change the font or size of note text**". (MS support page)
- **CONFIRMED** Images: users can "add a picture". (MS support page)
- **CONFIRMED** Ink: on compatible devices "write with your finger or stylus". (MS support page)
- **CONFIRMED** New version has "improved image handling". (windowsforum)
- **CONFIRMED** **No markdown anywhere in OneNote**: "As of February 2026, across every version of Microsoft OneNote there is zero native markdown support, despite thousands of users requesting it". (https://unmarkdown.com/blog/onenote-markdown)
- **UNKNOWN** Tables, checklists in the new Sticky Notes surface. **INFERRED: basic rich text only** (https://betterstickies.com/blog/best-sticky-notes-apps-2026 lists "basic rich text").

### Floating behavior
- **CONFIRMED (legacy) — hard limitation, quoted from Microsoft**: "**You cannot currently have Sticky Notes stay on top of other applications**". (MS support page)
- **CONFIRMED (new) — this was FIXED**: the new app added **always-on-top via a pin icon**. "If you want to keep a note on top of your other windows, you can click the pin icon". (https://www.neowin.net/news/microsoft-updates-new-sticky-notes-with-always-on-top-and-other-features/, https://www.xda-developers.com/sticky-notes-onenote-update-always-on-top/, hands-on dated 2024-09-19 https://www.windowslatest.com/2024/09/19/hands-on-with-windows-11s-new-sticky-notes-always-on-top-feature-now-available/)
- **CONFIRMED** Resizable: "Grab the edges of the note and increase its width and height". (MS support page)
- **UNKNOWN/NO** Opacity, roll-up, lock, snap. Not offered. **INFERRED: none.**

### Reminders
- **UNKNOWN → effectively NO.** The MS support documentation "makes no mention of reminders". Legacy UWP had Cortana-era reminders which are gone with Cortana. **INFERRED: no reminders in the current Sticky Notes surface.** This is a notable gap vs. all four competitors.

### Contextual  ← partial
- **CONFIRMED** "**Automatic source capture** for easy reference" and "**Automatic note recall when revisiting original sources**". (https://www.howtogeek.com/onenote-sticky-notes-windows-11-rollout/)
- **INFERRED** This is scoped to the screenshot-capture flow: the app records where a screenshot came from, and surfaces that note again when you return to that source. It is **not** a general "pin this note to that app window" feature like Notezilla/Zhorn. Exact matching mechanism and supported sources **UNKNOWN**.

### Capture
- **CONFIRMED** "**One-click screenshot capture**" with a camera icon that "capture[s] the currently active screen and embed[s] the screenshot into notes". (howtogeek; windowslatest hands-on)
- **CONFIRMED** OCR over captured screenshots/images, feeding search. (howtogeek)
- **CONFIRMED** Copy whole note: "Copying an entire note is now possible with just a few clicks by using the ellipsis icon and the 'Copy' button". (neowin)
- **CONFIRMED** Win+Alt+S global launch. (howtogeek)
- **Assessment**: **screenshot+OCR+source-capture is the strongest *capture* story of all five products.**

### Sync / cloud / mobile
- **CONFIRMED (legacy)** "In Sticky Notes version 3.0 and later, using the same Microsoft account, you can sign in to sync your notes across apps and your favorite devices." (MS support page)
- **CONFIRMED** Notes reachable on Windows, iOS, Android via OneNote and Outlook; web access at `outlook.office.com/mail/notes`. (https://en.wikipedia.org/wiki/Sticky_Notes)
- **CONFIRMED (new)** "Cross-platform synchronization with OneNote's Android and iOS mobile apps". (howtogeek)
- **CONFIRMED** Gap: "OneNote's Windows version contains various features that are missing on the macOS desktop version, such as sticky notes" — and there are reports of "Sticky Notes in OneNote app for iOS missing" (https://learn.microsoft.com/en-us/answers/questions/5816105/sticky-notes-in-onenote-app-for-ios-missing). Mobile parity is shaky.

### Windows integration
- **CONFIRMED** Ships with / installable on Windows; Start-menu entry; Win+Alt+S; purple-tinted icon added for identification. (neowin/techcommunity)
- **CONFIRMED** **Multi-monitor bug**: "The 'Dock to Desktop' feature doesn't work with extended monitors, though Microsoft is reportedly working on fixes." (https://www.howtogeek.com/onenote-sticky-notes-windows-11-rollout/) — dated; **UNKNOWN** whether fixed by Sept 2026.
- **UNKNOWN** Virtual desktop behavior, per-monitor DPI.

### Privacy
- **CONFIRMED** Cloud-first, account-based, Microsoft-hosted. (windowsforum)
- **UNKNOWN/NO** Per-note password or local encryption. **INFERRED: none.**
- **Assessment**: weakest privacy story of the five for a local-first user.

### Strengths
- Free, preinstalled/one-click, zero friction.
- Best capture flow (screenshot + OCR + source capture).
- Real cross-device sync via an ecosystem users already have.
- Always-on-top finally shipped in the new app.

### Weaknesses / complaints — substantial and well documented
- **CONFIRMED** Broad user backlash to the OneNote-integrated update. (https://windowsforum.com/threads/user-backlash-microsofts-sticky-notes-update-frustrates-windows-users.339944/, https://www.gizchina.com/microsoft/microsofts-sticky-notes-update-why-users-are-not-happy)
- **CONFIRMED** "The redesigned version appears **slower and less efficient**, which undermines its purpose as a quick and simple note-taking tool." (same sources)
- **CONFIRMED** "Reports of **lost notes** following the update have surfaced." (same sources)
- **CONFIRMED** "**Forced OneNote integration** means users are now required to interact with OneNote features, even if they prefer not to." (same sources)
- **CONFIRMED** "Many users have **reverted to the previous version** of Sticky Notes available on the Microsoft Store." (same sources)
- **CONFIRMED** Zero markdown support, thousands of requests ignored. (unmarkdown.com, Feb 2026)
- **CONFIRMED** Legacy app: no font/size control, no always-on-top. (MS support page)
- Confusing dual-product situation is itself a weakness — users don't know which app they have.

---

## 4. TSNotes  ⚠ LOWEST CONFIDENCE SECTION

Stated primary: https://tsnotes.app/ → **HTTP 403 Forbidden** to automated fetch.
Actual product site found: https://tomsparknotes.com/ → also **HTTP 403 Forbidden**.

All data below is from **search-result snippets only**. Nothing here is verified against a rendered vendor page. **Recommend a manual browser visit to tomsparknotes.com before relying on any of it.**

- **CONFIRMED-via-snippet** Tagline: "TSNotes — sticky notes, minus the cloud". (https://tomsparknotes.com/)
- **CONFIRMED-via-snippet** Price: **$10, one-time**, "no subscription, no account, no cloud, no telemetry", includes **lifetime updates** and free email support.
- **CONFIRMED-via-snippet** Platforms: **Windows, macOS, Linux** — the only cross-platform-desktop product of the five.
- **CONFIRMED-via-snippet** Storage: "Notes live as **plain JSON on your disk**".
- **CONFIRMED-via-snippet** Security: **per-note AES-256 encryption**.
- **CONFIRMED-via-snippet** Features named: **reminders, hashtags, wikilinks, daily notes, eight themes**.
- **INFERRED** wikilinks + daily notes ⇒ a PKM/Obsidian-influenced model, unusual in the sticky-note category. Likely markdown-ish, but **markdown support is UNKNOWN**.
- **UNKNOWN — everything else**: distribution format (installer/MSIX/Store), Windows version support, whether notes actually float on the desktop vs. live in one window, sidebar, always-on-top, opacity, roll-up, snap, docking, recurring reminders, screenshot/clipboard capture, global hotkeys, drag/drop, tray, startup, multi-monitor, virtual desktops, DPI, search, attach-to-window, version number, release dates, developer identity beyond "TomSparkBox" (https://tomsparkbox.com/projects).
- **UNKNOWN** Maturity and user base. It does **not appear** in the BetterStickies 2026 roundup (https://betterstickies.com/blog/best-sticky-notes-apps-2026), which covered the other four. **INFERRED: very small / new / niche indie product.**
- **UNKNOWN** Any reviews, forum threads, or complaints — none found.

### Assessment
Positioning is sharp (local-only, one-time $10, encrypted, cross-platform, PKM-flavored), but this is an **indie product with near-zero third-party footprint**. Treat as a positioning reference, not a market threat.

---

## 5. Simple Sticky Notes

Primary: https://www.simplestickynotes.com/
(`/features`, `/features/`, `/manual` all returned **404** — feature list assembled from the landing page, the download page, and third-party reviews.)

### Pricing / licensing
- **CONFIRMED** "Simple Sticky Notes is **completely free for personal and commercial use**." (https://www.simplestickynotes.com/download)
- **CONFIRMED** Landing page: "simple, easy-to-use, absolutely **free**, fast and efficient note taking software". (https://www.simplestickynotes.com/)
- **CONFIRMED** No paid tier, pro version, or donation mentioned on the download page. (same URL)

### Version / distribution — the most current of the five
- **CONFIRMED** **Version 6.9, updated 22 February 2026** — the most recently updated product in this set. (https://www.simplestickynotes.com/download)
- **CONFIRMED** Installer: `Setup_SimpleStickyNotes.exe`, **2.77 MB**. (same URL)
- **CONFIRMED** Windows support: "**Windows® 11 / 10 / 8.1 / 8 / 7**", plus **Arm64 builds of Windows 11/10** — notably the only one of the five with explicit ARM64 support. (same URL)
- **CONFIRMED** Requirements: "PC with a 1.5 GHz Intel or AMD processor and 2 GB of RAM". (same URL)
- **UNKNOWN** Portable version (not mentioned on download page); one third-party review calls it "Lightweight and easy to install and move" (https://simple-sticky-notes.en.lo4d.com/windows) which is **not** a portability claim. **INFERRED: no official portable build.**
- **UNKNOWN** MSIX / Microsoft Store presence. **INFERRED: classic installer only.**

### Note model
- **CONFIRMED** Desktop sticky notes.
- **CONFIRMED** **Note Explorer**: "view all your notes in one place, search for specific notes, create notebooks, and access deleted notes" — i.e. a manager window + recycle bin. (third-party feature roundups; https://www.makeuseof.com/free-sticky-notes-app-for-windows-better-than-default/)
- **INFERRED** Not an edge-docked sidebar.

### Organization
- **CONFIRMED** **Notebooks/groups**: "Organize notes into folders and group notes". (https://simple-sticky-notes.en.lo4d.com/windows)
- **CONFIRMED** **Search**: "Search and filter notes by title or content". (same URL)
- **CONFIRMED** Colors + downloadable **themes/skins**; **Dark Mode** "to reduce eye strain and increase readability in low-light environments". (third-party roundups; https://betterstickies.com/blog/best-sticky-notes-apps-2026 calls out its "dark mode theme engine")
- **UNKNOWN** Tags. **INFERRED: no tags** (notebooks only).

### Editor
- **CONFIRMED** Basic rich text. (https://betterstickies.com/blog/best-sticky-notes-apps-2026)
- **CONFIRMED** **Checklists**: "create checklists by adding checkboxes to sticky notes and track your tasks by marking them off when done". (third-party roundups)
- **CONFIRMED — notable limitation**: "**cannot hold images**". (https://betterstickies.com/blog/best-sticky-notes-apps-2026)
- **CONFIRMED** Export: "print, export notes in **text and RTF** formats, and share on Twitter and WhatsApp". (third-party roundups)
- **CONFIRMED** "Print your notes to paper or PDF". (https://simple-sticky-notes.en.lo4d.com/windows)
- **UNKNOWN** Tables, markdown, attachments. **INFERRED: none.**

### Floating behavior
- **CONFIRMED** **Opacity**: "transparency adjustments" / "choosing desired colors and opacity". (lo4d + roundups)
- **UNKNOWN** Always-on-top, roll-up, snap, dock-to-edge. Vendor feature page 404'd and the lo4d review explicitly "does not specifically address always on top, roll up, lock (as a standalone feature), dock, hotkeys, themes/skins, or export". **INFERRED**: always-on-top and roll-up almost certainly exist (they are table stakes and the product is mature at v6.9), but **unverified — do not claim without checking the app.**

### Reminders
- **CONFIRMED** **Alarms**: "an alarm feature to set reminders so you never forget a task again" / "Set task reminders with alert notifications". (roundups; lo4d)
- **UNKNOWN** Recurring alarms. **INFERRED: probably simple one-shot alarms only.**

### Contextual
- **UNKNOWN → NO.** No mention anywhere of attaching notes to apps/windows/documents. **INFERRED: not supported.**

### Capture
- **UNKNOWN** Global hotkeys, clipboard capture, screenshot. Not documented. **INFERRED: minimal.**

### Sync
- **CONFIRMED-weak** One third-party review claims "Automatically sync notes across devices" (https://simple-sticky-notes.en.lo4d.com/windows). **This is doubtful** — the vendor site advertises no account, no cloud service, and no mobile app, and no other source corroborates it. **INFERRED: this is likely a review-site error describing backup/restore, not real cloud sync. Treat sync as NOT SUPPORTED until verified.**
- **CONFIRMED** Backup/restore and a deleted-notes bin exist via Note Explorer. (roundups)

### Windows integration / privacy
- **CONFIRMED** Password protection: "Lock notes with a password for security". (https://simple-sticky-notes.en.lo4d.com/windows)
- **UNKNOWN** Tray, startup, multi-monitor, virtual desktops, DPI. **INFERRED: standard tray + startup.**
- **UNKNOWN** Encryption at rest.

### Strengths
- **CONFIRMED** Free for commercial use, actively maintained (Feb 2026), clean modern-ish interface, dark mode, ARM64 support, notebooks + search + alarms + checklists + password lock. Best free/features ratio of the five.
- Recommended over the built-in app by mainstream press. (https://www.makeuseof.com/free-sticky-notes-app-for-windows-better-than-default/)

### Weaknesses
- **CONFIRMED** No images. (betterstickies 2026)
- **CONFIRMED** Windows-only. (betterstickies 2026)
- No (verified) sync, no mobile, no tags, no attach-to-window, no markdown.

---

## 6. Cross-product comparison matrix

Legend: **Y** confirmed yes · **N** confirmed/strongly-inferred no · **?** unknown · **~** partial

| | Notezilla | Zhorn Stickies | MS Sticky Notes (new) | TSNotes | Simple Sticky Notes |
|---|---|---|---|---|---|
| Price | $29.95 once / $19.95-yr | Free | Free | $10 once | Free |
| Latest version / date | v9 (current) | 10.2a · Jan 2025 | OneNote-hosted · 2025-26 | ? | 6.9 · Feb 2026 |
| Win11 support | Y | Y | Y | ? | Y (+ARM64) |
| Distribution | exe + Store + portable | exe + zip + winget | Store / in OneNote | ? | exe |
| Desktop sticky notes | Y | Y | Y | ? | Y |
| Manager/board surface | Y (memoboards) | Y (Manage) | ~ (OneNote) | ? | Y (Note Explorer) |
| Edge-docked sidebar | N | N | N | ? | N |
| Folders/notebooks | Y | N | N | ? | Y |
| Tags | Y | N | N | Y (hashtags) | N |
| Search | Y | ? | Y (+OCR) | ? | Y |
| Rich text | Y | Y | Y | ? | Y |
| Markdown | N | N | **N** (zero in OneNote) | ? | N |
| Checklists | Y | N | ? | ? | Y |
| Images | Y | Y | Y | ? | **N** |
| Tables | N | N | ? | ? | N |
| Attachments/file links | Y | N | N | ? | N |
| Always-on-top | Y (Ctrl+Q) | ? | Y (new only; legacy **cannot**) | ? | ? |
| Opacity | Y | ? | N | ? | Y |
| Roll-up | ? | ? | N | ? | ? |
| Snap to edges | ? | **Y** | N | ? | ? |
| Lock / password | Y | N | N | Y (AES-256) | Y |
| Reminders/alarms | Y (+email, snooze) | Y | **N** | Y | Y |
| Recurring | Y | **Y** (daily/weekly/monthly) | N | ? | ? |
| **Attach note to app/window** | **Y** (title+wildcard) | **Y** | ~ (source recall) | N | N |
| Screenshot capture | N | N | **Y** (+OCR) | N | N |
| Global hotkeys | Y | ? | Y (Win+Alt+S) | ? | ? |
| Cloud sync | Y (paid) | N (LAN/SMTP p2p) | Y (MS account) | **N by design** | N |
| Mobile apps | Y (iOS/Android) | N | Y (OneNote/Outlook) | N | N |
| Web access | Y | N | Y | N | N |
| Cross-platform desktop | N | N | ~ (Mac lacks it) | **Y** (Win/Mac/Linux) | N |
| Encryption | Y (master pw) | N | N | **Y** (per-note AES-256) | ~ (pw lock) |
| Group Policy / API | N | **Y** both | N | N | N |

---

## 7. Synthesis

### Which are actually strong

1. **Notezilla — strongest overall.** Only product that is complete: attach-to-window, memoboards, tags, checklists, images, reminders with email+snooze+recurrence, always-on-top, opacity, encryption, cloud sync, mobile, web, LAN share. Real weakness is aesthetic (dated UI, vendor-acknowledged) and commercial (no free tier, two confusing price axes). **It is the product to beat on features; it is beatable on design and pricing clarity.**

2. **Microsoft Sticky Notes — strongest distribution, and improving fast on capture.** Screenshot + OCR + automatic source capture/recall is the best capture flow here, and always-on-top finally shipped. But it is mid-migration and bleeding trust: documented backlash over slowness, lost notes, and forced OneNote coupling, with users actively reverting to the legacy app. **No reminders. No markdown. No password. No opacity.** Its weakness is that it is now a feature of a heavy app, not a lightweight utility.

3. **Simple Sticky Notes — strongest free option.** Most recently updated (v6.9, Feb 2026), ARM64, notebooks, search, alarms, checklists, dark mode, password lock, free for commercial use. Held back by **no images**, no sync, no tags, no contextual features.

4. **Zhorn Stickies — strong niche, weak surface.** Attach-to-window + sleep/wake scheduling + Group Policy + an API + 2.8 MB + no registry writes is a genuinely distinctive combination. But 32-bit, ~annual releases, dated UI, no tags, no search (unverified), no encryption, no cloud. It survives on inertia and trust, not on capability.

5. **TSNotes — not a real competitor yet.** Sharp positioning ($10 once, local JSON, AES-256 per note, Win/Mac/Linux, wikilinks + daily notes), but the site blocks automated access, it appears in no major 2026 roundup, and nothing about its floating/window behavior is verifiable. Useful as a *positioning* reference for local-first + PKM framing; not a market threat.

### The common feature baseline (what Noto must have to be taken seriously)

Every serious entrant in this category ships:

```
desktop sticky notes -> persist position across reboot
  + per-note color / theme  + dark mode
  + resize
  + a manager window (list all notes, search, restore deleted)
  + some grouping (notebooks OR memoboards)
  + basic rich text
  + checklists
  + alarms / reminders
  + always-on-top toggle
  + tray + run-at-startup
  + free tier or a cheap one-time price
```

Near-baseline (3 of 5, expected but not universal): opacity control, images, password lock, export/print.

### What NONE of them do — the gaps

These are the genuinely open spaces:

1. **No real markdown.** Zero of the five confirm markdown support; OneNote's total absence of markdown has thousands of unsatisfied requests as of Feb 2026. A markdown-native sticky note is unoccupied ground.

2. **No edge-docked sidebar.** All five are "floating notes + a manager window". None offers a persistent screen-edge panel. The "sticky notes AND sidebar" model is unclaimed.

3. **Contextual attachment is primitive everywhere it exists.** Notezilla matches on **window title strings with `*` wildcards** — user-authored, fragile, no per-tab/URL/process/document-path identity. Zhorn's mechanism isn't even documented. Microsoft's is limited to recalling a screenshot's source. **Nobody does robust app/window/URL/document identity, nobody does automatic context detection, nobody exposes per-browser-tab or per-file binding.** This is the biggest technical gap and it is directly adjacent to what Noto is apparently aiming at.

4. **No modern Windows platform integration.** Across all five: virtual desktop awareness is undocumented; per-monitor DPI is undocumented; Microsoft's own new app has a *known bug* where "Dock to Desktop" fails on extended monitors. None mentions Windows 11 notification/Focus Assist integration, Snap Layouts, Widgets, or Mica/acrylic.

5. **Local-first + encrypted + syncing is unserved.** You can have sync (Notezilla, paid; Microsoft, cloud-mandated) or you can have local+encrypted (TSNotes, Zhorn), never both. No E2E-encrypted sync with a local-first store.

6. **No screenshot/OCR outside Microsoft.** Only Microsoft ships screenshot capture and OCR search. Every third-party product has zero capture tooling — no clipboard watcher, no snip-to-note, no OCR.

7. **No modern design language anywhere.** Notezilla "obviously not polished", Zhorn "dated interface", Microsoft's rebuild is called slower and heavier than what it replaced. The entire category looks like 2010.

8. **No table support, no attachments beyond Notezilla's file links, no code blocks, no linking between notes** (except TSNotes' unverified wikilinks).

9. **No automation surface.** Only Zhorn has an API. No CLI, no URL scheme, no plugin model, no scripting hooks, no MCP/agent integration in any of the five.

---

## 8. Open questions / verification TODO

- [ ] **TSNotes**: site 403s automated fetch. Visit `https://tomsparknotes.com/` in a real browser to confirm float behavior, always-on-top, Windows version support, distribution format, version number.
- [ ] **Simple Sticky Notes**: `/features` and `/manual` 404. Install v6.9 to verify always-on-top, roll-up, snap, dock, hotkeys, and whether the lo4d "syncs across devices" claim is real (suspected false).
- [ ] **Zhorn Stickies**: confirm whether always-on-top and full-text search exist; determine the attach-to-window matching mechanism (title vs. process vs. path).
- [ ] **Microsoft Sticky Notes**: confirm whether a Microsoft account is now *required* for the new OneNote-hosted experience; confirm whether the extended-monitor "Dock to Desktop" bug is fixed; confirm no Copilot surface has shipped inside Sticky Notes as of Sept 2026; check for any announced legacy-UWP retirement date.
- [ ] **Notezilla**: confirm roll-up / snap / dock support, exact upgrade pricing between major versions, and Store-listing version metadata.

## 9. Source index

Vendor / official:
- https://www.conceptworld.com/Notezilla/
- https://www.conceptworld.com/Notezilla/Features
- https://www.conceptworld.com/Notezilla/BuyNow
- https://www.conceptworld.com/Notezilla/Attach-Sticky-Notes-To-Documents-Websites
- https://www.conceptworld.com/Notezilla/HelpTopic/Working-With-Sticky-Notes-Sticking-Notes-Documents-Websites
- https://www.conceptworld.com/Notezilla/HelpTopic/Editing-Sticky-Notes-Stay-On-Top
- https://www.conceptworld.com/qa/197/notezilla-cloud-sync-licensing-and-data-storage
- https://www.notezilla.net/Home/Index
- https://www.zhornsoftware.co.uk/stickies/
- https://www.zhornsoftware.co.uk/stickies/download.html
- https://www.zhornsoftware.co.uk/stickies/versions.html
- https://www.zhornsoftware.co.uk/stickies/api.html
- https://support.microsoft.com/en-us/windows/apps/stickynotes/get-started-with-sticky-notes
- https://apps.microsoft.com/detail/9NBLGGH4QGHW
- https://apps.microsoft.com/detail/xp8jj6n539pnlp
- https://techcommunity.microsoft.com/blog/microsoft365insiderblog/introducing-the-new-sticky-notes-app-on-windows/4223819
- https://learn.microsoft.com/en-us/answers/questions/5816105/sticky-notes-in-onenote-app-for-ios-missing
- https://tomsparknotes.com/ (403 to fetch)
- https://tsnotes.app/ (403 to fetch)
- https://www.simplestickynotes.com/
- https://www.simplestickynotes.com/download

Press / analysis:
- https://en.wikipedia.org/wiki/Sticky_Notes
- https://www.howtogeek.com/onenote-sticky-notes-windows-11-rollout/
- https://www.neowin.net/news/microsoft-updates-new-sticky-notes-with-always-on-top-and-other-features/
- https://www.xda-developers.com/sticky-notes-onenote-update-always-on-top/
- https://www.windowslatest.com/2024/09/19/hands-on-with-windows-11s-new-sticky-notes-always-on-top-feature-now-available/
- https://www.windowscentral.com/software-apps/the-sticky-notes-app-just-got-the-biggest-update-since-microsoft-shipped-windows-11
- https://windowsforum.com/threads/sticky-notes-refresh-onenote-integration-brings-new-features-and-tradeoffs.395949/
- https://windowsforum.com/threads/user-backlash-microsofts-sticky-notes-update-frustrates-windows-users.339944/
- https://windowsforum.com/threads/new-vs-old-sticky-notes-on-windows-cloud-sync-onenote-integration-search.408278/
- https://www.gizchina.com/microsoft/microsofts-sticky-notes-update-why-users-are-not-happy
- https://unmarkdown.com/blog/onenote-markdown
- https://www.makeuseof.com/free-sticky-notes-app-for-windows-better-than-default/
- https://betterstickies.com/blog/best-sticky-notes-apps-2026
- https://betterstickies.com/blog/betterstickies-vs-zhorn-stickies

Reviews / aggregators:
- https://www.capterra.com/p/147902/Notezilla/reviews/
- https://sourceforge.net/software/product/Notezilla/
- https://slashdot.org/software/p/Notezilla/
- https://subscribed.fyi/notezilla/review/
- https://www.softwaresuggest.com/notezilla
- https://technologycounter.com/products/notezilla
- https://www.softpedia.com/get/Office-tools/Other-Office-Tools/Stickies.shtml
- https://simple-sticky-notes.en.lo4d.com/windows
- https://winget.ragerworks.com/package/ZhornSoftware.Stickies
