# Negative control: an unrelated process that has received no input asks for the foreground. Windows must refuse.
# Started by the run's probe host (helpers/probe-host.ps1), never by the shell at the moment of the control.
param([Parameter(Mandatory)] [int64] $Hwnd, [Parameter(Mandatory)] [string] $Out)
Add-Type -Name Fg -Namespace NotoValProbe -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
'@
function Owner([IntPtr] $h) { $p = 0; [void][NotoValProbe.Fg]::GetWindowThreadProcessId($h, [ref]$p); "$((Get-Process -Id $p -ErrorAction SilentlyContinue).ProcessName)($p)" }
$me = Get-Process -Id $PID; $parent = $me.Parent
$before = [NotoValProbe.Fg]::GetForegroundWindow(); $beforeOwner = Owner $before
$at = [DateTime]::UtcNow.ToString('HH:mm:ss.fff')
$ok = [NotoValProbe.Fg]::SetForegroundWindow([IntPtr]$Hwnd)
Start-Sleep -Milliseconds 300
$after = [NotoValProbe.Fg]::GetForegroundWindow()
Set-Content -LiteralPath $Out -Value @(
    "pid=$PID parent=$($parent.Id) start=$($me.StartTime.ToUniversalTime().Ticks)",
    ('sfw={0} before=0x{1:X} after=0x{2:X}' -f $ok, [int64]$before, [int64]$after),
    "probe: at=$at parent=$($parent.ProcessName)($($parent.Id)) session=$($me.SessionId) fg-before=$beforeOwner probe-in-front=$($beforeOwner -like "*($PID)") fg-after=$(Owner $after)",
    'done')
