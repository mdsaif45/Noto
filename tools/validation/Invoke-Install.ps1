#Requires -Version 7.4
<#
.SYNOPSIS
  Installs, upgrades and uninstalls Noto for real with its Velopack installers (ADR-008), on an isolated data root.

.DESCRIPTION
  Two installers built by tools/packaging/New-Release.ps1: -Setup (the older version) and -Upgrade (a later one). Both
  are run as a user runs them, started by the shell, silent, into this run's folder (--installto), with Noto pointed at
  this run's data root (EXE_ARGS). The user's notes, any installed Noto and the default install folder are never used.

    0  preconditions: the registry is the user's real hive (Setup writes there); no Noto.App uninstall entry, Start
       menu Noto shortcut or Run\Noto value exists before (a run never touches a real one: INVALID otherwise)
    1  clean install -> the install folder, its version, the Apps entry, the Start menu shortcut; Noto started on the
       data root with the welcome of a new workspace, and nothing written outside the run's folders
    2  notes added to the installed Noto's workspace
    3  the later installer run while Noto runs -> the version replaced; the notes intact and shown by the new Noto
    4  Run\Noto registered for the installed Noto (as Start Noto with Windows writes it) -> uninstall removes it
    5  uninstall, as Settings > Apps runs it, while Noto runs -> Noto ended, the install folder, Apps entry and
       shortcut gone; the data root and every note kept
    6  the installed copy carries none of the Windows App SDK parts Noto does not use (AI, ML, search, widgets)

  The prior Run, Uninstall and StartupApproved state is snapshotted first and restored in finally.

.EXAMPLE
  pwsh -NoProfile -File tools/validation/Invoke-InShell.ps1 Invoke-Install -Arguments "-Setup <old>\Noto.App-win-Setup.exe -Upgrade <new>\Noto.App-win-Setup.exe"
#>
param([Parameter(Mandatory)] [string] $Setup, [Parameter(Mandatory)] [string] $Upgrade)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'NotoValidation.psm1') -Force
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$Setup = (Resolve-Path $Setup).Path; $Upgrade = (Resolve-Path $Upgrade).Path

$run = New-ValidationRun 'install'
Write-Host "run $($run.Id)  setup $Setup  upgrade $Upgrade"

$AE = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
$RunPath = 'Software\Microsoft\Windows\CurrentVersion\Run'
$ApprovedPath = 'Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run'
$UninstallPath = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\Noto.App'
$Shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Noto.lnk'
$DefaultInstall = Join-Path $env:LOCALAPPDATA 'Noto.App'
# The Windows App SDK parts Noto does not reference (Directory.Packages.props): never in the installer.
$Unused = '^(onnxruntime|DirectML|DWriteCore|Microsoft\.ML\.|Microsoft\.Asg\.|Microsoft\.Windows\.(Internal\.)?AI\.|Microsoft\.Windows\.Search\.|Microsoft\.Windows\.Widgets\.)'
$FolderId = '01J0000000000000000000F801'
$NoteIds = '01J0000000000000000000N801', '01J0000000000000000000N802', '01J0000000000000000000N803'
$Contents = @{}; foreach ($id in $NoteIds) { $Contents[$id] = "Kept across install, upgrade and uninstall $id" }

function Registry-Snapshot([string] $Path) {
    $k = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($Path, $false)
    if (-not $k) { return [ordered]@{ '<key>' = 'absent' } }
    try {
        $m = [ordered]@{}
        foreach ($v in ($k.GetValueNames() | Sort-Object)) { $d = $k.GetValue($v, $null, 'DoNotExpandEnvironmentNames'); $m[$v] = "$($k.GetValueKind($v))|$(if ($d -is [byte[]]) { [Convert]::ToHexString($d) } else { "$d" })" }
        $m
    } finally { $k.Dispose() }
}
function Same($A, $B) { ($A | ConvertTo-Json -Compress) -eq ($B | ConvertTo-Json -Compress) }
function Uninstall-Entry { $k = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($UninstallPath, $false); if (-not $k) { return $null }; try { $m = @{}; foreach ($v in $k.GetValueNames()) { $m[$v] = $k.GetValue($v) }; $m } finally { $k.Dispose() } }
function Shortcut-Target { if (-not (Test-Path -LiteralPath $Shortcut)) { return $null }; (New-Object -ComObject WScript.Shell).CreateShortcut($Shortcut).TargetPath }
function Installed-Version([string] $Install) {
    $f = Join-Path $Install 'current\sq.version'
    if (-not (Test-Path -LiteralPath $f)) { return $null }
    ([xml](Get-Content -LiteralPath $f -Raw)).package.metadata.version
}
function Assert-RealHive {
    <# Setup writes the Apps entry to HKCU: a harness in a virtualized registry would neither see nor clean it. #>
    $path = "Software\Noto.Validation.RealHive-$($run.Id)"; $token = [guid]::NewGuid().ToString('N')
    try {
        $k = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($path, $true)
        try { $k.SetValue('probe', $token, [Microsoft.Win32.RegistryValueKind]::String) } finally { $k.Dispose() }
        $seen = @(Start-ShortLived $run -Script 'registry-probe.ps1' -Out (Join-Path $run.Root 'registry-probe.txt') -Arguments "-Key `"$path`"")
    }
    finally { [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($path, $false) }
    if ($seen -notcontains "probe=String|$token") { throw "the registry this harness writes is not the user's real hive; run it through Invoke-InShell.ps1" }
}

function Run-Installer([string] $Exe, [string] $Arguments, [string] $Log, [string] $Marker = $script:install, [int] $Seconds = 180) {
    <# Started by the shell, as a double-click, with Velopack's own log kept as evidence; returns once it has exited. #>
    $p = Start-ViaShell $run -File $Exe -Arguments "--log `"$(Join-Path $run.Root $Log)`" --verbose $Arguments" -Marker $Marker -Show 1 -TimeoutSeconds 30
    $proc = Get-Process -Id $p.Pid -ErrorAction SilentlyContinue
    if ($proc -and -not $proc.WaitForExit($Seconds * 1000)) {
        $tail = (Get-Content -LiteralPath (Join-Path $run.Root $Log) -Tail 8 -ErrorAction SilentlyContinue) -join ' | '
        throw "$Exe did not finish within ${Seconds}s; its log ends: $tail"
    }
}
function Listed([string] $Version) { $Version -replace '-.*$', '' }
function Any-Installed { @(Get-CimInstance Win32_Process -Filter "Name = 'Noto.exe'" | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($script:install, [StringComparison]::OrdinalIgnoreCase) }) }
function Find-ById($Hwnd, [string] $Id, [int] $Seconds = 10) {
    $cond = [System.Windows.Automation.PropertyCondition]::new($AE::AutomationIdProperty, $Id)
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) { $e = $AE::FromHandle($Hwnd).FindFirst($Scope::Descendants, $cond); if ($e) { return $e }; Start-Sleep -Milliseconds 200 }
    $null
}
function Folder-Names($Hwnd) {
    $list = Find-ById $Hwnd 'FolderList'
    if (-not $list) { return @() }
    @($list.FindAll($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)) | ForEach-Object { $_.Current.Name })
}
function Notes-Intact {
    $rows = foreach ($id in $NoteIds) { Get-NotoScalar $run -Exe $script:sqlite -DataRoot $script:data -Sql 'SELECT Content FROM Notes WHERE Id = $i AND DeletedAt IS NULL' -Parameters @{ '$i' = $id } }
    $ok = $true; for ($i = 0; $i -lt $NoteIds.Count; $i++) { if ("$($rows[$i])" -ne $Contents[$NoteIds[$i]]) { $ok = $false } }
    $ok
}
function Outside-Run {
    <# What Velopack wrote outside the run: its own log folder, every file with its size. #>
    if (-not (Test-Path -LiteralPath $VelopackLogs)) { return 'absent' }
    (@(Get-ChildItem -LiteralPath $VelopackLogs -Recurse -Force -File | ForEach-Object { "$($_.Name)|$($_.Length)" }) -join ', ')
}

$VelopackLogs = Join-Path $env:LOCALAPPDATA 'velopack'
$before = @{ Run = Registry-Snapshot $RunPath; Approved = Registry-Snapshot $ApprovedPath; Outside = Outside-Run }
exit (Invoke-IsolatedRun $run -Title 'Install, upgrade and uninstall (ADR-008)' -Body {
      try {
        # 0 - nothing of a real install is ever touched
        Assert-RealHive
        if ($before.Run.Contains('Noto')) { throw 'a Run\Noto value already exists; refusing to touch it' }
        if (Uninstall-Entry) { throw "an uninstall entry $UninstallPath already exists: Noto.App is installed; refusing to touch it" }
        if (Test-Path -LiteralPath $Shortcut) { throw "a Start menu shortcut $Shortcut already exists; refusing to touch it" }
        if (Test-Path -LiteralPath $DefaultInstall) { throw "$DefaultInstall exists: Noto.App is installed; refusing to touch it" }
        Add-Result $run '0  real hive; no Noto.App entry, shortcut, default install folder or Run\Noto before' $true "velopack logs before: $($before.Outside)"

        $script:install = Join-Path $run.Root 'install'
        $script:data = Join-Path $run.Root 'data'
        New-Item -ItemType Directory $script:data | Out-Null
        # SQLite for the harness from the package itself, never the install: a library loaded here would stay locked.
        $payload = Join-Path $run.Root 'payload'
        $nupkg = Get-ChildItem (Split-Path $Setup) -Filter '*-full.nupkg' | Select-Object -First 1
        [IO.Compression.ZipFile]::ExtractToDirectory($nupkg.FullName, $payload)
        $script:sqlite = Join-Path $payload 'lib\app\Noto.exe'
        $fromVersion = ([xml](Get-Content (Join-Path $payload 'lib\app\sq.version') -Raw)).package.metadata.version

        # 1 - clean install
        # Silent: Setup never starts the app (Velopack 1.2.161 install.rs), so no Noto ever opens the default data root.
        # Its "-- EXE_ARGS" would point that start here, but panics and hangs in this version (velopack#1039).
        Run-Installer $Setup "--silent --installto `"$script:install`"" -Log 'setup.log'
        Add-Result $run '1  a silent install starts no Noto' ((Any-Installed).Count -eq 0) "running=$((Any-Installed).Count)"
        $entry = Uninstall-Entry; $target = Shortcut-Target
        $files = (Test-Path (Join-Path $script:install 'Update.exe')) -and (Test-Path (Join-Path $script:install 'Noto.exe')) -and (Test-Path (Join-Path $script:install 'current\Noto.exe'))
        Add-Result $run '1  installed: Update.exe, the Noto.exe stub and current\Noto.exe, at the version built' ($files -and (Installed-Version $script:install) -eq $fromVersion) "version=$(Installed-Version $script:install) expected=$fromVersion"
        # Apps shows the version without its pre-release part (Velopack): 0.1.0-test.1 is listed as 0.1.0.
        Add-Result $run '1  an Apps entry for Noto, this version, this folder, a per-user uninstall' ($entry -and $entry.DisplayName -eq 'Noto' -and $entry.DisplayVersion -eq (Listed $fromVersion) -and "$($entry.InstallLocation)".TrimEnd('\') -eq $script:install -and "$($entry.UninstallString)" -match [regex]::Escape($script:install)) "name=$($entry.DisplayName) version=$($entry.DisplayVersion) location=$($entry.InstallLocation) uninstall=$($entry.UninstallString)"
        # The shortcut starts current\Noto.exe itself, a path an upgrade keeps (as it keeps a Run\Noto value's).
        $exe = Join-Path $script:install 'current\Noto.exe'
        $aumid = (New-Object -ComObject Shell.Application).Namespace((Split-Path $Shortcut)).ParseName((Split-Path $Shortcut -Leaf)).ExtendedProperty('System.AppUserModel.ID')
        Add-Result $run '1  a Start menu shortcut to the installed Noto, with the identity the installed Noto takes' ($target -eq $exe -and $aumid -eq 'velopack.Noto.App') "target=$target aumid=$aumid"
        $logs = Outside-Run
        $noto = Start-Noto $run -Exe $target -DataRoot $script:data -KeepWelcome
        $welcome = [bool](Find-ById $noto.Hwnd 'WelcomeRoot' 15)
        Add-Result $run '1  the installed Noto starts: a new workspace''s welcome on the data root' ($welcome -and (Test-Path (Join-Path $script:data 'noto.db'))) "pid=$($noto.Pid) welcome=$welcome"
        Skip-NotoWelcome $noto.Hwnd
        Add-Result $run '1  an ordinary start writes nothing to Velopack''s log' ((Outside-Run) -eq $logs) "before=$logs after=$(Outside-Run)"
        Add-Result $run '1  nothing installed outside the run (no default install folder)' (-not (Test-Path -LiteralPath $DefaultInstall)) "velopack logs: $(Outside-Run)"

        # 2 - notes in the installed Noto's workspace
        if (-not (Stop-Noto $noto)) { throw 'the installed Noto did not close cleanly' }
        $now = [DateTimeOffset]::UtcNow.ToString('O')
        $null = Invoke-NotoSql $run -Exe $script:sqlite -DataRoot $script:data -Sql "INSERT INTO Folders (Id, Name, SortOrder, CreatedAt, UpdatedAt) VALUES (`$f, 'Kept folder', 1, `$t, `$t)" -Parameters @{ '$f' = $FolderId; '$t' = $now }
        $order = 1; foreach ($id in $NoteIds) { $null = Invoke-NotoSql $run -Exe $script:sqlite -DataRoot $script:data -Sql "INSERT INTO Notes (Id, FolderId, Content, SortOrder, CreatedAt, UpdatedAt) VALUES (`$n, `$f, `$c, `$s, `$t, `$t)" -Parameters @{ '$n' = $id; '$f' = $FolderId; '$c' = $Contents[$id]; '$s' = $order++; '$t' = $now } }
        $noto = Start-Noto $run -Exe (Join-Path $script:install 'current\Noto.exe') -DataRoot $script:data
        Add-Result $run '2  the installed Noto shows the workspace''s notes' ((Notes-Intact) -and [bool](@(Folder-Names $noto.Hwnd) -match 'Kept folder')) "folders=$((Folder-Names $noto.Hwnd) -join ', ')"

        # 3 - the later installer while Noto runs
        $toVersion = (& { $z = [IO.Compression.ZipFile]::OpenRead((Get-ChildItem (Split-Path $Upgrade) -Filter '*-full.nupkg' | Select-Object -First 1).FullName); try { $s = $z.GetEntry('lib/app/sq.version').Open(); try { ([xml]([IO.StreamReader]::new($s).ReadToEnd())).package.metadata.version } finally { $s.Dispose() } } finally { $z.Dispose() } })
        Run-Installer $Upgrade "--silent --installto `"$script:install`"" -Log 'upgrade.log'
        $oldGone = -not (Get-Process -Id $noto.Pid -ErrorAction SilentlyContinue)
        $entry = Uninstall-Entry
        Add-Result $run '3  upgraded while running: the running Noto ended, the new version installed and in Apps' ($oldGone -and (Any-Installed).Count -eq 0 -and (Installed-Version $script:install) -eq $toVersion -and $entry.DisplayVersion -eq (Listed $toVersion) -and $toVersion -ne $fromVersion) "$fromVersion -> $(Installed-Version $script:install) (expected $toVersion); Apps $($entry.DisplayVersion); old ended=$oldGone; running after=$((Any-Installed).Count)"
        $logs = Outside-Run
        $noto = Start-Noto $run -Exe $exe -DataRoot $script:data
        Add-Result $run '3  the upgraded Noto starts on the same data root with every note intact and shown' ((Notes-Intact) -and [bool](@(Folder-Names $noto.Hwnd) -match 'Kept folder') -and -not (Find-ById $noto.Hwnd 'WelcomeRoot' 2) -and (Outside-Run) -eq $logs) "folders=$((Folder-Names $noto.Hwnd) -join ', '); velopack logs unchanged=$((Outside-Run) -eq $logs)"
        Add-Result $run '3  one shortcut, still to the installed Noto' ((Shortcut-Target) -eq $exe) "target=$(Shortcut-Target)"

        # 6 - what the installed copy carries
        $carried = @(Get-ChildItem -LiteralPath (Split-Path $exe) -File | Where-Object Name -Match $Unused | ForEach-Object Name)
        $size = (Get-ChildItem -LiteralPath $script:install -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
        Add-Result $run '6  the installed copy carries no Windows App SDK AI, ML, search or widgets library' ($carried.Count -eq 0) ("carried=[{0}] installed size {1:N1} MB" -f ($carried -join ', '), $size)

        # 4 - start at login, as the tray's row writes it for this executable
        $command = "`"$exe`" --login"
        $k = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($RunPath, $true)
        try { $k.SetValue('Noto', $command, [Microsoft.Win32.RegistryValueKind]::String) } finally { $k.Dispose() }

        # 5 - uninstall as Settings > Apps runs it, while Noto runs
        $uninstall = "$($entry.UninstallString)"
        if ($uninstall -notmatch '^"([^"]+)"\s*(.*)$') { throw "an uninstall command not understood: $uninstall" }
        Run-Installer $Matches[1] "--silent $($Matches[2])" -Log 'uninstall.log' -Marker $Matches[1] -Seconds 120
        # Update.exe removes itself a moment after it exits.
        $deadline = (Get-Date).AddSeconds(20); while ((Get-Date) -lt $deadline -and (Test-Path -LiteralPath $script:install)) { Start-Sleep -Milliseconds 500 }
        $runAfter = Registry-Snapshot $RunPath
        Add-Result $run '4  uninstall removed Run\Noto, which started this installed Noto' (-not $runAfter.Contains('Noto')) "command=$command; after: $($runAfter['Noto'])"
        $left = if (Test-Path -LiteralPath $script:install) { @(Get-ChildItem -LiteralPath $script:install -Recurse -Force | ForEach-Object { $_.FullName.Substring($script:install.Length) }) } else { @() }
        Add-Result $run '5  uninstalled: Noto ended, the install folder, Apps entry and shortcut gone' ((Any-Installed).Count -eq 0 -and -not (Test-Path -LiteralPath $script:install) -and -not (Uninstall-Entry) -and -not (Test-Path -LiteralPath $Shortcut)) "running=$((Any-Installed).Count) folder left=$($left.Count) [$((($left | Select-Object -First 5) -join ', '))] entry=$([bool](Uninstall-Entry)) shortcut=$(Test-Path -LiteralPath $Shortcut) uninstall=$uninstall"
        Add-Result $run '5  the data root and every note kept' ((Test-Path (Join-Path $script:data 'noto.db')) -and (Notes-Intact)) "data=$(@(Get-ChildItem -LiteralPath $script:data -Force | ForEach-Object Name) -join ', ')"
        Add-Result $run '5  nothing left outside the run (no default install folder)' (-not (Test-Path -LiteralPath $DefaultInstall)) "velopack logs: $(Outside-Run) (before: $($before.Outside))"
        Add-Result $run '5  Windows'' Startup apps setting byte-identical throughout (never written)' (Same $before.Approved (Registry-Snapshot $ApprovedPath)) ''
      }
      finally {
        # Whatever happened, no Noto from this run's install outlives it, recorded or not (a start that aborted before
        # it was seen is not in $run.Started): a leftover would hold the hotkey every later run needs.
        if ($script:install) {
            $stray = @(Any-Installed)
            $stray | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
            Start-Sleep -Milliseconds 500
            Add-Result $run 'hygiene: no Noto from this run''s install left running' ((Any-Installed).Count -eq 0) "stopped at the end: $($stray.Count)"
        }
        # The Run value, the Apps entry and the shortcut are as they were before this run.
        $k = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($RunPath, $true)
        try { if (-not $before.Run.Contains('Noto')) { $k.DeleteValue('Noto', $false) } } finally { $k.Dispose() }
        if ($script:install -and (Uninstall-Entry) -and "$((Uninstall-Entry).InstallLocation)".TrimEnd('\') -eq $script:install) { [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($UninstallPath, $false) }
        if ($script:install -and (Shortcut-Target) -and (Shortcut-Target).StartsWith($script:install, [StringComparison]::OrdinalIgnoreCase)) { Remove-Item -LiteralPath $Shortcut -Force }
        # Velopack's own log of this run's install hooks: the test's footprint, removed when there was none before.
        if ($before.Outside -eq 'absent' -and (Test-Path -LiteralPath $VelopackLogs)) { Remove-Item -LiteralPath $VelopackLogs -Recurse -Force }
        Add-Result $run 'restored: Run, StartupApproved, Apps entry and shortcut as before' ((Same $before.Run (Registry-Snapshot $RunPath)) -and (Same $before.Approved (Registry-Snapshot $ApprovedPath)) -and -not (Uninstall-Entry) -and -not (Test-Path -LiteralPath $Shortcut)) "Run values $((Registry-Snapshot $RunPath).Count)"
      }
})
