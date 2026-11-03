#Requires -Version 7.4
<#
.SYNOPSIS
  Noto exits, every time: open, edit, close, many times over, each exit timed and its code checked.

.DESCRIPTION
  A release-blocking exit hang was seen once in development: Noto traced its whole shutdown and was still alive 10 s
  later. This repeats the full cycle that preceded it, deterministically:

    start (isolated data root) -> show (hotkey) -> enter the folder -> edit a card -> type -> format (Ctrl+B) ->
    leave or stay editing -> close (WM_CLOSE)

  and records how long the process took to end after the close and its exit code. Half the cycles run with a
  debug-output reader attached (as every other harness has, to read Noto's traces) and half without, because an
  OutputDebugString call waits on that reader: a stalled reader can hold a writer for up to 10 s.

  Pass: every exit ends within -ExitBudgetMs with code 0. Each cycle's exit time is reported.

.EXAMPLE
  pwsh -NoProfile -File tools/validation/Invoke-ExitCycles.ps1 -NotoExe <path>\Noto.exe -Cycles 30
#>
param([Parameter(Mandatory)] [string] $NotoExe, [ValidateRange(2, 200)] [int] $Cycles = 20, [int] $ExitBudgetMs = 3000)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'NotoValidation.psm1') -Force
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
if (-not ('NotoVal.Instance' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'helpers/single-instance.cs') }
$NotoExe = (Resolve-Path $NotoExe).Path
if (-not ('NotoVal.Dump' -as [type])) {
    Add-Type -TypeDefinition @'
using System; using System.IO; using System.Runtime.InteropServices;
namespace NotoVal {
    public static class Dump {
        [DllImport("dbghelp.dll", SetLastError = true)]
        static extern bool MiniDumpWriteDump(IntPtr process, uint pid, Microsoft.Win32.SafeHandles.SafeFileHandle file, uint type, IntPtr e, IntPtr u, IntPtr c);
        // Full memory (0x2) with thread and handle data (0x1000 | 0x4): enough for WinDbg/SOS to show every stack.
        public static bool Write(System.Diagnostics.Process p, string path) {
            using var f = new FileStream(path, FileMode.Create);
            return MiniDumpWriteDump(p.Handle, (uint)p.Id, f.SafeFileHandle, 0x2 | 0x4 | 0x1000, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        }
    }
}
'@
}

$run = New-ValidationRun 'exit-cycles'
Write-Host "run $($run.Id)  exe $NotoExe  cycles $Cycles"

$AE = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
$CT = [System.Windows.Automation.ControlType]
$FolderId = '01J0000000000000000000F019'
$NoteId = '01J00000000000000000000X01'

function Sql($Data, [string] $Text, [hashtable] $Parameters = @{}) {
    $db = Join-Path $Data 'noto.db'; Assert-UnderRun $run $db
    Initialize-NotoSqlite $NotoExe
    $c = [Microsoft.Data.Sqlite.SqliteConnection]::new("Data Source=$db"); $c.Open()
    try { $cmd = $c.CreateCommand(); $cmd.CommandText = $Text; foreach ($k in $Parameters.Keys) { [void]$cmd.Parameters.AddWithValue($k, $Parameters[$k]) }; [void]$cmd.ExecuteNonQuery() }
    finally { $c.Close(); [Microsoft.Data.Sqlite.SqliteConnection]::ClearAllPools() }
}
function Find-ById([string] $Id, [int] $Seconds = 10) {
    $cond = [System.Windows.Automation.PropertyCondition]::new($AE::AutomationIdProperty, $Id)
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) { $e = $AE::FromHandle($script:noto.Hwnd).FindFirst($Scope::Descendants, $cond); if ($e -and -not $e.Current.IsOffscreen) { return $e }; Start-Sleep -Milliseconds 150 }
    throw "no visible element '$Id' within ${Seconds}s"
}
function Send-ToNoto([int[]] $Mods, [int] $Key, [int] $Wait = 400) {
    if ([NotoVal.Win32]::GetForegroundWindow() -ne $script:noto.Hwnd) { throw 'refusing to send keys: Noto is not the foreground window' }
    $rev = [int[]]$Mods.Clone(); [array]::Reverse($rev)
    [NotoVal.Win32]::Keys(@(@($Mods | ForEach-Object { [NotoVal.Win32]::Vk($_, $false) }) + @([NotoVal.Win32]::Vk($Key, $false), [NotoVal.Win32]::Vk($Key, $true)) + @($rev | ForEach-Object { [NotoVal.Win32]::Vk($_, $true) })))
    Start-Sleep -Milliseconds $Wait
}
function Type-Text([string] $Text) {
    foreach ($ch in $Text.ToCharArray()) {
        if ([NotoVal.Win32]::GetForegroundWindow() -ne $script:noto.Hwnd) { throw 'refusing to type: Noto is not the foreground window' }
        $down = New-Object NotoVal.Win32+INPUT; $down.type = 1; $down.wScan = [uint16]$ch; $down.dwFlags = 4
        $up = New-Object NotoVal.Win32+INPUT; $up.type = 1; $up.wScan = [uint16]$ch; $up.dwFlags = 6
        [NotoVal.Win32]::Keys(@($down, $up)); Start-Sleep -Milliseconds 30
    }
    Start-Sleep -Milliseconds 250
}

exit (Invoke-IsolatedRun $run -Title 'Noto exits, every time' -Body {
    Assert-ChordFree $run
    $target = Start-TargetApp $run
    $data = Join-Path $run.Root 'data'; New-Item -ItemType Directory $data | Out-Null
    $first = Start-Noto $run -Exe $NotoExe -DataRoot $data
    if (-not (Stop-Noto $first)) { throw 'the seeding start did not exit cleanly' }
    $now = [DateTimeOffset]::UtcNow.ToString('O')
    Sql $data "INSERT INTO Folders (Id, Name, SortOrder, CreatedAt, UpdatedAt) VALUES (`$f, 'Cycles', 1, `$t, `$t)" @{ '$f' = $FolderId; '$t' = $now }
    Sql $data "INSERT INTO Notes (Id, FolderId, Content, SortOrder, CreatedAt, UpdatedAt) VALUES (`$n, `$f, `$c, 1, `$t, `$t)" @{ '$n' = $NoteId; '$f' = $FolderId; '$c' = "# Cycles`nsome **text** and - [ ] a task"; '$t' = $now }

    $times = @()
    for ($i = 1; $i -le $Cycles; $i++) {
        $listener = ($i % 2) -eq 1
        $log = if ($listener) { [NotoVal.DebugLog]::new() } else { $null }
        try {
            $script:noto = Start-Noto $run -Exe $NotoExe -DataRoot $data
            $proc = Get-Process -Id $script:noto.Pid
            $null = $proc.Handle   # opened now, so the exit code can be read after the process ends
            $null = Set-ForegroundByKeyboard $target
            Send-ActivationChord -AllowedForeground $target.Hwnd
            if ([NotoVal.Win32]::GetForegroundWindow() -ne $script:noto.Hwnd) { throw 'the hotkey did not bring Noto forward' }

            $folder = (Find-ById 'FolderList').FindFirst($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::ListItem))
            $folder.SetFocus(); Start-Sleep -Milliseconds 200
            Send-ToNoto @(0x11) 0x28 700
            $card = (Find-ById 'NoteList').FindFirst($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::ListItem))
            $card.SetFocus(); Start-Sleep -Milliseconds 200
            Send-ToNoto @() 0x0D 700
            $null = Find-ById 'NoteEditor'
            Send-ToNoto @(0x11) 0x23 200
            Type-Text " $i"
            Send-ToNoto @(0x11, 0x10) 0x25 200
            Send-ToNoto @(0x11) 0x42 300
            # Odd cycles close with the card still editing (the close saves), even ones after leaving it.
            if (($i % 4) -ge 2) { Send-ToNoto @() 0x1B 500 }

            $closed = [System.Diagnostics.Stopwatch]::StartNew()
            [void][NotoVal.Win32]::PostMessage($script:noto.Hwnd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
            $exited = $proc.WaitForExit($ExitBudgetMs)
            $dump = ''
            if (-not $exited) {
                # Over budget: keep the evidence of where it is stuck before waiting any longer.
                $path = Join-Path $run.Root "noto-exit-$i.dmp"
                $dump = if ([NotoVal.Dump]::Write($proc, $path)) { "dump: $path" } else { 'dump failed' }
                $exited = $proc.WaitForExit(20000)
            }
            $ms = $closed.ElapsedMilliseconds
            if (-not $exited) { Stop-Process -Id $proc.Id -Force; $proc.WaitForExit(5000) | Out-Null }
            $code = if ($exited) { $proc.ExitCode } else { 'killed' }
            $times += $ms
            Add-Result $run ("{0,2} exit {1}listener: {2} ms, code {3}" -f $i, $(if ($listener) { 'with ' } else { 'without ' }), $ms, $code) ($exited -and $code -eq 0 -and $ms -le $ExitBudgetMs) ("$dump " + $(if ($listener -and $dump) { "last traces: $((@($log.Lines($script:noto.Pid)) | Select-Object -Last 4) -join ' | ')" } else { '' })).Trim()
        }
        finally { if ($log) { $log.Dispose() } }
        Start-Sleep -Milliseconds 1500   # a person reopening, not a relaunch storm
    }

    $sorted = $times | Sort-Object
    Add-Result $run "exit time over $Cycles cycles: median $($sorted[[int]($sorted.Count / 2)]) ms, max $($sorted[-1]) ms" ($sorted[-1] -le $ExitBudgetMs) ''
})
