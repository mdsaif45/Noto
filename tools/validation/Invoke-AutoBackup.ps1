#Requires -Version 7.4
<#
.SYNOPSIS
  F15 (#15): Noto backs itself up once a day into the data root's backups, keeping the last seven automatic backups,
  and never touches any other backup there.

.DESCRIPTION
  One isolated data root on a temporary virtual desktop; Noto started by the shell; the real data folders checked
  unchanged. Evidence: the files in the data root's backups, the archive's own entries, and Noto's
  "Noto.Backup automatic ..." trace lines (types only, never content). The first check runs 30 s after a start.

    1  with eight older automatic backups, a backup taken before a restore and one the user saved: a start writes a new
       automatic backup, a whole Noto backup (its manifest and database inside); the seven newest automatic backups
       remain (the two oldest removed); the other two files are untouched
    2  a second start the same day writes none: one is taken a day
    3  with "Back up automatically" off: a start writes none

.EXAMPLE
  pwsh -NoProfile -File tools/validation/Invoke-AutoBackup.ps1 -NotoExe <frozen Release>\Noto.exe
#>
param([Parameter(Mandatory)] [string] $NotoExe)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'NotoValidation.psm1') -Force
if (-not ('NotoVal.Instance' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'helpers/single-instance.cs') }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$NotoExe = (Resolve-Path $NotoExe).Path

$run = New-ValidationRun 'auto-backup'
Write-Host "run $($run.Id)  exe $NotoExe"

function Trace-Mark { @($log.Lines($script:noto.Pid)).Count }
function Trace-Since([int] $Mark) { (@($log.Lines($script:noto.Pid)) | Select-Object -Skip $Mark) -join ' | ' }
function Until([scriptblock] $Condition, [int] $Seconds) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) { if (& $Condition) { return $true }; Start-Sleep -Milliseconds 500 }
    [bool](& $Condition)
}
function Backups { @(Get-ChildItem -LiteralPath $script:backups -File -ErrorAction SilentlyContinue | ForEach-Object Name | Sort-Object) }
function Autos { @(Backups | Where-Object { $_ -like 'auto-*.notobackup' }) }
function Old-Auto([int] $DaysAgo) { "auto-$((Get-Date).AddDays(-$DaysAgo).ToString('yyyy-MM-dd HHmmss', [Globalization.CultureInfo]::InvariantCulture)).notobackup" }
function Entries([string] $Path) {
    $zip = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try { @($zip.Entries | ForEach-Object FullName) } finally { $zip.Dispose() }
}

$log = [NotoVal.DebugLog]::new()
try {
    $code = Invoke-IsolatedRun $run -Title 'F15: automatic backups' -Body {
        $data = Join-Path $run.Root 'data'; New-Item -ItemType Directory $data | Out-Null
        $first = Start-Noto $run -Exe $NotoExe -DataRoot $data
        if (-not (Stop-Noto $first)) { throw 'the seeding start did not exit cleanly' }
        $script:backups = Join-Path $data 'backups'
        Get-ChildItem -LiteralPath $script:backups -File -ErrorAction SilentlyContinue | Remove-Item   # the seeding start's own, if it took one

        # 1 - a start writes one; the seven newest automatic backups are kept; nothing else is touched
        $seeded = @(2..9 | ForEach-Object { Old-Auto $_ })
        New-Item -ItemType Directory $script:backups -Force | Out-Null
        foreach ($name in $seeded) { Set-Content -LiteralPath (Join-Path $script:backups $name) -Value 'seeded' }
        $others = @('before-restore-2026-01-01 000000.notobackup', 'My own backup.notobackup')
        foreach ($name in $others) { Set-Content -LiteralPath (Join-Path $script:backups $name) -Value 'not Noto''s to prune' }

        $script:noto = Start-Noto $run -Exe $NotoExe -DataRoot $data
        $m = Trace-Mark
        $written = Until { (Trace-Since $m) -match 'Noto\.Backup automatic (written|failed)' } 60
        $new = @(Autos | Where-Object { $_ -notin $seeded })
        $entries = if ($new.Count -eq 1) { Entries (Join-Path $script:backups $new[0]) } else { @() }
        $kept = Autos
        $expectedKept = @(@($new) + @($seeded | Select-Object -First 6)) | Sort-Object
        Add-Result $run '1  a start writes one automatic backup, a whole Noto backup (manifest and database inside)' ($written -and (Trace-Since $m) -match 'automatic written' -and $new.Count -eq 1 -and $entries -contains 'manifest.json' -and $entries -contains 'noto.db') "new=$($new -join ',') entries=$($entries.Count) [$(@($entries | Select-Object -First 3) -join ',')]; trace: $(Trace-Since $m)"
        Add-Result $run '1  the seven newest automatic backups remain, the two oldest are removed' ($kept.Count -eq 7 -and (Compare-Object $kept $expectedKept).Count -eq 0) "kept=$($kept.Count): $($kept -join ' | ')"
        Add-Result $run '1  a backup taken before a restore, and one the user saved, are untouched' (@($others | Where-Object { (Get-Content -LiteralPath (Join-Path $script:backups $_) -Raw).Trim() -eq "not Noto's to prune" }).Count -eq 2) "others: $(@(Backups | Where-Object { $_ -notlike 'auto-*' }) -join ' | ')"
        if (-not (Stop-Noto $script:noto)) { throw 'Noto did not exit cleanly' }

        # 2 - a second start the same day writes none
        $before = Autos
        $script:noto = Start-Noto $run -Exe $NotoExe -DataRoot $data
        $m = Trace-Mark
        Start-Sleep -Seconds 45
        Add-Result $run '2  a second start the same day writes none' ((Compare-Object (Autos) $before).Count -eq 0 -and (Trace-Since $m) -notmatch 'Noto\.Backup automatic') "autos=$((Autos).Count); trace: $(Trace-Since $m)"
        if (-not (Stop-Noto $script:noto)) { throw 'Noto did not exit cleanly' }

        # 3 - off: none
        Set-NotoSetting $run -Exe $NotoExe -DataRoot $data -Key 'backup.automatic' -Value 'False'
        Autos | ForEach-Object { Remove-Item -LiteralPath (Join-Path $script:backups $_) }
        $script:noto = Start-Noto $run -Exe $NotoExe -DataRoot $data
        $m = Trace-Mark
        Start-Sleep -Seconds 45
        Add-Result $run '3  with Back up automatically off, a start writes none' ((Autos).Count -eq 0 -and (Trace-Since $m) -notmatch 'Noto\.Backup automatic') "autos=$((Autos).Count); trace: $(Trace-Since $m)"
        Add-Result $run 'teardown: Noto exited cleanly' (Stop-Noto $script:noto) ''
    }
}
finally {
    @($log.All()) | Set-Content (Join-Path $run.Root 'trace.txt')
    $log.Dispose()
}
exit $code
