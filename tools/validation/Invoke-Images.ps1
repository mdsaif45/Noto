#Requires -Version 7.4
<#
.SYNOPSIS
  Images (ADR-019 §4): kept beside the notes, written as ![name](attachments/<ULID>.<type>), drawn below their line in
  the editor and in the cards, sized to fit and never enlarged; pasted, dropped and copied in; a missing or unreadable
  file says so; files no note refers to are swept after a week.

.DESCRIPTION
  A fresh isolated data root with one folder of seeded notes and image files drawn by this script in pure red, so the
  pictures are found in screenshots by colour (cards.png, editor.png and pasted.png are kept in the run). The clipboard
  is used for cases 9-11 and restored. A file drop (case 12) is a real OLE drag (helpers/drop-files.ps1): the button is
  pressed on a small window of the helper's own, shown over Noto without taking the foreground, and released over the
  editor's line. Both points are checked to be Noto's window first, and Noto is checked to be still in front after.
  Keys reach Noto only while it is verifiably the foreground window.

     1  at start, an unreferenced file older than a week is swept; a newer one, and an old referenced one, are kept
     2  a card draws its image below its caption: red, shape kept, at most 160 tall
     3  a card says when an image's file is missing or unreadable
     4  the editor draws the image below its line, in space it reserves: the next line starts below the picture
     5  a tall image is drawn at most 320 tall, its shape kept
     6  a small image is never enlarged
     7  the editor says when an image's file is missing or unreadable
     8  the picture is not in the accessibility tree; the caption, the source, is
     9  a pasted picture is kept as PNG and written on its own line; one undo removes it
    10  a clipboard with text and a picture pastes the text
    11  a copied image file is kept as a copy and shown; a copied text file is linked
    12  an image file dropped on the editor is kept and written where it was dropped
    13  a picture removed and older than a week is swept at the next start
    14  cost: a note with five 1200x800 images, Noto's own load, first-frame and images-drawn traces

.EXAMPLE
  pwsh -NoProfile -File tools/validation/Invoke-Images.ps1 -NotoExe <path>\Noto.exe
#>
param([Parameter(Mandatory)] [string] $NotoExe)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'NotoValidation.psm1') -Force
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -AssemblyName System.Drawing
if (-not ('NotoVal.Instance' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'helpers/single-instance.cs') }
if (-not ('NotoVal.ImageCapture' -as [type])) {
    Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
namespace NotoVal {
    public static class ImageCapture {
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        // PW_RENDERFULLCONTENT (2): the composited content, which is what a WinUI window draws.
        [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
        [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
    }
}
'@
}
$NotoExe = (Resolve-Path $NotoExe).Path

$run = New-ValidationRun 'images'
Write-Host "run $($run.Id)  exe $NotoExe"

$AE = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
$CT = [System.Windows.Automation.ControlType]
$TP = [System.Windows.Automation.TextPattern]
$FolderId = '01J0000000000000000000F061'
# ULIDs: Crockford base32, no I, L, O or U.
$Red = '01J00000000000000000000A01'; $Tall = '01J00000000000000000000A02'; $Small = '01J00000000000000000000A03'
$Gone = '01J00000000000000000000A04'; $Broken = '01J00000000000000000000A05'
$OldLoose = '01J00000000000000000000A06'; $NewLoose = '01J00000000000000000000A07'
$Five = 1..5 | ForEach-Object { "01J00000000000000000000B0$_" }
# The note pasted and dropped into is the stack's first card: dragged over near the stack's edge, the stack scrolls
# (measured: 19 px during case 12 when it was the last card), and the drop would land a line off.
$Notes = [ordered]@{
    '01J00000000000000000000N67' = "Paste here`nfirst line`ndrop here`nlast line"
    '01J00000000000000000000N61' = "Red`n![red square](attachments/$Red.png)`nafter the picture"
    '01J00000000000000000000N62' = "Tall`n![tall](attachments/$Tall.png)"
    '01J00000000000000000000N63' = "Small`n![dot](attachments/$Small.png)"
    '01J00000000000000000000N64' = "Gone`n![gone](attachments/$Gone.png)"
    '01J00000000000000000000N65' = "Broken`n![broken](attachments/$Broken.png)"
    '01J00000000000000000000N66' = "Five`n" + (($Five | ForEach-Object { "![photo](attachments/$_.png)" }) -join "`n")
}

function Sql($Data, [string] $Text, [hashtable] $Parameters = @{}, [switch] $Scalar) {
    $db = Join-Path $Data 'noto.db'; Assert-UnderRun $run $db
    Initialize-NotoSqlite $NotoExe
    $c = [Microsoft.Data.Sqlite.SqliteConnection]::new("Data Source=$db"); $c.Open()
    try {
        $cmd = $c.CreateCommand(); $cmd.CommandText = $Text
        foreach ($k in $Parameters.Keys) { [void]$cmd.Parameters.AddWithValue($k, $Parameters[$k]) }
        if ($Scalar) { $cmd.ExecuteScalar() } else { $cmd.ExecuteNonQuery() }
    }
    finally { $c.Close(); [Microsoft.Data.Sqlite.SqliteConnection]::ClearAllPools() }
}
function By-Id([string] $Id) { $AE::FromHandle($script:noto.Hwnd).FindFirst($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::AutomationIdProperty, $Id)) }
function Find-ById([string] $Id, [int] $Seconds = 10) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) { $e = By-Id $Id; if ($e -and -not $e.Current.IsOffscreen) { return $e }; Start-Sleep -Milliseconds 200 }
    throw "no visible element '$Id' within ${Seconds}s"
}
function Editor { Find-ById 'NoteEditor' }
function Editor-Value { (Editor).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value }
function Range-Of([string] $Needle) { (Editor).GetCurrentPattern($TP::Pattern).DocumentRange.FindText($Needle, $false, $false) }
function Notice { $n = By-Id 'NoteNoticeText'; if ($n -and -not $n.Current.IsOffscreen) { $n.Current.Name } else { '' } }
function Send-ToNoto([int[]] $Mods, [int] $Key, [int] $Wait = 500) {
    if ([NotoVal.Win32]::GetForegroundWindow() -ne $script:noto.Hwnd) { throw 'refusing to send keys: Noto is not the foreground window' }
    $rev = [int[]]$Mods.Clone(); [array]::Reverse($rev)
    [NotoVal.Win32]::Keys(@(@($Mods | ForEach-Object { [NotoVal.Win32]::Vk($_, $false) }) + @([NotoVal.Win32]::Vk($Key, $false), [NotoVal.Win32]::Vk($Key, $true)) + @($rev | ForEach-Object { [NotoVal.Win32]::Vk($_, $true) })))
    Start-Sleep -Milliseconds $Wait
}
function Bring-Forward {
    for ($attempt = 1; $attempt -le 4; $attempt++) {
        $null = Set-ForegroundByKeyboard $script:target
        if ([NotoVal.Win32]::GetForegroundWindow() -eq $script:target.Hwnd) { Send-ActivationChord -AllowedForeground $script:target.Hwnd }
        if ([NotoVal.Win32]::GetForegroundWindow() -eq $script:noto.Hwnd) { return }
        Start-Sleep -Milliseconds 1000
    }
    throw 'the hotkey did not bring Noto forward'
}
function Traces([string] $Pattern) { @($script:log.Lines($script:noto.Pid) | Where-Object { $_ -match $Pattern }) }
function Ms([string] $Line) { if ($Line -match 'ms=([0-9.]+)') { [double]$Matches[1] } else { $null } }

function New-RedPng([string] $Path, [int] $Width, [int] $Height) {
    Assert-UnderRun $run $Path
    $bmp = New-Object System.Drawing.Bitmap $Width, $Height
    try {
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        try { $g.Clear([System.Drawing.Color]::FromArgb(255, 255, 0, 0)) } finally { $g.Dispose() }
        $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $bmp.Dispose() }
}
function Attachment([string] $Name) { Join-Path $script:data "attachments\$Name.png" }
function Attachments { @(Get-ChildItem (Join-Path $script:data 'attachments') -File | ForEach-Object Name) }
function Png-Size([string] $Path) {
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $signature = [BitConverter]::ToString($bytes, 0, 8) -eq '89-50-4E-47-0D-0A-1A-0A'
    $img = [System.Drawing.Image]::FromFile($Path); try { "$signature $($img.Width)x$($img.Height)" } finally { $img.Dispose() }
}

function Scale { [NotoVal.ImageCapture]::GetDpiForWindow($script:noto.Hwnd) / 96.0 }
function Red-Box($Element) {
    <# The red pixels inside the element's on-screen rectangle, from a capture of Noto's own window: their bounding box in screen pixels. #>
    $w = New-Object NotoVal.ImageCapture+RECT; [void][NotoVal.ImageCapture]::GetWindowRect($script:noto.Hwnd, [ref]$w)
    $bmp = New-Object System.Drawing.Bitmap ($w.R - $w.L), ($w.B - $w.T)
    $g = [System.Drawing.Graphics]::FromImage($bmp); $dc = $g.GetHdc()
    try { [void][NotoVal.ImageCapture]::PrintWindow($script:noto.Hwnd, $dc, 2) } finally { $g.ReleaseHdc($dc); $g.Dispose() }
    try {
        $r = $Element.Current.BoundingRectangle
        $x0 = [Math]::Max(0, [int]$r.X - $w.L); $y0 = [Math]::Max(0, [int]$r.Y - $w.T)
        $x1 = [Math]::Min($bmp.Width, [int]($r.X + $r.Width) - $w.L); $y1 = [Math]::Min($bmp.Height, [int]($r.Y + $r.Height) - $w.T)
        $minX = [int]::MaxValue; $minY = [int]::MaxValue; $maxX = -1; $maxY = -1
        $data = $bmp.LockBits([System.Drawing.Rectangle]::new(0, 0, $bmp.Width, $bmp.Height), 'ReadOnly', 'Format32bppArgb')
        try {
            $stride = $data.Stride; $buffer = [byte[]]::new($stride * $bmp.Height)
            [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $buffer, 0, $buffer.Length)
        }
        finally { $bmp.UnlockBits($data) }
        for ($y = $y0; $y -lt $y1; $y++) {
            $row = $y * $stride
            for ($x = $x0; $x -lt $x1; $x++) {
                $i = $row + 4 * $x
                if ($buffer[$i + 2] -gt 220 -and $buffer[$i + 1] -lt 40 -and $buffer[$i] -lt 40) {
                    if ($x -lt $minX) { $minX = $x }; if ($x -gt $maxX) { $maxX = $x }
                    if ($y -lt $minY) { $minY = $y }; if ($y -gt $maxY) { $maxY = $y }
                }
            }
        }
        if ($maxX -lt 0) { return $null }
        [pscustomobject]@{ X = $minX + $w.L; Y = $minY + $w.T; W = $maxX - $minX + 1; H = $maxY - $minY + 1 }
    }
    finally { $bmp.Dispose() }
}
function Shot([string] $Name) {
    <# Noto's window as drawn, saved in the run for review. #>
    $w = New-Object NotoVal.ImageCapture+RECT; [void][NotoVal.ImageCapture]::GetWindowRect($script:noto.Hwnd, [ref]$w)
    $bmp = New-Object System.Drawing.Bitmap ($w.R - $w.L), ($w.B - $w.T)
    $g = [System.Drawing.Graphics]::FromImage($bmp); $dc = $g.GetHdc()
    try { [void][NotoVal.ImageCapture]::PrintWindow($script:noto.Hwnd, $dc, 2) } finally { $g.ReleaseHdc($dc); $g.Dispose() }
    try { $bmp.Save((Join-Path $run.Root "$Name.png"), [System.Drawing.Imaging.ImageFormat]::Png) } finally { $bmp.Dispose() }
}
function Box-Text($Box) { if ($Box) { "$($Box.W)x$($Box.H)@($($Box.X),$($Box.Y))" } else { 'none' } }
function Raw-Find($Root, [string] $Name) {
    <# Searches the raw view (what Narrator skips is still there): whether an element named $Name is below $Root. #>
    $walker = [System.Windows.Automation.TreeWalker]::RawViewWalker
    $stack = [System.Collections.Generic.Stack[object]]::new(); $stack.Push($Root)
    while ($stack.Count -gt 0) {
        $e = $stack.Pop()
        if ($e.Current.Name -eq $Name) { return $e }
        for ($c = $walker.GetFirstChild($e); $c; $c = $walker.GetNextSibling($c)) { $stack.Push($c) }
    }
    $null
}

function Card([string] $Title) {
    $items = (Find-ById 'NoteList').FindAll($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::ListItem))
    $card = @($items | Where-Object { $_.Current.Name -eq $Title })[0]
    if (-not $card) { throw "no card '$Title' (cards: $(@($items | ForEach-Object { $_.Current.Name }) -join ' | '))" }
    try { $card.GetCurrentPattern([System.Windows.Automation.ScrollItemPattern]::Pattern).ScrollIntoView() } catch { }
    Start-Sleep -Milliseconds 700
    $card
}
function Edit-Note([string] $Title) {
    $card = Card $Title
    $card.SetFocus(); Start-Sleep -Milliseconds 300
    Send-ToNoto @() 0x0D 1500
    $null = Editor
}
function Leave-Editor { Send-ToNoto @() 0x1B 900 }
function Caret-End { Send-ToNoto @(0x11) 0x23 300 }

function Invoke-Sta([string] $Name, [string] $Body, [string[]] $Arguments) {
    <# Clipboard and OLE drag and drop are STA's: a Windows PowerShell script under the run. #>
    $file = Join-Path $run.Root "$Name.ps1"; Assert-UnderRun $run $file
    Set-Content $file $Body
    & powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File $file @Arguments

    # A clipboard just set by another process has not settled for the others yet: Noto, asked to paste in the same
    # instant, read the state from before the change (measured, with paste traces: no file, no text, no picture). A
    # person cannot change applications and paste that fast; the harness waits as long as one would.
    if ($Name -like 'set-*') { Start-Sleep -Milliseconds 500 }
}
$SetImage = 'Add-Type -AssemblyName System.Windows.Forms, System.Drawing; $b = New-Object System.Drawing.Bitmap ([int]$args[0]), ([int]$args[1]); $g = [System.Drawing.Graphics]::FromImage($b); $g.Clear([System.Drawing.Color]::Red); $g.Dispose(); $d = New-Object System.Windows.Forms.DataObject; $d.SetImage($b); if ($args.Count -gt 2) { $d.SetText($args[2]) }; [System.Windows.Forms.Clipboard]::SetDataObject($d, $true, 10, 100); if (-not [System.Windows.Forms.Clipboard]::ContainsImage()) { throw "the clipboard did not take the picture" }'
$SetFiles = 'Add-Type -AssemblyName System.Windows.Forms; $c = New-Object System.Collections.Specialized.StringCollection; foreach ($p in $args) { [void]$c.Add($p) }; $d = New-Object System.Windows.Forms.DataObject; $d.SetFileDropList($c); [System.Windows.Forms.Clipboard]::SetDataObject($d, $true, 10, 100); if (-not [System.Windows.Forms.Clipboard]::ContainsFileDropList()) { throw "the clipboard did not take the files" }'

$script:log = [NotoVal.DebugLog]::new()
try {
exit (Invoke-IsolatedRun $run -Title 'Images (ADR-019)' -Body {
    Assert-ChordFree $run
    $script:target = Start-TargetApp $run
    $script:data = Join-Path $run.Root 'data'; New-Item -ItemType Directory $script:data | Out-Null

    $first = Start-Noto $run -Exe $NotoExe -DataRoot $script:data
    if (-not (Stop-Noto $first)) { throw 'the seeding start did not exit cleanly' }
    $folderOfImages = Join-Path $script:data 'attachments'; New-Item -ItemType Directory $folderOfImages -Force | Out-Null
    New-RedPng (Attachment $Red) 400 200
    New-RedPng (Attachment $Tall) 600 1500
    New-RedPng (Attachment $Small) 40 20
    Set-Content (Attachment $Broken) 'not a picture at all'
    New-RedPng (Attachment $OldLoose) 10 10
    New-RedPng (Attachment $NewLoose) 10 10
    foreach ($id in $Five) { New-RedPng (Attachment $id) 1200 800 }
    $week = (Get-Date).AddDays(-8)
    (Get-Item (Attachment $OldLoose)).LastWriteTime = $week
    (Get-Item (Attachment $Red)).LastWriteTime = $week

    $now = [DateTimeOffset]::UtcNow.ToString('O')
    $null = Sql $script:data "INSERT INTO Folders (Id, Name, SortOrder, CreatedAt, UpdatedAt) VALUES (`$f, 'Pictures', 1, `$t, `$t)" @{ '$f' = $FolderId; '$t' = $now }
    $order = 1
    foreach ($id in $Notes.Keys) {
        $null = Sql $script:data "INSERT INTO Notes (Id, FolderId, Content, SortOrder, CreatedAt, UpdatedAt) VALUES (`$n, `$f, `$c, `$o, `$t, `$t)" @{ '$n' = $id; '$f' = $FolderId; '$c' = $Notes[$id]; '$o' = $order++; '$t' = $now }
    }

    $script:noto = Start-Noto $run -Exe $NotoExe -DataRoot $script:data
    Bring-Forward
    $folder = (Find-ById 'FolderList').FindFirst($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::ListItem))
    $folder.SetFocus(); Start-Sleep -Milliseconds 300
    Send-ToNoto @(0x11) 0x28 1500
    $s = Scale

    # 1 - the sweep at start
    $deadline = (Get-Date).AddSeconds(5)
    while ((Test-Path (Attachment $OldLoose)) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 200 }
    $swept = -not (Test-Path (Attachment $OldLoose)); $keptNew = Test-Path (Attachment $NewLoose); $keptReferenced = Test-Path (Attachment $Red)
    Add-Result $run '1  at start, an old unreferenced file is swept; a new one and an old referenced one are kept' ($swept -and $keptNew -and $keptReferenced) "swept=$swept keptNew=$keptNew keptReferenced=$keptReferenced"

    # 2 - a card draws its image
    $box = Red-Box (Card 'Red')
    $cardOk = $box -and $box.H -le [Math]::Ceiling(160 * $s) + 1 -and [Math]::Abs($box.W / $box.H - 2) -lt 0.06
    Shot 'cards'
    Add-Result $run '2  a card draws its image below its caption: shape kept, at most 160 tall' $cardOk "box=$(Box-Text $box) scale=$s"

    # 3 - a card's notices
    $missingCard = Raw-Find (Card 'Gone') 'Image not found'
    $brokenCard = Raw-Find (Card 'Broken') "This image can't be shown"
    Add-Result $run '3  a card says when an image''s file is missing or unreadable' ($null -ne $missingCard -and $null -ne $brokenCard) "missing=$($null -ne $missingCard) unreadable=$($null -ne $brokenCard)"

    # 4 - the editor draws it below its line, in reserved space
    Edit-Note 'Red'
    Start-Sleep -Milliseconds 800
    $box = Red-Box (Editor)
    $caption = @((Range-Of 'red square').GetBoundingRectangles())[0]
    $after = @((Range-Of 'after the picture').GetBoundingRectangles())[0]
    # The caption's rectangle, as RichEdit reports it, takes in the space reserved below its text: the picture is in
    # that space, after the text.
    $belowCaption = $box -and $box.Y -gt $caption.Y -and $box.Y -ge $caption.Y + $caption.Height - $box.H - [Math]::Ceiling(12 * $s) - 1 -and $box.Y + $box.H -le $caption.Y + $caption.Height
    $textBelow = $box -and $after.Y -ge $box.Y + $box.H - 1
    $shape = $box -and [Math]::Abs($box.W / $box.H - 2) -lt 0.04 -and $box.W -le [Math]::Ceiling(400 * $s) + 1
    Shot 'editor'
    Add-Result $run '4  the editor draws the image below its line; the next line starts below the picture' ($belowCaption -and $textBelow -and $shape) "box=$(Box-Text $box) caption.bottom=$([int]($caption.Y + $caption.Height)) next.top=$([int]$after.Y)"
    Leave-Editor

    # 5 - tall
    Edit-Note 'Tall'
    Start-Sleep -Milliseconds 800
    $box = Red-Box (Editor)
    Add-Result $run '5  a tall image is drawn at most 320 tall, its shape kept' ($box -and $box.H -le [Math]::Ceiling(320 * $s) + 1 -and $box.H -ge [Math]::Floor(320 * $s) - 2 -and [Math]::Abs($box.W / $box.H - 0.4) -lt 0.02) "box=$(Box-Text $box)"
    Leave-Editor

    # 6 - small
    Edit-Note 'Small'
    Start-Sleep -Milliseconds 800
    $box = Red-Box (Editor)
    Add-Result $run '6  a small image is never enlarged' ($box -and [Math]::Abs($box.W - 40 * $s) -le 2 -and [Math]::Abs($box.H - 20 * $s) -le 2) "box=$(Box-Text $box) expected=$(40 * $s)x$(20 * $s)"
    Leave-Editor

    # 7 - the editor's notices
    Edit-Note 'Gone'
    Start-Sleep -Milliseconds 600
    $missing = Raw-Find $AE::FromHandle($script:noto.Hwnd) 'Image not found'
    Leave-Editor
    Edit-Note 'Broken'
    Start-Sleep -Milliseconds 800
    $broken = Raw-Find $AE::FromHandle($script:noto.Hwnd) "This image can't be shown"
    Leave-Editor
    Add-Result $run '7  the editor says when an image''s file is missing or unreadable' ($null -ne $missing -and $null -ne $broken) "missing=$($null -ne $missing) unreadable=$($null -ne $broken)"

    # 8 - accessibility: the source is read, the picture is not in the control view
    Edit-Note 'Red'
    $value = Editor-Value
    $host_ = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent((Editor))
    $images = $host_.FindAll($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::Image))
    Add-Result $run '8  the picture is not in the accessibility tree; the caption, the source, is' ($value -eq $Notes['01J00000000000000000000N61'] -and $images.Count -eq 0) "value ok=$($value -eq $Notes['01J00000000000000000000N61']) images in control view=$($images.Count)"
    Leave-Editor

    $savedClip = Get-Clipboard -Raw
    try {
        # 9 - a pasted picture
        Edit-Note 'Paste here'
        Caret-End
        $before = Attachments
        Invoke-Sta 'set-image' $SetImage @('120', '60')
        Send-ToNoto @(0x11) 0x56 2000
        $added = @(Attachments | Where-Object { $before -notcontains $_ })
        $lines = (Editor-Value).Split("`n")
        $written = $added.Count -eq 1 -and $lines[-1] -eq "![Pasted image](attachments/$($added[0]))" -and $lines[-2] -eq 'last line'
        $png = if ($added.Count -eq 1) { Png-Size (Join-Path $folderOfImages $added[0]) } else { '' }
        Send-ToNoto @(0x11) 0x5A 900
        $undone = (Editor-Value) -eq $Notes['01J00000000000000000000N67']
        $script:pasted = if ($added.Count -eq 1) { $added[0] } else { $null }
        Add-Result $run '9  a pasted picture is kept as PNG on its own line; one undo removes it' ($written -and $png -eq 'True 120x60' -and $undone) "added=$($added -join ',') tail='$($lines[-1])' png='$png' undone=$undone"

        # 10 - text and a picture: the text
        $before = Attachments
        Invoke-Sta 'set-image-text' $SetImage @('30', '30', 'plain words')
        Send-ToNoto @(0x11) 0x56 1500
        $added = @(Attachments | Where-Object { $before -notcontains $_ })
        $pastedValue = Editor-Value
        $textPasted = $pastedValue.EndsWith('last lineplain words')
        Send-ToNoto @(0x11) 0x5A 900
        Add-Result $run '10 a clipboard with text and a picture pastes the text' ($textPasted -and $added.Count -eq 0) "text=$textPasted new files=$($added.Count) tail='$(($pastedValue.Split("`n"))[-2..-1] -join ' | ')'"

        # 11 - copied files
        $photo = Join-Path $run.Root 'holiday.png'; New-RedPng $photo 80 40
        $paper = Join-Path $run.Root 'paper.txt'; Set-Content $paper 'a document'
        $before = Attachments
        Invoke-Sta 'set-files' $SetFiles @($photo, $paper)
        Send-ToNoto @(0x11) 0x56 2000
        $added = @(Attachments | Where-Object { $before -notcontains $_ })
        $lines = (Editor-Value).Split("`n")
        $same = $added.Count -eq 1 -and (Get-FileHash (Join-Path $folderOfImages $added[0])).Hash -eq (Get-FileHash $photo).Hash
        $copied = $same -and ($lines[-1] -eq "![holiday.png](attachments/$($added[0]))") -and ($lines[-2] -eq "last line[paper.txt]($(([Uri]$paper).AbsoluteUri))")
        Shot 'pasted'
        Add-Result $run '11 a copied image file is kept as a copy and shown; a copied text file is linked' $copied "added=$($added -join ',') same=$same tail='$($lines[-3..-1] -join ' | ')'"
        Send-ToNoto @(0x11) 0x5A 600
        Send-ToNoto @(0x11) 0x5A 900
    }
    finally { if ($null -ne $savedClip) { Set-Clipboard -Value $savedClip } else { Set-Clipboard -Value $null } }

    # 12 - a dropped file, where it was dropped
    $drop = Join-Path $run.Root 'dropped.png'; New-RedPng $drop 60 30
    $target = @((Range-Of 'drop here').GetBoundingRectangles())[0]
    $x = [int]($target.X + $target.Width - 2); $y = [int]($target.Y + $target.Height / 2)
    # The drag starts a little below the note's last line, on the helper's own window shown over Noto.
    $last = @((Range-Of 'last line').GetBoundingRectangles())[0]
    $fromX = [int]($last.X + 30); $fromY = [int]($last.Y + $last.Height + 40)
    if ([NotoVal.Win32]::GetForegroundWindow() -ne $script:noto.Hwnd) { throw 'refusing to drop: Noto is not the foreground window' }
    foreach ($point in @(@($x, $y), @($fromX, $fromY))) {
        $pt = New-Object NotoVal.Win32+POINT; $pt.X = $point[0]; $pt.Y = $point[1]
        if ([NotoVal.Win32]::GetAncestor([NotoVal.Win32]::WindowFromPoint($pt), 2) -ne $script:noto.Hwnd) { throw "refusing to drag at ($($point[0]),$($point[1])): it is not Noto" }
    }
    $before = Attachments
    $effect = & powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'helpers/drop-files.ps1') "$fromX" "$fromY" "$x" "$y" $drop
    Start-Sleep -Milliseconds 1500
    if ([NotoVal.Win32]::GetForegroundWindow() -ne $script:noto.Hwnd) { throw 'the drag took the foreground from Noto' }
    $added = @(Attachments | Where-Object { $before -notcontains $_ })
    $lines = (Editor-Value).Split("`n")
    $at = [Array]::IndexOf($lines, "![dropped.png](attachments/$($added | Select-Object -First 1))")
    Add-Result $run '12 an image file dropped on the editor is kept and written where it was dropped' ($added.Count -eq 1 -and $at -eq 3 -and $lines[2] -eq 'drop here') "$effect at=($x,$y) editor=$((Editor).Current.BoundingRectangle) added=$($added -join ',') lines='$($lines -join ' | ')'"
    Send-ToNoto @(0x11) 0x5A 900
    Leave-Editor

    # 14 - cost of a note with five images
    $mark = (Traces 'Noto\.Editor').Count
    Edit-Note 'Five'
    $deadline = (Get-Date).AddSeconds(10)
    $drawn = $null
    while (-not $drawn -and (Get-Date) -lt $deadline) { $drawn = @(Traces 'Noto\.Editor' | Select-Object -Skip $mark | Where-Object { $_ -match 'images drawn' })[-1]; if (-not $drawn) { Start-Sleep -Milliseconds 200 } }
    $since = @(Traces 'Noto\.Editor' | Select-Object -Skip $mark)
    $loaded = Ms (@($since | Where-Object { $_ -match 'Noto\.Editor loaded' })[-1])
    $rendered = Ms (@($since | Where-Object { $_ -match 'Noto\.Editor rendered' })[-1])
    $all = Ms $drawn
    Add-Result $run '14 five 1200x800 images: first frame under 100 ms, all drawn under 1000 ms' ($null -ne $all -and $drawn -match 'count=5' -and $rendered -lt 100 -and $all -lt 1000) "loaded=$loaded ms rendered=$rendered ms drawn='$drawn'"
    Leave-Editor

    if (-not (Stop-Noto $script:noto)) { throw 'Noto did not exit cleanly' }

    # 13 - the picture pasted and undone in case 9 is no note's: aged a week, the next start sweeps it
    if ($script:pasted) { (Get-Item (Join-Path $folderOfImages $script:pasted)).LastWriteTime = $week }
    $script:noto = Start-Noto $run -Exe $NotoExe -DataRoot $script:data
    $deadline = (Get-Date).AddSeconds(5)
    while ($script:pasted -and (Test-Path (Join-Path $folderOfImages $script:pasted)) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 200 }
    $gone = $script:pasted -and -not (Test-Path (Join-Path $folderOfImages $script:pasted))
    $stillReferenced = (Test-Path (Attachment $Red)) -and (Test-Path (Attachment $Tall))
    Add-Result $run '13 a picture removed and older than a week is swept at the next start; referenced ones stay' ($gone -and $stillReferenced) "pasted=$($script:pasted) gone=$gone referenced kept=$stillReferenced"
    if (-not (Stop-Noto $script:noto)) { throw 'Noto did not exit cleanly' }
}) }
finally {
    $script:log.Dispose()
    Set-Content (Join-Path $run.Root 'trace.txt') ($script:log.All())
}
