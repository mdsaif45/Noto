#Requires -Version 7.4
<#
.SYNOPSIS
  #153 spike: does Windows give back the space an application desktop toolbar (SHAppBarMessage) reserved, when the
  process that docked it ends badly? ADR-007 §4 rejected the API on the risk that it does not, until sign-out.

.DESCRIPTION
  A small window of the spike's own (helpers/appbar-spike.ps1, not Noto) docks to the primary display's right edge,
  reserving 300 px, and ends four ways. After each, the work area (SystemParametersInfo SPI_GETWORKAREA, what every
  maximised window fits into) is read until it is whole again, for up to 60 s.

    1  remove     ABM_REMOVE, then exit: the space comes back (the control)
    2  kill       the process killed from outside (a crash nothing in the process can catch)
    3  failfast   Environment.FailFast while docked (a crash as Windows reports one)
    4  exit       an exit that skips ABM_REMOVE (an update, or a bug, that misses the clean path)

  One display only on this machine: the two-monitor half of #153 is NOT RUN. Recorded, not asserted as a product
  verdict: each case passes when the work area is whole again within 60 s, which is what ADR-007 would need.

.EXAMPLE
  pwsh -NoProfile -File tools/validation/Invoke-DockSpike.ps1
#>
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'NotoValidation.psm1') -Force
if (-not ('NotoVal.WorkArea' -as [type])) {
    Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
namespace NotoVal {
    public static class WorkArea {
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
        [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint action, uint param, ref RECT rect, uint flags);
        public static string Now() { var r = new RECT(); SystemParametersInfo(0x0030, 0, ref r, 0); return r.L + "," + r.T + "," + r.R + "," + r.B; }
    }
}
'@
}

$run = New-ValidationRun 'dock-spike'
Write-Host "run $($run.Id)"
$whole = [NotoVal.WorkArea]::Now()
Write-Host "work area before: $whole"
$monitors = @(Get-CimInstance -Namespace root\wmi -ClassName WmiMonitorBasicDisplayParams -ErrorAction SilentlyContinue).Count

function Wait-For([scriptblock] $Condition, [int] $Seconds) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $Seconds) { if (& $Condition) { return [int]$sw.Elapsed.TotalMilliseconds }; Start-Sleep -Milliseconds 100 }
    -1
}

$code = 1
try {
    foreach ($end in 'remove', 'kill', 'failfast', 'exit') {
        $status = Join-Path $run.Root "spike-$end.txt"
        $p = Start-Process powershell.exe -ArgumentList "-NoProfile -ExecutionPolicy Bypass -STA -File `"$(Join-Path $PSScriptRoot 'helpers/appbar-spike.ps1')`" -Status `"$status`" -End $end" -PassThru -WindowStyle Hidden
        [void]$run.Seen.Add($p.Id)
        if ((Wait-For { Test-Path -LiteralPath $status } 30) -lt 0) { throw "the $end spike did not dock" }
        $reserved = [NotoVal.WorkArea]::Now()
        if ($end -eq 'kill') { Start-Sleep -Seconds 2; Stop-Process -Id $p.Id -Force }
        $ms = Wait-For { [NotoVal.WorkArea]::Now() -eq $whole } 60
        $null = Wait-For { $p.HasExited } 10
        Add-Result $run "$end : the work area is whole again after the docked process ends" ($ms -ge 0) "docked=$((Get-Content $status -Raw).Trim()) reserved work area=$reserved -> after=$([NotoVal.WorkArea]::Now()) in $ms ms (exit code $($p.ExitCode))"
        if ($ms -lt 0) { break }
    }
    if ($monitors -lt 2) { Write-Host "  [NOT RUN] two monitors: $monitors display(s) connected" }
}
catch { Add-Invalid $run "aborted: $($_.Exception.Message)" }
finally {
    $after = [NotoVal.WorkArea]::Now()
    Add-Result $run 'hygiene: the work area is as it was before the run' ($after -eq $whole) "before=$whole after=$after"
    foreach ($id in $run.Seen) { Stop-Process -Id $id -Force -ErrorAction SilentlyContinue }
    $code = Complete-ValidationRun $run '#153 spike: does Windows give back an app bar''s space?'
}
exit $code
