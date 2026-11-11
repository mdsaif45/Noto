#Requires -Version 7.4
<#
.SYNOPSIS
  F16 (#10): a Noto failing at every start stops instead of failing again: quietly at sign-in, otherwise only on the
  user's word.

.DESCRIPTION
  One isolated data root on a temporary virtual desktop; Noto started by the shell; the real data folders checked
  unchanged. The record of failures (failures.txt in the data root) is seeded with three failures, each five seconds
  after a start, in the last few minutes: a loop. Evidence: the windows Noto shows (UI Automation), whether it exits,
  the record afterwards, and its log's CrashLoopStopped event (booleans only).

    1  a start asks (a Windows message box), shows no workspace; No ends it, and the record stays
    2  a start asks again; Yes clears the record and Noto starts, its workspace shown
    3  a sign-in start (--login) asks nothing and shows nothing: it ends quietly, and the record stays
    4  with -DebugExe (a Debug build, which can fail on demand): a failure soon after a start is recorded as one line,
       a few seconds after its start

.EXAMPLE
  pwsh -NoProfile -File tools/validation/Invoke-CrashLoop.ps1 -NotoExe <frozen Release>\Noto.exe [-DebugExe <Debug>\Noto.exe]
#>
param([Parameter(Mandatory)] [string] $NotoExe, [string] $DebugExe)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'NotoValidation.psm1') -Force
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$NotoExe = (Resolve-Path $NotoExe).Path
if ($DebugExe) { $DebugExe = (Resolve-Path $DebugExe).Path }

$run = New-ValidationRun 'crash-loop'
Write-Host "run $($run.Id)  exe $NotoExe"
$AE = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]

function Seed-Loop {
    $now = [DateTimeOffset]::UtcNow
    @(1..3 | ForEach-Object { "$($now.AddMinutes(-$_).ToString('O', [Globalization.CultureInfo]::InvariantCulture)) 5" }) | Set-Content -LiteralPath $script:record
}
function Shown([int] $ProcessId) {
    <# Noto's visible top-level windows, by class: #32770 is a Windows message box, anything else the workspace. #>
    @([NotoVal.Win32]::TopWindows([uint32]$ProcessId) | Where-Object { [NotoVal.Win32]::IsWindowVisible($_) } | ForEach-Object {
            [pscustomobject]@{ Hwnd = $_; Class = $AE::FromHandle($_).Current.ClassName; Title = [NotoVal.Win32]::Text($_) }
        })
}
function Start-Here([string] $Exe, [string] $Extra = '') {
    Start-ViaShell $run -File $Exe -Arguments "$Extra --data-root=`"$script:data`"".Trim() -Marker "--data-root=`"$script:data`""
}
function Until([scriptblock] $Condition, [int] $Seconds) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) { if (& $Condition) { return $true }; Start-Sleep -Milliseconds 250 }
    [bool](& $Condition)
}
function Running([int] $ProcessId) { [bool](Get-Process -Id $ProcessId -ErrorAction SilentlyContinue) }
function Parts([IntPtr] $Dialog) {
    <# A message box's controls in UI Automation's raw view: Win32 Buttons and Statics (here reported as panes). #>
    $walker = [System.Windows.Automation.TreeWalker]::RawViewWalker
    $parts = @(); $c = $walker.GetFirstChild($AE::FromHandle($Dialog))
    while ($c) { $parts += $c; $c = $walker.GetNextSibling($c) }
    $parts
}
function Press([IntPtr] $Dialog, [string] $Name) {
    <# Clicks a message box's button by posting BM_CLICK to it: no key and no pointer reach the desktop. A button is named
       with its access key ("&No"), so the ampersand is not compared. #>
    $button = $null
    $deadline = (Get-Date).AddSeconds(5)
    while ((Get-Date) -lt $deadline -and -not $button) {
        $button = Parts $Dialog | Where-Object { $_.Current.ClassName -eq 'Button' -and ($_.Current.Name -replace '&', '') -eq $Name } | Select-Object -First 1
        if (-not $button) { Start-Sleep -Milliseconds 250 }
    }
    if (-not $button) { throw "the message box has no '$Name' button (it has: $(@(Parts $Dialog | ForEach-Object { "$($_.Current.ClassName):$($_.Current.Name)" }) -join ', '))" }
    [void][NotoVal.Win32]::PostMessage([IntPtr]$button.Current.NativeWindowHandle, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero)
}
function Stopped-Events { @(Get-ChildItem -LiteralPath (Join-Path $script:data 'logs') -File -ErrorAction SilentlyContinue | Get-Content | Where-Object { $_ -match '"CrashLoopStopped"' }) }

exit (Invoke-IsolatedRun $run -Title 'F16: crash-loop detection' -Body {
        $script:data = Join-Path $run.Root 'data'; New-Item -ItemType Directory $script:data | Out-Null
        $first = Start-Noto $run -Exe $NotoExe -DataRoot $script:data
        if (-not (Stop-Noto $first)) { throw 'the seeding start did not exit cleanly' }
        $script:record = Join-Path $script:data 'failures.txt'

        # 1 - asks; No ends it
        Seed-Loop
        $p = Start-Here $NotoExe
        $asked = Until { @(Shown $p.Pid | Where-Object Class -eq '#32770').Count -eq 1 } 20
        $windows = Shown $p.Pid
        $dialog = @($windows | Where-Object Class -eq '#32770')[0]
        $workspace = @($windows | Where-Object Class -ne '#32770').Count
        $text = if ($dialog) { (Parts $dialog.Hwnd | Where-Object { $_.Current.ClassName -eq 'Static' } | ForEach-Object { $_.Current.Name }) -join ' ' } else { '' }
        if ($dialog) { Press $dialog.Hwnd 'No' }
        $ended = Until { -not (Running $p.Pid) } 15
        Add-Result $run '1  a start in a loop asks, shows no workspace; No ends it and the record stays' ($asked -and $workspace -eq 0 -and $text -match 'Start Noto anyway' -and $ended -and (Test-Path $script:record) -and (Stopped-Events).Count -eq 1) "asked=$asked workspace windows=$workspace ended=$ended record=$(Test-Path $script:record) events=$((Stopped-Events) -join ' ') text='$($text.Substring(0, [Math]::Min(80, $text.Length)))'"

        # 2 - Yes starts it and clears the record
        $p = Start-Here $NotoExe
        $asked = Until { @(Shown $p.Pid | Where-Object Class -eq '#32770').Count -eq 1 } 20
        $dialog = @(Shown $p.Pid | Where-Object Class -eq '#32770')[0]
        if ($dialog) { Press $dialog.Hwnd 'Yes' }
        $started = Until { @(Shown $p.Pid | Where-Object { $_.Class -ne '#32770' -and $_.Title -eq 'Noto' }).Count -ge 1 } 30
        $hwnd = @(Shown $p.Pid | Where-Object { $_.Class -ne '#32770' -and $_.Title -eq 'Noto' })[0].Hwnd
        Add-Result $run '2  Yes clears the record and Noto starts, its workspace shown' ($asked -and $started -and -not (Test-Path $script:record)) "asked=$asked started=$started record=$(Test-Path $script:record) events=$((Stopped-Events).Count)"
        if ($hwnd) { $null = Stop-Noto ([pscustomobject]@{ Pid = $p.Pid; Hwnd = $hwnd }) }

        # 3 - at sign-in: nothing asked, nothing shown, ends
        Seed-Loop
        $before = (Stopped-Events).Count
        # It ends too soon for the shell helper to see the process, so it is started through the shell without waiting,
        # and every Noto's windows are watched until its event is logged and no Noto is left.
        [NotoVal.ShellLaunch]::DesktopViewDispatch().Application.ShellExecute($NotoExe, "--login --data-root=`"$script:data`"", $run.Root, 'open', 1)
        $seen = @()
        $deadline = (Get-Date).AddSeconds(15)
        do {
            foreach ($n in @(Get-Process -Name Noto -ErrorAction SilentlyContinue)) { $seen += @(Shown $n.Id) }
            Start-Sleep -Milliseconds 50
        } while ((Get-Date) -lt $deadline -and ((Stopped-Events).Count -le $before -or @(Get-Process -Name Noto -ErrorAction SilentlyContinue).Count -gt 0))
        $ended = @(Get-Process -Name Noto -ErrorAction SilentlyContinue).Count -eq 0
        Add-Result $run '3  a sign-in start in a loop asks nothing, shows nothing and ends; the record stays' ($ended -and $seen.Count -eq 0 -and (Test-Path $script:record) -and (Stopped-Events).Count -eq $before + 1 -and (Stopped-Events)[-1] -match '"login":true') "ended=$ended windows seen=$($seen.Count) record=$(Test-Path $script:record) last event=$((Stopped-Events)[-1])"

        # 4 - a real failure soon after a start is recorded (Debug builds can fail on demand)
        if ($DebugExe) {
            Remove-Item -LiteralPath $script:record -ErrorAction SilentlyContinue
            $d = Start-Noto $run -Exe $DebugExe -DataRoot $script:data
            Set-Content -LiteralPath (Join-Path $script:data 'noto-fault-ui') -Value ''
            $ended = Until { -not (Running $d.Pid) } 30
            $lines = @(Get-Content -LiteralPath $script:record -ErrorAction SilentlyContinue)
            $seconds = if ($lines.Count -eq 1) { [double]($lines[0] -split ' ')[1] } else { -1 }
            Add-Result $run '4  a failure soon after a start is recorded once, with how soon' ($ended -and $lines.Count -eq 1 -and $seconds -ge 0 -and $seconds -lt 60) "ended=$ended lines=$($lines.Count) after start=$seconds s"
        }
        else {
            Write-Host '  4 skipped: no -DebugExe (only a Debug build fails on demand)'
        }
    })
