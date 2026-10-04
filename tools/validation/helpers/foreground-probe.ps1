# Negative control: an unrelated process that has received no input asks for the foreground. Windows must refuse.
param([Parameter(Mandatory)] [int64] $Hwnd, [Parameter(Mandatory)] [string] $Out)
Add-Type -Name Fg -Namespace NotoValProbe -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
'@
$before = [NotoValProbe.Fg]::GetForegroundWindow()
$ok = [NotoValProbe.Fg]::SetForegroundWindow([IntPtr]$Hwnd)
Start-Sleep -Milliseconds 300
Set-Content -LiteralPath $Out -Value @("pid=$PID parent=$((Get-Process -Id $PID).Parent.Id) start=$((Get-Process -Id $PID).StartTime.ToUniversalTime().Ticks)", ('sfw={0} before=0x{1:X} after=0x{2:X}' -f $ok, [int64]$before, [int64][NotoValProbe.Fg]::GetForegroundWindow()), 'done')
