#Requires -Version 7.4
<#
.SYNOPSIS
  Unsaved editor text and closing the window: saved, kept on failure, discarded only on the second close.

.DESCRIPTION
  Each case gets a fresh isolated data root seeded with one folder and one note. The editor's text is set
  through UI Automation (ValuePattern) - nothing is typed into the editor. Keys reach Noto only while it is
  verifiably the foreground window, which the activation hotkey arranges.

    1  unsaved text, Alt+F4          -> saved, process exits, row holds the new text
    2  clean editor, close           -> exits, row untouched (UpdatedAt unchanged)
    3  save fails (note binned), Alt+F4 -> close cancelled, window stays, text kept, notice shown, row untouched
       ... then close again          -> exits, the unsaved text is discarded, row untouched
    4  save fails, text edited, close -> a new first attempt: cancelled again; the next close exits
    5  close from the note list      -> exits
    6  close from the folder list    -> exits

.EXAMPLE
  pwsh -NoProfile -File tools/validation/Invoke-EditorSaveOnClose.ps1 -NotoExe <path>\Noto.exe
#>
param([Parameter(Mandatory)] [string] $NotoExe)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'NotoValidation.psm1') -Force
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$NotoExe = (Resolve-Path $NotoExe).Path

$run = New-ValidationRun 'save-on-close'
Write-Host "run $($run.Id)  exe $NotoExe"

$AE = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
$FolderId = '01J0000000000000000000F001'
$NoteId = '01J0000000000000000000N001'
$Original = 'original note text'

function Find-ById($Noto, [string] $Id, [int] $Seconds = 10) {
    $cond = [System.Windows.Automation.PropertyCondition]::new($AE::AutomationIdProperty, $Id)
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        $e = $AE::FromHandle($Noto.Hwnd).FindFirst($Scope::Descendants, $cond)
        if ($e -and -not $e.Current.IsOffscreen) { return $e }
        Start-Sleep -Milliseconds 200
    }
    throw "no visible element '$Id' within ${Seconds}s"
}

function First-Item($Noto, [string] $ListId) {
    $list = Find-ById $Noto $ListId
    $cond = [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $deadline) { $i = $list.FindFirst($Scope::Descendants, $cond); if ($i) { return $i }; Start-Sleep -Milliseconds 200 }
    throw "list '$ListId' has no item"
}

function Send-ToNoto($Noto, [int[]] $Mods, [int] $Key) {
    if ([NotoVal.Win32]::GetForegroundWindow() -ne $Noto.Hwnd) { throw 'refusing to send keys: Noto is not the foreground window' }
    $rev = [int[]]$Mods.Clone(); [array]::Reverse($rev)
    [NotoVal.Win32]::Keys(@(@($Mods | ForEach-Object { [NotoVal.Win32]::Vk($_, $false) }) + @([NotoVal.Win32]::Vk($Key, $false), [NotoVal.Win32]::Vk($Key, $true)) + @($rev | ForEach-Object { [NotoVal.Win32]::Vk($_, $true) })))
    Start-Sleep -Milliseconds 700
}

function Bring-NotoForward($Noto, $Target) {
    $null = Set-ForegroundByKeyboard $Target
    Send-ActivationChord -AllowedForeground $Target.Hwnd
    if ([NotoVal.Win32]::GetForegroundWindow() -ne $Noto.Hwnd) { throw 'the hotkey did not bring Noto forward' }
}

function Select-AndFocus($Item) {
    $Item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $Item.SetFocus(); Start-Sleep -Milliseconds 300
}

function New-SeededRoot([string] $Name) {
    $data = Join-Path $run.Root $Name; New-Item -ItemType Directory $data | Out-Null
    $first = Start-Noto $run -Exe $NotoExe -DataRoot $data
    if (-not (Stop-Noto $first)) { throw 'the seeding start did not exit cleanly' }
    $now = [DateTimeOffset]::UtcNow.ToString('O')
    $null = Invoke-NotoSql $run -Exe $NotoExe -DataRoot $data -Sql "INSERT INTO Folders (Id, Name, SortOrder, CreatedAt, UpdatedAt) VALUES (`$f, 'Validation', 1, `$t, `$t)" -Parameters @{ '$f' = $FolderId; '$t' = $now }
    $null = Invoke-NotoSql $run -Exe $NotoExe -DataRoot $data -Sql "INSERT INTO Notes (Id, FolderId, Content, SortOrder, CreatedAt, UpdatedAt) VALUES (`$n, `$f, `$c, 1, `$t, `$t)" -Parameters @{ '$n' = $NoteId; '$f' = $FolderId; '$c' = $Original; '$t' = $now }
    $data
}

function Row($Data, [string] $Column) { Get-NotoScalar $run -Exe $NotoExe -DataRoot $Data -Sql "SELECT $Column FROM Notes WHERE Id = `$n" -Parameters @{ '$n' = $NoteId } }

# Starts Noto on a seeded root and walks it to the requested surface: 'folders', 'notes' or 'editor'.
function Open-At($Data, [string] $Surface, $Target) {
    $noto = Start-Noto $run -Exe $NotoExe -DataRoot $Data
    Bring-NotoForward $noto $Target
    if ($Surface -eq 'folders') { $null = First-Item $noto 'FolderList'; return $noto }
    Select-AndFocus (First-Item $noto 'FolderList')
    Send-ToNoto $noto @(0x11) 0x28                                   # Ctrl+Down: enter the folder
    $null = First-Item $noto 'NoteList'
    if ($Surface -eq 'notes') { return $noto }
    Select-AndFocus (First-Item $noto 'NoteList')
    Send-ToNoto $noto @() 0x0D                                       # Enter: open the note
    $editor = Find-ById $noto 'NoteEditor'
    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $deadline -and $editor.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -ne $Original) { Start-Sleep -Milliseconds 200 }
    $noto
}

function Editor-Value($Noto) { (Find-ById $Noto 'NoteEditor').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value }
function Set-Editor($Noto, [string] $Text) { (Find-ById $Noto 'NoteEditor').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Text); Start-Sleep -Milliseconds 300 }
function Notice($Noto) { try { (Find-ById $Noto 'EditorNoticeText' 2).Current.Name } catch { '' } }

function Close-AltF4($Noto) { Send-ToNoto $Noto @(0x12) 0x73 }
function Close-SysCommand($Noto) { [void][NotoVal.Win32]::PostMessage($Noto.Hwnd, 0x0112, [IntPtr]0xF060, [IntPtr]::Zero); Start-Sleep -Milliseconds 700 }
function Exited($Noto, [int] $Seconds = 10) { $p = Get-Process -Id $Noto.Pid -ErrorAction SilentlyContinue; (-not $p) -or $p.WaitForExit($Seconds * 1000) }
function Still-Open($Noto) { Start-Sleep -Seconds 2; [bool](Get-Process -Id $Noto.Pid -ErrorAction SilentlyContinue) -and (Get-WindowEvidence $Noto).WsVisible }
function Bin-Note($Data) { $null = Invoke-NotoSql $run -Exe $NotoExe -DataRoot $Data -Sql "UPDATE Notes SET DeletedAt = `$t WHERE Id = `$n" -Parameters @{ '$t' = [DateTimeOffset]::UtcNow.ToString('O'); '$n' = $NoteId } }

exit (Invoke-IsolatedRun $run -Title 'Editor: save before the window closes' -Body {
    Assert-ChordFree $run
    $target = Start-TargetApp $run

    # 1 - unsaved text, Alt+F4
    $data = New-SeededRoot 'case1'
    $noto = Open-At $data 'editor' $target
    $text = "saved on close $([guid]::NewGuid().ToString('N').Substring(0, 6))"
    Set-Editor $noto $text
    Close-AltF4 $noto
    $gone = Exited $noto
    Add-Result $run '1  unsaved text + Alt+F4 -> process exits' $gone "pid $($noto.Pid)"
    Add-Result $run '1  the row holds exactly the unsaved text' ((Row $data 'Content') -eq $text) "content='$(Row $data 'Content')'"

    # 2 - clean editor, close
    $data = New-SeededRoot 'case2'
    $before = Row $data 'UpdatedAt'
    $noto = Open-At $data 'editor' $target
    Close-SysCommand $noto
    Add-Result $run '2  clean editor + close -> process exits' (Exited $noto) ''
    Add-Result $run '2  row untouched (content and UpdatedAt)' (((Row $data 'Content') -eq $Original) -and ((Row $data 'UpdatedAt') -eq $before)) "UpdatedAt $before -> $(Row $data 'UpdatedAt')"

    # 3 - save fails, then the second close discards
    $data = New-SeededRoot 'case3'
    $noto = Open-At $data 'editor' $target
    $text = "cannot be saved $([guid]::NewGuid().ToString('N').Substring(0, 6))"
    Set-Editor $noto $text
    Bin-Note $data
    Close-AltF4 $noto
    $open = Still-Open $noto
    Add-Result $run '3  save fails + Alt+F4 -> close cancelled, window still visible' $open "pid $($noto.Pid)"
    Add-Result $run '3  the editor still holds the unsaved text' ($open -and (Editor-Value $noto) -eq $text) ''
    $notice = Notice $noto
    Add-Result $run '3  the failure notice is shown and offers the second close' ($notice -match 'recycle bin' -and $notice -match 'Close the window again') "notice='$notice'"
    Add-Result $run '3  nothing was written' ((Row $data 'Content') -eq $Original) ''
    Bring-NotoForward $noto $target
    Close-AltF4 $noto
    Add-Result $run '3  second close with the same text -> process exits' (Exited $noto) ''
    Add-Result $run '3  the unsaved text was discarded, the row untouched' ((Row $data 'Content') -eq $Original -and [bool](Row $data 'DeletedAt')) ''

    # 4 - an edit after the failure is a new first attempt
    $data = New-SeededRoot 'case4'
    $noto = Open-At $data 'editor' $target
    Set-Editor $noto 'first unsaved text'
    Bin-Note $data
    Close-SysCommand $noto
    Add-Result $run '4  first failed close is cancelled' (Still-Open $noto) ''
    Set-Editor $noto 'edited after the failure'
    Close-SysCommand $noto
    Add-Result $run '4  close after an edit is a new first attempt: cancelled again' (Still-Open $noto) ''
    Close-SysCommand $noto
    Add-Result $run '4  the next close with the same text exits' (Exited $noto) ''
    Add-Result $run '4  row untouched' ((Row $data 'Content') -eq $Original) ''

    # 5 - close from the note list
    $data = New-SeededRoot 'case5'
    $noto = Open-At $data 'notes' $target
    Close-SysCommand $noto
    Add-Result $run '5  close from the note list -> exits' (Exited $noto) ''

    # 6 - close from the folder list
    $data = New-SeededRoot 'case6'
    $noto = Open-At $data 'folders' $target
    Close-AltF4 $noto
    Add-Result $run '6  close from the folder list -> exits' (Exited $noto) ''
})
