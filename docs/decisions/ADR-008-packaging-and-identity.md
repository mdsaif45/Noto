# ADR-008 — Packaging: sparse package, self-contained, Velopack updates

**Status:** Accepted
**Date:** 2026-09-14
**Related:** ADR-001 (framework), ADR-002 (local-first)

---

## Context

Several Windows features Noto needs require **package identity** — the OS will
not grant them to a plain unpackaged executable:

```
  app notifications (AppNotificationManager)   requires identity
  StartupTask (run at login)                   requires identity
  protocol activation (noto:// URI)            requires identity
```

But full MSIX packaging brings real costs: installation into a sandboxed
location, a harder debugging story, and Store-style distribution friction.

A further constraint emerged in research: **the `ms-appinstaller:` URI protocol
has been disabled by default since December 2023** (it was being abused for
malware distribution). That removes MSIX's one-click web-install story, which
was its main distribution advantage for a product not shipping through the
Store.

---

## Problem

How is Noto packaged, installed and updated, given that it needs package
identity for some features but wants a conventional desktop installation and a
working auto-update path?

---

## Options considered

| Option | Identity | Install | Updates |
| ------ | -------- | ------- | ------- |
| **A. Unpackaged** | No | Normal | Any updater |
| **B. Full MSIX** | Yes | Sandboxed | App Installer (crippled) or Store |
| **C. Sparse package** | Yes | Normal Program Files | Any updater |
| **D. Microsoft Store only** | Yes | Store | Store |

---

## Decision

**Option C: a sparse package (packaged with external location), with
`WindowsAppSDKSelfContained=true`, distributed as a conventional installer and
updated via Velopack.**

```
  +------------------------------------------+
  |  Conventional install in Program Files   |
  |  normal .exe, normal debugging           |
  |                                          |
  |  + sparse package manifest               |
  |    -> grants package identity            |
  |    -> notifications, StartupTask,        |
  |       protocol activation all work       |
  |                                          |
  |  + WindowsAppSDKSelfContained            |
  |    -> no runtime prerequisite            |
  |                                          |
  |  + Velopack                              |
  |    -> delta updates that actually work   |
  +------------------------------------------+
```

---

## Rationale

**Sparse packaging is the only option that gets both halves.** It grants
package identity while keeping a normal installation location, normal file
system access, and a normal debugging experience. Full MSIX would give identity
at the cost of all three; unpackaged would give the developer experience while
silently removing three user-facing features.

**Self-contained Windows App SDK removes a prerequisite.** Without it, users
must have the correct Windows App SDK runtime installed, which is a support
burden and an install-time failure mode for a small utility. The cost is
distribution size, which is an acceptable trade for a one-time download against
a recurring class of "it will not start" reports.

**Velopack rather than App Installer.** With `ms-appinstaller:` disabled by
default, MSIX auto-update outside the Store requires the user to download and
run a package each time — which is not auto-update. Velopack provides delta
updates and a conventional update flow, and it does not conflict with sparse
packaging.

**Not Store-only.** The Store would handle identity, distribution and updates
in one step, and remains attractive as an *additional* channel. But as the sole
channel it requires certification for every release, constrains the release
cadence, and sits awkwardly with a local-first tool that some users will want
to install without a Microsoft account (ADR-002).

---

## Consequences

### Positive

- notifications, run-at-login and `noto://` protocol activation all work
- conventional install location; users can find and inspect the application
- normal debugging, which matters for a project relying on rapid iteration
- no runtime prerequisite
- working delta auto-update

### Negative

- **sparse packaging is less commonly used**, so there is less community
  material when something goes wrong
- a signing certificate is required — sparse packages must be signed. This is a
  real cost and a release-process prerequisite, not a detail.
- two mechanisms to understand (installer + sparse manifest)
- Velopack is an additional dependency in the release path

### Release prerequisites

Before the first public release:

- a code-signing certificate must be obtained and the signing process
  documented
- the installer must be tested on a clean Windows 10 22H2 and Windows 11 24H2
  machine
- the upgrade path must be tested from a previous version, **with an existing
  notes database present** — an update that loses user data is the worst
  possible first impression
- uninstall must be verified to leave user data intact by default, and to ask
  before removing it

---

## Alternatives rejected

| Option | Why |
| ------ | --- |
| **Unpackaged (A)** | Silently loses notifications, StartupTask and protocol activation. The registry `Run` key could substitute for startup, but the other two have no substitute. |
| **Full MSIX (B)** | Sandboxed install, harder debugging, and its distribution advantage evaporated when `ms-appinstaller:` was disabled by default. |
| **Store only (D)** | Certification on every release, constrained cadence, and a poor fit for a local-first tool. Attractive as a **secondary** channel later. |

---

### Where packaging stands

> **Added 2026-10-12 by run at login (#94, ADR-015).** The decision above is
> unchanged; this records that it is not built yet.

Noto ships unpackaged today (`WindowsPackageType=None`): there is no sparse
manifest, installer, updater or certificate. Signing (#5): see *Signing* below.
Until the sparse package lands, run at login uses the registry `Run` key this
ADR names as the substitute (ADR-015). The packaging slice owns moving it to
`StartupTask` and removing the `Run` value; uninstall must remove it too.

### Single instance (A17)

The single-instance mechanism (ADR-013) — a named mutex and a named pipe keyed
by the user and the data root — depends on neither package identity nor
`AppInstance`, so it works unchanged unpackaged and as a sparse package. When
the sparse package lands, the A17 runtime harness is re-run on the packaged
build.

### Signing (#5): what it costs and what an unsigned build does

> **Added 2026-10-13 by the code-signing research (#5).** The decision above is
> unchanged: the sparse package still has to be signed. This records the
> options, prices and lead times found, and the recommendation. Prices are as
> published on the dates checked; they change, so re-check before buying.

**What must be signed, and why.**
- **The identity (sparse) package.** Microsoft: it "must be signed with a
  certificate that is trusted on the target computer". A self-signed
  certificate registers only on a machine where its public certificate was
  imported into *Trusted People*, so it is for development only. The
  manifest's `Publisher` must equal the certificate's Subject.
- **`Noto.exe`, its DLLs and the installer.** Smart App Control (Windows 11)
  checks the signature of every module, DLLs included, and blocks unsigned
  code it cannot vouch for. Signed code can still be blocked until it has
  reputation.

**SmartScreen gives no new file instant trust any more.** Since 2024, EV
certificates no longer bypass SmartScreen. Every option below starts with
warnings, and builds reputation by signing release after release with the
same identity. Reputation is per file hash and publisher, so each new build
starts low.

| Option | Who can use it | Cost (checked 2026-10-13) | Lead time | Notes |
| ------ | -------------- | ------------------------- | --------- | ----- |
| **Azure Artifact Signing** (formerly Trusted Signing) | Organizations in the USA, Canada, EU, UK; **individuals in the USA and Canada only** | ~$9.99/month (Basic, 5,000 signatures) | identity validation: a few business days | No hardware token; signs from CI; Microsoft's recommendation outside the Store. Supports MSIX |
| **OV / IV certificate** from a public CA | Anyone, worldwide; IV is the individual form | Microsoft quotes $150–300/year for OV. SSL.com's own price for IV: $129/year (1-year term), plus key storage: a YubiKey token $379, or an eSigner cloud subscription | 3–5 days standard (SSL.com) | Since June 2023 the key must be on a hardware token or HSM. **Since 2026-03-01 a certificate lasts at most 460 days** (CA/B Forum CSC-31), so a multi-year purchase means re-issues |
| **EV certificate** | Organizations | $400+/year (Microsoft) | longer validation | **Not recommended**: same SmartScreen treatment as OV since 2024 |
| **SignPath Foundation** | Open-source projects with an OSI licence and no proprietary parts; Noto is MIT | Free | application and review | **The certificate names SignPath Foundation as the publisher, not Noto.** The project must already be released, every signing is approved by hand, roles and MFA are required, and the download page must carry a code-signing policy. MSIX support for the free tier is not confirmed by the Foundation's own documents |
| **Microsoft Store (MSIX)** | Anyone | Free: the Store re-signs | certification per release | Gives no SmartScreen warnings, but it is a different channel. This ADR rejected Store-only; it stays a possible secondary channel |
| **Self-signed** | Development only | Free | none | Registers only where the certificate was imported; public users get a strong block |

**The unsigned fallback, measured against what Noto is today.**
- **Unpackaged (today).** An unsigned `Noto.exe` runs.
  - Downloaded from the internet, it gets SmartScreen's *Windows protected
    your PC*; a user can go on with *More info → Run anyway*.
  - On a machine with Smart App Control on, it is blocked, and there is no
    per-app exception short of turning Smart App Control off.
  - A build the user compiles, or copies without the internet mark, gets
    neither warning.
- **Sparse package.** It **cannot be registered on an end user's machine
  unsigned**, so the features it unlocks (`StartupTask`, notifications,
  protocol activation) do not exist in an unsigned build.
- **Conclusion.** Dogfooding with unsigned, unpackaged builds is viable for the
  author and for testers willing to click through SmartScreen on a machine
  without Smart App Control. A public pre-release is not: some Windows 11
  machines will refuse to run it.

**Recommendation.**
1. **Development and dogfooding:** stay unsigned and unpackaged. Use a
   self-signed certificate only on the development machine, when the sparse
   package is being built.
2. **The first public release:** Azure Artifact Signing if the publisher is
   eligible: an organization in the USA, Canada, EU or UK, or an individual in
   the USA or Canada. Otherwise an **IV/OV certificate with cloud or token key
   storage** (for example SSL.com IV, $129/year plus key storage).
   **SignPath Foundation** only if Noto accepts SignPath Foundation, not Noto,
   as the publisher every user sees.
3. **Do not buy EV** for SmartScreen.
4. **Budget the lead time:** days for identity validation. Plan for re-issue
   every 460 days at most, and for SmartScreen warnings on early releases
   whatever the option.

**What the owner still decides:** the publisher's identity, an individual or an
organization and in which country. That alone picks between options 2a and 2b
above. Nothing is bought or built by this record.

Sources, read 2026-10-13:
- Microsoft Learn, [Code signing options for Windows app developers](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options) (updated 2026-08-29);
- Microsoft Learn, [Grant package identity by packaging with external location](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/grant-identity-to-nonpackaged-apps) (updated 2026-10-03);
- Microsoft Support, [Smart App Control FAQ](https://support.microsoft.com/en-us/windows/security/threat-malware-protection/smart-app-control-frequently-asked-questions);
- [SSL.com IV code signing](https://www.ssl.com/products/software-integrity/code-signing/iv/);
- [SignPath Foundation terms](https://signpath.org/terms);
- DigiCert, [code signing validity change (CSC-31)](https://www.digicert.com/blog/understanding-the-new-code-signing-certificate-validity-change).

## Future reconsideration criteria

Revisit if:

- sparse package support is deprecated or proves unreliable in practice
- `ms-appinstaller:` is re-enabled with adequate safeguards, restoring MSIX web
  install
- the Store becomes a meaningful distribution channel for Noto — as an
  **addition**, not a replacement
- the signing certificate cost or process proves prohibitive, which would force
  a reassessment of the whole packaging approach

The signing requirement is the most likely thing to force a rethink, and it
should be confirmed early rather than discovered at release time.
