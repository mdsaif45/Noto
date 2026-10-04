#Requires -Version 7.4
<#
.SYNOPSIS
  Smoke test of the harness itself. No Noto build needed.

.DESCRIPTION
  Proves on this machine, before any Noto run is trusted, that:
    - processes start through the shell (parent = explorer), with fresh pids
    - real click + typing makes a window the foreground, and the typed text reaches only it
    - the negative control is refused: an unrelated shell-started process cannot take the foreground
    - the same window CAN be brought forward by real input, so a refusal is not just a broken probe
    - every started process is stopped, the virtual desktop is restored, real data is unchanged

.EXAMPLE
  pwsh -NoProfile -File tools/validation/Invoke-HarnessSelfTest.ps1
#>
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'NotoValidation.psm1') -Force

$run = New-ValidationRun 'selftest'
Write-Host "run $($run.Id)"

exit (Invoke-IsolatedRun $run -Title 'Harness self-test' -Body {
    $shell = Get-ShellPid
    $first = Start-TargetApp $run -Name 'first'
    $second = Start-TargetApp $run -Name 'second'
    $parents = @($first.Pid, $second.Pid | ForEach-Object { (Get-CimInstance Win32_Process -Filter "ProcessId=$_").ParentProcessId })
    Add-Result $run 'shell launch: both target apps are children of the shell' (@($parents | Where-Object { $_ -ne $shell }).Count -eq 0) "pids $($first.Pid),$($second.Pid) parents $($parents -join ',') shell $shell"
    Add-Result $run 'fresh pids: the two starts are distinct processes' ($first.Pid -ne $second.Pid) ''

    # Both windows are activated by real input once before any control. A window that has never been activated
    # may still hold its start-up activation right (seen as an occasional grant on a cold start); every window
    # the Noto runs probe has been activated already - Noto by its own launch, the target by a click.
    $null = Set-ForegroundByKeyboard $second
    $token = Set-ForegroundByKeyboard $first
    $leak = (Get-Content $second.Status -Raw -ErrorAction SilentlyContinue) -match $token
    Add-Result $run 'keyboard: first app in front, typed token reached it and not the other' (([NotoVal.Win32]::GetForegroundWindow() -eq $first.Hwnd) -and -not $leak) "token $token"

    for ($i = 1; $i -le 2; $i++) {
        $neg = Test-NegativeControl $run $second.Hwnd
        $run.Controls.Add([pscustomobject]@{ Label = "self-test #$i"; Refused = $neg.Refused; Detail = $neg.Detail })
        Add-Result $run "negative control #${i}: an unrelated process is refused the foreground" $neg.Refused $neg.Detail
    }

    $token2 = Set-ForegroundByKeyboard $second
    Add-Result $run 'positive control: real input does bring the second app forward' ([NotoVal.Win32]::GetForegroundWindow() -eq $second.Hwnd) "token $token2"
})
