# Fingerprints the default Noto data folder as a shell-launched process sees it. Names, sizes and hashes only; read-only.
param([Parameter(Mandatory)] [string] $Out)
$root = Join-Path $env:LOCALAPPDATA 'Noto'
$lines = if (Test-Path -LiteralPath $root) {
    Get-ChildItem -LiteralPath $root -Recurse -File | Sort-Object FullName |
        ForEach-Object { '{0}|{1}|{2}' -f $_.FullName.Substring($root.Length), $_.Length, (Get-FileHash -LiteralPath $_.FullName).Hash }
} else { 'absent' }
Set-Content -LiteralPath $Out -Value (@("pid=$PID parent=$((Get-Process -Id $PID).Parent.Id) start=$((Get-Process -Id $PID).StartTime.ToUniversalTime().Ticks)") + @($lines) + @('done'))
