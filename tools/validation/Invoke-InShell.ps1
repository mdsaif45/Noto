#Requires -Version 7.4
<#
.SYNOPSIS
  Runs a validation script in a process the shell (Explorer) starts, then relays its output and exit code.

.DESCRIPTION
  A packaged host (an MSIX app: a terminal, an editor, an agent) virtualizes HKCU writes for every process it starts,
  so a harness started from one reads and writes a private copy of the registry, not the user's. A script started by
  the shell sees the user's real hive, as Noto does. Invoke-LoginStart -RealRegistry checks this itself and refuses
  to run anywhere else.

.EXAMPLE
  pwsh -NoProfile -File tools/validation/Invoke-InShell.ps1 -NotoExe <path>\Noto.exe Invoke-LoginStart -RealRegistry
#>
param([Parameter(Mandatory)] [string] $NotoExe, [Parameter(Mandatory, Position = 0)] [ValidatePattern('^Invoke-[A-Za-z]+$')] [string] $Script,
      [switch] $RealRegistry, [int] $TimeoutMinutes = 30)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'NotoValidation.psm1') -Force

$path = Join-Path $PSScriptRoot "$Script.ps1"
if (-not (Test-Path -LiteralPath $path)) { throw "no validation script $path" }
$NotoExe = (Resolve-Path $NotoExe).Path
$id = [guid]::NewGuid().ToString('N').Substring(0, 12)
$log = Join-Path ([IO.Path]::GetTempPath()) "noto-inshell-$id.log"; $rc = "$log.rc"
$switches = if ($RealRegistry) { '-RealRegistry' } else { '' }
$command = "& '$path' -NotoExe '$NotoExe' $switches *> '$log'; Set-Content -LiteralPath '$rc' `$LASTEXITCODE # noto-inshell-$id"

$view = [NotoVal.ShellLaunch]::DesktopViewDispatch()
$view.Application.ShellExecute('pwsh.exe', "-NoProfile -WindowStyle Hidden -Command `"$command`"", (Split-Path $PSScriptRoot -Parent | Split-Path -Parent), 'open', 0)

$deadline = (Get-Date).AddSeconds(30); $p = $null
while ((Get-Date) -lt $deadline -and -not $p) {
    $p = Get-CimInstance Win32_Process -Filter "Name = 'pwsh.exe'" | Where-Object { $_.CommandLine -and $_.CommandLine.Contains("noto-inshell-$id") } | Select-Object -First 1
    if (-not $p) { Start-Sleep -Milliseconds 250 }
}
if (-not $p) { Write-Host "  [INVALID] the shell did not start $Script"; exit 2 }
$shell = Get-ShellPid
Write-Host "in-shell: $Script pid=$($p.ProcessId) parent=$($p.ParentProcessId) shell=$shell"
$proc = Get-Process -Id $p.ProcessId -ErrorAction SilentlyContinue
if ([int]$p.ParentProcessId -ne $shell) { if ($proc) { Stop-Process -Id $p.ProcessId -Force }; Write-Host "  [INVALID] $Script was not started by the shell"; exit 2 }
if ($proc -and -not $proc.WaitForExit($TimeoutMinutes * 60000)) { Stop-Process -Id $p.ProcessId -Force; Write-Host "  [INVALID] $Script did not finish within $TimeoutMinutes minutes" }

if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log; Remove-Item -LiteralPath $log }
# No exit code (the script threw before its own exit) is INVALID, never a pass.
$code = if ((Test-Path -LiteralPath $rc) -and (Get-Content -LiteralPath $rc -Raw).Trim() -match '^-?\d+$') { [int]$Matches[0] } else { 2 }
Remove-Item -LiteralPath $rc -ErrorAction SilentlyContinue
exit $code
