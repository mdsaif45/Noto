# The run's probe host: started once by the shell at the beginning of a run, before any scenario, it starts every
# negative-control probe when asked. A probe the shell started at the moment of a control could inherit a foreground
# right the shell had just used (measured: three such grants on 2026-10-12, each right after the shell activated or
# launched something). The host shows no window and takes no input. Requests are files in -Dir:
#   <id>.req = {"Kind":"probe","Hwnd":<n>,"Out":"<path>"}  start a probe, which writes its own result to Out
#   <id>.req = {"Kind":"self","Hwnd":<n>,"Out":"<path>"}   the host asks for the foreground itself (validation of the
#                                                          host only: it must be refused like any probe)
# host.ready is written once the host is listening; host.stop ends it.
param([Parameter(Mandatory)] [string] $Dir, [Parameter(Mandatory)] [string] $Probe)
$ErrorActionPreference = 'Stop'
$me = Get-Process -Id $PID
Set-Content -LiteralPath (Join-Path $Dir 'host.ready') -Value "pid=$PID parent=$($me.Parent.Id) start=$($me.StartTime.ToUniversalTime().Ticks) session=$($me.SessionId)"
while (-not (Test-Path -LiteralPath (Join-Path $Dir 'host.stop'))) {
    foreach ($req in @(Get-ChildItem -LiteralPath $Dir -Filter '*.req' -File)) {
        $r = Get-Content -LiteralPath $req.FullName -Raw | ConvertFrom-Json
        Remove-Item -LiteralPath $req.FullName
        if ($r.Kind -eq 'probe') {
            $si = [Diagnostics.ProcessStartInfo]::new('pwsh.exe')
            foreach ($a in '-NoProfile', '-WindowStyle', 'Hidden', '-File', $Probe, '-Hwnd', "$($r.Hwnd)", '-Out', $r.Out) { $si.ArgumentList.Add($a) }
            $si.UseShellExecute = $false; $si.CreateNoWindow = $true
            [void][Diagnostics.Process]::Start($si)
        }
        elseif ($r.Kind -eq 'self') {
            if (-not ('NotoValHost.Fg' -as [type])) {
                Add-Type -Name Fg -Namespace NotoValHost -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
'@
            }
            $before = [NotoValHost.Fg]::GetForegroundWindow()
            $ok = [NotoValHost.Fg]::SetForegroundWindow([IntPtr][int64]$r.Hwnd)
            Start-Sleep -Milliseconds 300
            Set-Content -LiteralPath $r.Out -Value @(
                "pid=$PID parent=$($me.Parent.Id) start=$($me.StartTime.ToUniversalTime().Ticks)",
                ('sfw={0} before=0x{1:X} after=0x{2:X}' -f $ok, [int64]$before, [int64][NotoValHost.Fg]::GetForegroundWindow()),
                "probe: the host itself, session=$($me.SessionId)", 'done')
        }
    }
    Start-Sleep -Milliseconds 50
}
