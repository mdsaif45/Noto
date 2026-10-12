#Requires -Version 7.4
<#
.SYNOPSIS
  Regression check for the harness's process identity (no desktop, no Noto build needed).

.DESCRIPTION
  Windows reuses an exited process's pid. A campaign on 2026-10-12 went INVALID when a probe's pid was reused for
  the next app the shell started, and Start-ViaShell, which judged by pid alone, never saw the new process. A process
  is now new unless the run has seen its pid AND it started before the launch or wait began. Both implementations of
  that rule are checked: Test-NewProcess (NotoValidation.psm1) and IsNew/WaitNew (helpers/single-instance.cs).

.EXAMPLE
  pwsh -NoProfile -File tools/validation/Test-ProcessIdentity.ps1
#>
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'NotoValidation.psm1') -Force
if (-not ('NotoVal.Instance' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'helpers/single-instance.cs') }
$failed = 0
function Check([string] $Case, [bool] $Ok) { if (-not $Ok) { $script:failed++ }; Write-Host ('  [{0}] {1}' -f $(if ($Ok) { 'PASS' } else { 'FAIL' }), $Case) }

$since = Get-Date
$seen = [System.Collections.Generic.HashSet[int]]::new(); [void]$seen.Add(4242)
foreach ($impl in 'Test-NewProcess', 'IsNew') {
    $new = if ($impl -eq 'IsNew') { { param($p, $s) [NotoVal.Instance]::IsNew($seen, $p, $s, $since) } } else { { param($p, $s) Test-NewProcess $seen $p $s $since } }
    Check "$impl : a pid never seen is new" (& $new 1717 $since.AddSeconds(-60))
    Check "$impl : a seen pid that started before the launch is the old process: rejected" (-not (& $new 4242 $since.AddSeconds(-1)))
    Check "$impl : a seen pid that started after the launch is a reused pid, a new process: accepted" (& $new 4242 $since.AddMilliseconds(5))
}
Check 'Test-NewProcess : a seen pid whose start time is unknown is not taken as new' (-not (Test-NewProcess $seen 4242 $null $since))

# WaitNew against a real process, in the order a harness uses it: the launch reference is taken, the process is
# launched and starts, and only then does the wait begin. Its pid is put in Seen to stand for a pid Windows reused.
$name = 'noto-identity-' + [guid]::NewGuid().ToString('N').Substring(0, 6)
$copy = Join-Path ([IO.Path]::GetTempPath()) "$name.exe"
Copy-Item (Join-Path $env:SystemRoot 'System32\PING.EXE') $copy
try {
    $launch = Get-Date
    $p = Start-Process -FilePath $copy -ArgumentList '-n', '30', '127.0.0.1' -WindowStyle Hidden -PassThru
    if (-not (Get-Process -Id $p.Id -ErrorAction SilentlyContinue)) { throw 'the test process did not start' }
    $reused = [System.Collections.Generic.HashSet[int]]::new(); [void]$reused.Add($p.Id)
    $atWait = Get-Date
    $found = [NotoVal.Instance]::WaitNew($name, $reused, 1, 5000, $launch)
    Check 'WaitNew : a reused pid that started after the launch began, before the wait, is found (launch reference)' ($found.Count -eq 1 -and $found[0].Id -eq $p.Id)
    $missed = [NotoVal.Instance]::WaitNew($name, [System.Collections.Generic.HashSet[int]]::new([int[]]@($p.Id)), 1, 500, $atWait)
    Check 'WaitNew : with the wait as the reference (the old order) the same process is missed - why the launch is the reference' ($missed.Count -eq 0)
    $again = [NotoVal.Instance]::WaitNew($name, $reused, 1, 500, (Get-Date))
    Check 'WaitNew : a process this run has already found (seen pid, started before this launch) is not found again' ($again.Count -eq 0)
    $twice = [NotoVal.Instance]::WaitNew($name, [System.Collections.Generic.HashSet[int]]::new(), 2, 500, $launch)
    Check 'WaitNew : one process is never returned twice by one wait' ($twice.Count -eq 1)
    $found + $missed + $again + $twice | ForEach-Object { $_.Dispose() }
}
finally {
    Get-Process -Name $name -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 300
    Remove-Item -LiteralPath $copy -ErrorAction SilentlyContinue
}
Write-Host "process identity: $(if ($failed) { "$failed FAILED" } else { 'ALL PASS' })"
exit [int]($failed -gt 0)
