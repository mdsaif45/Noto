# Noto's icon

**The mark: "the edge drawer"** (#95).

- **The tile is the screen.** It is lit by a warm sunrise gradient (amber →
  coral → rose) and framed by an even bezel of that colour.
- **A white drawer is docked against its right edge,** and a slim pill-shaped
  pull tab on the drawer's leading edge reaches into the colour. That is the
  gesture of pulling Noto out from the edge of the screen, which is the
  product's defining idea.
- **Nothing reads as a document:** no page, no folded corner, no lines, no
  letter.

At 16 and 20 px the mark is a warm tile with a white bar and tab on its right.
The bezel keeps the tile's outline whole on light and dark taskbars alike.

![Noto icon](noto-icon.svg)

## Where it is used

| Surface | How |
|---|---|
| The executable (Explorer, desktop, Start) | `ApplicationIcon` in `Noto.Windows.csproj`, embedded as resource 32512 |
| The window: taskbar button, Alt+Tab | `AppWindow.SetIcon` with `Assets\Noto.ico`, copied next to the executable |
| The notification area | `TrayIcon` loads the executable's resource 32512 at the shell's small-icon size |

## Changing it

`tools/branding/New-NotoIcon.ps1` is the source of truth. Edit it, never the
outputs, and run it to regenerate both:

```
pwsh -NoProfile -File tools/branding/New-NotoIcon.ps1 -Preview $env:TEMP\noto-icon-preview.png
```

It writes:
- **`src/Noto.Windows/Assets/Noto.ico`:** 16, 20, 24, 32, 40, 48, 64, 96, 128
  and 256 px, as 32-bit PNG entries with alpha;
- **`docs/branding/noto-icon.svg`:** the 256 px master, for design tools.

Every size is drawn on its own pixel grid rather than scaled down from one
image, so the drawer's edge and the tab land on whole pixels at 16 and 20 px.
The preview shows the small shell sizes at 8× and actual size, on light and
dark surfaces. Look at it before committing a change.

**Windows caches icons by executable path.** A build that runs from a path
where an older icon was seen may keep showing the old one until Explorer's
cache refreshes. A fresh path shows the new icon at once; an installer that
updates in place should notify the shell.
