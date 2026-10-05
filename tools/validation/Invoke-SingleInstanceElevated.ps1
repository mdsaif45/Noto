#Requires -Version 7.4
<#
.SYNOPSIS
  A17 scenario 23: an elevated launch. NEEDS ONE UAC APPROVAL from the person at the machine.

.DESCRIPTION
  ADR-007 §8 and ADR-013: an elevated Noto must never own a data root. It may hand off to a running, normal
  Noto; with none running it is refused.

  This script runs normally. It starts a normal Noto owner on one isolated root, then asks Windows to run
  helpers/elevated-launch.ps1 elevated (the UAC prompt). That helper - and only it - launches Noto elevated:

    23a elevated, no owner     -> message box naming the requirement, exit 3, never holds the mutex
    23b elevated, normal owner -> hands off: exit 0, no window; the normal owner stays the only owner, still normal

  Nothing is elevated except that helper and the two Noto processes it starts. If the prompt is declined or
  times out, the run is INVALID (exit 2): the scenario was not validated.

.EXAMPLE
  pwsh -NoProfile -File tools/validation/Invoke-SingleInstanceElevated.ps1 -NotoExe <path>\Noto.exe
#>
param([Parameter(Mandatory)] [string] $NotoExe, [int] $ApprovalSeconds = 120)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'NotoValidation.psm1') -Force
$NotoExe = (Resolve-Path $NotoExe).Path
if (-not ('NotoVal.Instance' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'helpers/single-instance.cs') }

$run = New-ValidationRun 'single-instance-elevated'
Write-Host "run $($run.Id)  exe $NotoExe"

function Trace-Of([int] $ProcessId) { @($log.Lines($ProcessId)) -join ' | ' }

$log = [NotoVal.DebugLog]::new()
try {
    $code = Invoke-IsolatedRun $run -Title 'A17 scenario 23: elevated launch' -Body {
        if (@(Get-Process -Name Noto -ErrorAction SilentlyContinue).Count) { throw 'a Noto process is already running; close it and rerun' }
        $free = Join-Path $run.Root 'no-owner'; New-Item -ItemType Directory $free | Out-Null
        $owned = Join-Path $run.Root 'owned'; New-Item -ItemType Directory $owned | Out-Null
        $freeName = [NotoVal.Instance]::PipeName($free); $ownedName = [NotoVal.Instance]::PipeName($owned)

        $owner = Start-Noto $run -Exe $NotoExe -DataRoot $owned
        $out = Join-Path $run.Root 'elevated.txt'
        $helper = Join-Path $PSScriptRoot 'helpers/elevated-launch.ps1'
        $helperArgs = "-NoProfile -WindowStyle Hidden -File `"$helper`" -Exe `"$NotoExe`" -NoOwnerRoot `"$free`" -OwnedRoot `"$owned`" -NoOwnerMutex `"$freeName`" -Out `"$out`""

        Write-Host '  >>> A UAC prompt is about to appear. Approve it to run the elevated scenario. <<<'
        $view = [NotoVal.ShellLaunch]::DesktopViewDispatch()
        $view.Application.ShellExecute('pwsh.exe', $helperArgs, $run.Root, 'runas', 0)
        $deadline = (Get-Date).AddSeconds($ApprovalSeconds)
        while ((Get-Date) -lt $deadline -and -not ((Get-Content $out -ErrorAction SilentlyContinue) -match '^done$')) { Start-Sleep -Milliseconds 500 }
        $lines = @(Get-Content $out -ErrorAction SilentlyContinue)
        if (-not ($lines -match '^done$')) { Add-Invalid $run "the elevated helper did not finish within ${ApprovalSeconds}s (UAC declined or not answered): not validated"; return }
        foreach ($l in $lines) { Write-Host "    helper: $l" }
        foreach ($m in [regex]::Matches(($lines -join "`n"), '(?m)^[AB] pid=(\d+)')) { Add-Started $run ([int]$m.Groups[1].Value) $null }

        $a = ($lines | Where-Object { $_ -like 'A pid=*' }) -join ''
        $aPid = if ($a -match '^A pid=(\d+)') { [int]$Matches[1] } else { 0 }
        $b = ($lines | Where-Object { $_ -like 'B pid=*' }) -join ''
        $bPid = if ($b -match '^B pid=(\d+)') { [int]$Matches[1] } else { 0 }

        Add-Result $run '23 the helper ran elevated' (($lines -join ' ') -match 'elevated=True') ''
        Add-Result $run '23a elevated, no owner -> refused: message box, exit 3' ($a -match 'exited=True code=3 ' -and $a -match 'windows=0 ' -and $a -match 'dialogMs=\d' -and $a -match 'normal-integrity') $a
        Add-Result $run '23a ... never held the mutex (not while the message showed, not after)' ($a -match 'mutexWhileDialog=False' -and ($lines -join ' ') -match 'A mutexAfter=False') ''
        Add-Result $run '23a ... its own trace: claim Refused, refusal=Elevated' ((Trace-Of $aPid) -match 'outcome=Refused refusal=Elevated') (Trace-Of $aPid)
        Add-Result $run '23b elevated, normal owner -> handed off: exit 0, no window' ($b -match 'exited=True code=0 ' -and $b -match 'windows=0 ' -and $b -match 'dialogMs=-1') $b
        Add-Result $run '23b ... its own trace: claim HandedOff' ((Trace-Of $bPid) -match 'outcome=HandedOff') (Trace-Of $bPid)
        $server = [NotoVal.Instance]::PipeServerPid($ownedName)
        $ownerAlive = [bool](Get-Process -Id $owner.Pid -ErrorAction SilentlyContinue)
        Add-Result $run '23b ... the normal owner is still the only owner and serves the pipe' ($ownerAlive -and $server -eq $owner.Pid -and @(Get-Process -Name Noto -ErrorAction SilentlyContinue).Count -eq 1) "owner pid=$($owner.Pid) pipe server=$server"
        Add-Result $run '23b ... the owner traced the launch request' ((Trace-Of $owner.Pid) -match 'launch action=') (Trace-Of $owner.Pid)
        Add-Result $run 'teardown: the owner exited cleanly' (Stop-Noto $owner) ''
    }
}
finally {
    $log.Dispose()
    Set-Content (Join-Path $run.Root 'trace.txt') ($log.All())
}
exit $code
