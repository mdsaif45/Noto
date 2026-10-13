# Lists one HKCU key as a shell-launched process sees it: the user's real hive, whatever the caller sees. Read-only.
# Its values, one line each as "name=kind|data" (binary as hex), or with -SubKeys its subkey names; "<key>=absent"
# when there is no such key.
param([Parameter(Mandatory)] [string] $Out, [Parameter(Mandatory)] [string] $Key, [switch] $SubKeys)
$k = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($Key, $false)
$lines = if (-not $k) { '<key>=absent' } else {
    try {
        if ($SubKeys) { $k.GetSubKeyNames() | Sort-Object }
        else {
            foreach ($v in ($k.GetValueNames() | Sort-Object)) {
                $d = $k.GetValue($v, $null, 'DoNotExpandEnvironmentNames')
                "$v=$($k.GetValueKind($v))|$(if ($d -is [byte[]]) { [Convert]::ToHexString($d) } else { "$d" })"
            }
        }
    } finally { $k.Dispose() }
}
Set-Content -LiteralPath $Out -Value (@("pid=$PID parent=$((Get-Process -Id $PID).Parent.Id) start=$((Get-Process -Id $PID).StartTime.ToUniversalTime().Ticks)") + @($lines) + @('done'))
