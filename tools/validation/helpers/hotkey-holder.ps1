# Holds Ctrl+Alt+Win+Space in this process until -StopFile exists, counting the presses it receives.
param([Parameter(Mandatory)] [string] $ReadyFile, [Parameter(Mandatory)] [string] $StopFile)
Add-Type -Name Hold -Namespace NotoValHolder -MemberDefinition @'
[StructLayout(LayoutKind.Sequential)] public struct MSG { public IntPtr h; public uint m; public IntPtr w; public IntPtr l; public uint t; public int x; public int y; }
[DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
[DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
[DllImport("user32.dll")] public static extern bool PeekMessage(out MSG m, IntPtr h, uint min, uint max, uint remove);
'@
$held = [NotoValHolder.Hold]::RegisterHotKey([IntPtr]::Zero, 9, 0x2 -bor 0x1 -bor 0x8 -bor 0x4000, 0x20)
Set-Content -LiteralPath $ReadyFile -Value "held=$held"
$hits = 0
$deadline = (Get-Date).AddMinutes(5)
while (-not (Test-Path -LiteralPath $StopFile) -and (Get-Date) -lt $deadline) {
    $m = New-Object NotoValHolder.Hold+MSG
    while ([NotoValHolder.Hold]::PeekMessage([ref]$m, [IntPtr]::Zero, 0x0312, 0x0312, 1)) { $hits++ }
    Start-Sleep -Milliseconds 50
}
[void][NotoValHolder.Hold]::UnregisterHotKey([IntPtr]::Zero, 9)
Add-Content -LiteralPath $ReadyFile -Value "hits=$hits"
