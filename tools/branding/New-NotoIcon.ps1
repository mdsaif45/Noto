#Requires -Version 7.4
<#
.SYNOPSIS
  Builds Noto's icon (#95): the multi-size Windows .ico, the vector master (.svg), and a preview sheet.

.DESCRIPTION
  The mark is "the edge drawer". The tile is the screen, lit by a warm sunrise gradient and framed by an even bezel
  of that colour; the white drawer is docked against its right edge, and a pill-shaped pull tab on the drawer's
  leading edge reaches into the colour - the gesture of pulling Noto out from the edge of the screen. The bezel keeps
  the tile's outline whole on light and dark surfaces alike. No page, no fold, no text: nothing that reads as a
  document.

  This script is the source of truth. Every size is drawn on its own pixel grid - the drawer's edge and the tab
  snapped to whole pixels - at 8x and reduced once, rather than one large image scaled down, so 16 and 20 px stay
  crisp. It writes:

    src/Noto.Windows/Assets/Noto.ico     16 20 24 32 40 48 64 96 128 256 px, 32-bit with alpha (PNG entries)
    docs/branding/noto-icon.svg           the 256 px master as vector, for design tools
    <-Preview path>                        the small shell sizes at 8x and 1x, and the large sizes, on light and dark

  Windows only: System.Drawing (GDI+), which ships with PowerShell 7 on Windows. No other dependency.

.EXAMPLE
  pwsh -NoProfile -File tools/branding/New-NotoIcon.ps1 [-Preview $env:TEMP\noto-icon-preview.png]
#>
param([string] $Preview)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$Sizes = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256

# The palette. Hex strings are shared with the SVG master.
$Hex = @{ Amber = 'FFB03B'; Coral = 'FF6A4D'; Rose = 'E83A6E'; Paper = 'FFFAF5'; Shade = '5A0F2E' }
function Color([string] $Name, [int] $Alpha = 255) { $h = $Hex[$Name]; [System.Drawing.Color]::FromArgb($Alpha, [Convert]::ToInt32($h.Substring(0, 2), 16), [Convert]::ToInt32($h.Substring(2, 2), 16), [Convert]::ToInt32($h.Substring(4, 2), 16)) }

function Geometry([int] $Size) {
    <# Whole-pixel geometry for one size: the tile, where the drawer begins, and the tab. #>
    $margin = [Math]::Max(1, [Math]::Round($Size * 0.0625))
    $side = $Size - 2 * $margin
    $tabW = [Math]::Max(2, [Math]::Round($Size * 0.085))
    $tabH = [Math]::Max(4, [Math]::Round($Size * 0.23))
    if (($tabH % 2) -ne ($Size % 2)) { $tabH++ }                       # centred on the pixel grid
    $bezel = [Math]::Max(2, [Math]::Round($side * 0.14))
    [pscustomobject]@{
        Size = $Size; Margin = $margin; Side = $side; Radius = $side * 0.235
        Edge = [Math]::Round($Size * 0.58)                               # where the drawer's leading edge sits
        Right = $Size - $margin - $bezel; Top = $margin + $bezel; Bottom = $Size - $margin - $bezel
        Corner = [Math]::Max(1, $side * 0.07)
        TabW = $tabW; TabH = $tabH; TabY = ($Size - $tabH) / 2
        Shadow = [Math]::Max(1, $Size * 0.045)
    }
}

function Rounded([double] $X, [double] $Y, [double] $W, [double] $H, [double] $R) {
    $p = [System.Drawing.Drawing2D.GraphicsPath]::new(); $dia = 2 * $R
    $p.AddArc($X, $Y, $dia, $dia, 180, 90); $p.AddArc($X + $W - $dia, $Y, $dia, $dia, 270, 90)
    $p.AddArc($X + $W - $dia, $Y + $H - $dia, $dia, $dia, 0, 90); $p.AddArc($X, $Y + $H - $dia, $dia, $dia, 90, 90)
    $p.CloseFigure(); $p
}

function Drawer([psobject] $Geo, [double] $Scale) {
    <# The drawer, docked inside the right bezel, and its pull tab, as one path. #>
    $p = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $p.AddPath((Rounded ($Geo.Edge * $Scale) ($Geo.Top * $Scale) (($Geo.Right - $Geo.Edge) * $Scale) (($Geo.Bottom - $Geo.Top) * $Scale) ($Geo.Corner * $Scale)), $false)
    $tab = Rounded (($Geo.Edge - $Geo.TabW) * $Scale) ($Geo.TabY * $Scale) (($Geo.TabW + $Geo.TabW) * $Scale) ($Geo.TabH * $Scale) ($Geo.TabW * $Scale)
    $p.AddPath($tab, $false); $p.FillMode = 'Winding'
    $p
}

function Render([int] $Size) {
    $geo = Geometry $Size; $k = 8; $big = [System.Drawing.Bitmap]::new($Size * $k, $Size * $k, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $gx = [System.Drawing.Graphics]::FromImage($big)
    $gx.SmoothingMode = 'AntiAlias'; $gx.PixelOffsetMode = 'HighQuality'; $gx.Clear([System.Drawing.Color]::Transparent)

    $m = $geo.Margin * $k; $side = $geo.Side * $k
    $tile = Rounded $m $m $side $side ($geo.Radius * $k)
    $grad = [System.Drawing.Drawing2D.LinearGradientBrush]::new([System.Drawing.PointF]::new($m, $m), [System.Drawing.PointF]::new($m + $side, $m + $side), (Color Amber), (Color Rose))
    $blend = [System.Drawing.Drawing2D.ColorBlend]::new(3); $blend.Colors = @((Color Amber), (Color Coral), (Color Rose)); $blend.Positions = @(0.0, 0.5, 1.0)
    $grad.InterpolationColors = $blend
    $gx.FillPath($grad, $tile)
    $gx.SetClip($tile)

    # the slide: the drawer casts a soft shadow leftwards onto the screen
    $steps = 5
    for ($i = $steps; $i -ge 1; $i--) {
        $sp = Drawer $geo $k; $mx = [System.Drawing.Drawing2D.Matrix]::new(); $mx.Translate(-$geo.Shadow * $k * $i / $steps, 0); $sp.Transform($mx)
        $gx.FillPath([System.Drawing.SolidBrush]::new((Color Shade ([int](26 - 3 * $i)))), $sp); $sp.Dispose()
    }

    # the drawer, its tab reaching into the colour
    $gx.FillPath([System.Drawing.SolidBrush]::new((Color Paper)), (Drawer $geo $k))
    $gx.ResetClip(); $gx.Dispose()

    $small = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $sx = [System.Drawing.Graphics]::FromImage($small)
    $sx.InterpolationMode = 'HighQualityBicubic'; $sx.PixelOffsetMode = 'HighQuality'; $sx.CompositingQuality = 'HighQuality'
    $sx.DrawImage($big, [System.Drawing.Rectangle]::new(0, 0, $Size, $Size))
    $sx.Dispose(); $big.Dispose()
    $small
}

# ---------------------------------------------------------------- the .ico: one PNG entry per size

$images = foreach ($s in $Sizes) { $bmp = Render $s; $ms = [IO.MemoryStream]::new(); $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); [pscustomobject]@{ Size = $s; Bmp = $bmp; Png = $ms.ToArray() } }
$stream = [IO.MemoryStream]::new(); $writer = [IO.BinaryWriter]::new($stream)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($i in $images) {
    $dim = if ($i.Size -ge 256) { 0 } else { $i.Size }
    $writer.Write([byte]$dim); $writer.Write([byte]$dim); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$i.Png.Length); $writer.Write([uint32]$offset)
    $offset += $i.Png.Length
}
foreach ($i in $images) { $writer.Write($i.Png) }
$writer.Flush()
$icoPath = Join-Path $Root 'src\Noto.Windows\Assets\Noto.ico'
New-Item -ItemType Directory -Force (Split-Path $icoPath) | Out-Null
[IO.File]::WriteAllBytes($icoPath, $stream.ToArray())

# ---------------------------------------------------------------- the vector master, 256 px

$geo = Geometry 256; $m = $geo.Margin; $sd = $geo.Side; $r = [Math]::Round($geo.Radius, 2); $e = $geo.Edge
$svg = @"
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" width="256" height="256">
  <!-- Noto, "the edge drawer". Generated by tools/branding/New-NotoIcon.ps1: edit the script, not this file. -->
  <defs>
    <linearGradient id="sunrise" x1="$m" y1="$m" x2="$($m + $sd)" y2="$($m + $sd)" gradientUnits="userSpaceOnUse">
      <stop offset="0" stop-color="#$($Hex.Amber)"/><stop offset="0.5" stop-color="#$($Hex.Coral)"/><stop offset="1" stop-color="#$($Hex.Rose)"/>
    </linearGradient>
    <clipPath id="tile"><rect x="$m" y="$m" width="$sd" height="$sd" rx="$r"/></clipPath>
    <filter id="slide" x="-20%" y="0" width="140%" height="100%"><feGaussianBlur stdDeviation="$([Math]::Round($geo.Shadow / 2, 2))"/></filter>
    <g id="drawer"><rect x="$e" y="$($geo.Top)" width="$($geo.Right - $e)" height="$($geo.Bottom - $geo.Top)" rx="$([Math]::Round($geo.Corner, 2))"/><rect x="$($e - $geo.TabW)" y="$($geo.TabY)" width="$(2 * $geo.TabW)" height="$($geo.TabH)" rx="$($geo.TabW)"/></g>
  </defs>
  <rect x="$m" y="$m" width="$sd" height="$sd" rx="$r" fill="url(#sunrise)"/>
  <g clip-path="url(#tile)">
    <use href="#drawer" transform="translate(-$([Math]::Round($geo.Shadow, 2)) 0)" fill="#$($Hex.Shade)" fill-opacity="0.18" filter="url(#slide)"/>
    <use href="#drawer" fill="#$($Hex.Paper)"/>
  </g>
</svg>
"@
$svgPath = Join-Path $Root 'docs\branding\noto-icon.svg'
New-Item -ItemType Directory -Force (Split-Path $svgPath) | Out-Null
[IO.File]::WriteAllText($svgPath, ($svg -replace "`r`n", "`n") + "`n", [Text.UTF8Encoding]::new($false))   # LF, as the repository stores it

# ---------------------------------------------------------------- the preview

if ($Preview) {
    $sheet = [System.Drawing.Bitmap]::new(1900, 1500)
    $px = [System.Drawing.Graphics]::FromImage($sheet); $px.InterpolationMode = 'NearestNeighbor'; $px.PixelOffsetMode = 'Half'
    $surfaces = @([System.Drawing.Color]::FromArgb(255, 0xF3, 0xF3, 0xF3), [System.Drawing.Color]::FromArgb(255, 0x20, 0x20, 0x20))
    for ($band = 0; $band -lt 2; $band++) {
        $y0 = $band * 750
        $px.FillRectangle([System.Drawing.SolidBrush]::new($surfaces[$band]), 0, $y0, 1900, 750)
        $x = 20
        foreach ($s in 16, 20, 24, 32) {
            $i = $images | Where-Object Size -eq $s
            $px.DrawImage($i.Bmp, [System.Drawing.Rectangle]::new($x, $y0 + 20, $s * 8, $s * 8))
            $px.DrawImage($i.Bmp, [System.Drawing.Rectangle]::new($x, $y0 + 30 + $s * 8, $s, $s))
            $x += $s * 8 + 30
        }
        $x = 20
        foreach ($s in 48, 64, 128, 256) { $i = $images | Where-Object Size -eq $s; $px.DrawImage($i.Bmp, [System.Drawing.Rectangle]::new($x, $y0 + 330, $s, $s)); $x += $s + 30 }
    }
    $px.Dispose(); $sheet.Save($Preview, [System.Drawing.Imaging.ImageFormat]::Png)
}
foreach ($i in $images) { $i.Bmp.Dispose() }
"ico: $icoPath ($((Get-Item $icoPath).Length) bytes, sizes $($Sizes -join ' '))"
"svg: $svgPath"
if ($Preview) { "preview: $Preview" }
