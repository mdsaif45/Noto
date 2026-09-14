# Security Policy

Noto is a local-first desktop application. Your notes live on your machine, and
we treat them as sensitive by default.

## Supported versions

Noto is in early development. Only the latest release receives security fixes.

| Version | Supported |
| ------- | --------- |
| latest  | ✅        |
| older   | ❌        |

## Reporting a vulnerability

**Do not open a public issue for a security vulnerability.**

Report it privately using
[GitHub private vulnerability reporting](https://github.com/mdsaif45/Noto/security/advisories/new).

Please include:

- a description of the vulnerability and its impact
- steps to reproduce, or a proof of concept
- the Noto version and Windows version affected
- any suggested mitigation

### What to expect

- **Acknowledgement** within 7 days.
- **Initial assessment** within 14 days.
- **Fix or mitigation plan** communicated once the issue is understood.
- **Credit** in the release notes, unless you prefer to remain anonymous.

Noto is a personal open-source project, not a company with an on-call security
team. Response times are best effort.

## Scope

In scope:

- local data exposure or corruption
- path traversal or unsafe file handling
- unsafe handling of clipboard content, URLs or attachments
- arbitrary code or command execution
- privilege escalation
- bypass of note locking
- build or release integrity

Out of scope:

- vulnerabilities requiring an attacker who already has full control of the
  user's Windows account — Noto does not defend against a compromised OS user
- social engineering
- issues in third-party dependencies that do not affect Noto (report upstream)
- missing hardening that has no demonstrated impact

## Security principles

Noto commits to the following:

- **Local-first.** Core functionality requires no account, no network and no
  remote backend.
- **No telemetry by default.** Any future telemetry would be opt-in and
  documented.
- **No arbitrary execution.** Noto will not execute commands or code embedded in
  note content without an explicit, documented permission model.
- **Explicit network access.** Any feature that touches the network is opt-in
  and clearly described.
