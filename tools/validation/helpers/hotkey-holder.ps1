# Holds a chord in this process until -StopFile exists, counting the presses it receives. By default
# Ctrl+Alt+Win+Space (MOD_CONTROL|MOD_ALT|MOD_WIN|MOD_NOREPEAT, VK_SPACE); the settings harness passes another.
param([Parameter(Mandatory)] [string] $ReadyFile, [Parameter(Mandatory)] [string] $StopFile, [uint32] $Modifiers = 0x400B, [uint32] $VirtualKey = 0x20)
Add-Type -Name Hold -Namespace NotoValHolder -MemberDefinition @'
[StructLayout(LayoutKind.Sequential)] public struct MSG { public IntPtr h; public uint m; public IntPtr w; public IntPtr l; public uint t; public int x; public int y; }
[DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
[DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
[DllImport("user32.dll")] public static extern bool PeekMessage(out MSG m, IntPtr h, uint min, uint max, uint remove);
'@
$held = [NotoValHolder.Hold]::RegisterHotKey([IntPtr]::Zero, 9, $Modifiers, $VirtualKey)
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
