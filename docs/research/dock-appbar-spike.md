# Dock spike: does Windows give back an app bar's space? (#153)

**Date:** 2026-11-13. **Machine:** Windows 11 Home 10.0.26300, one display, 1920 × 1080 at 100%.
**Tool:** `tools/validation/Invoke-DockSpike.ps1` with `helpers/appbar-spike.ps1`: a window of the spike's own, not Noto.

## Why

[ADR-007 §4](../decisions/ADR-007-window-presentation.md) rejected `SHAppBarMessage`, the API that reserves a screen
edge so maximised windows sit beside a docked bar. The risk named: if the docked process crashes, the reserved strip may
stay until the user signs out. #153 asks whether it does, before any *Dock* mode is designed.

## What was done

A window docks to the primary display's right edge as an application desktop toolbar (`ABM_NEW`, `ABM_QUERYPOS`,
`ABM_SETPOS`), reserving 300 px. The work area (`SystemParametersInfo(SPI_GETWORKAREA)`, what every maximised window
fits into) then reads `0,0,1620,1032` instead of `0,0,1920,1032`. The process then ends one of four ways, and the work
area is read every 100 ms until it is whole again.

| How the docked process ended | Space back after | Note |
|---|---|---|
| `ABM_REMOVE`, then exit (the control) | 3.0 s | the spike waits 3 s before removing |
| Killed from outside (`Stop-Process -Force`) | **0.1 s** | a crash nothing in the process can catch |
| `Environment.FailFast` while docked | **12.3 s** | Windows Error Reporting holds the crashed process; the space comes back once it is gone |
| Exit without `ABM_REMOVE` | **about 2.4 s** (5.4 s, less the 3 s wait) | an update, or a bug, that misses the clean path |

The work area after the run was the same as before it (`0,0,1920,1032`).

## Result

On this Windows 11 build, with one display, **Windows gives the space back by itself** however the docked process ends.
The sign-out risk ADR-007 §4 named did not happen. A crash leaves the strip reserved only while Windows Error Reporting
holds the process (about 12 s here).

## Not done

- **Two monitors.** This machine has one display; #153's two-monitor half (a bar on the second display, a display
  disconnected while docked) is still to run.
- **Windows 10.** Not available here (#32).
- **Noto itself.** The spike proves the platform's behaviour, not a design: *Dock* as a Noto mode is #203.

## What it means for ADR-007

ADR-007 §4 is not amended yet: the two-monitor half comes first, as #153 asks. If it agrees, *Dock* can be offered as a
mode beside *Keep Noto open*, off by default (#203).
