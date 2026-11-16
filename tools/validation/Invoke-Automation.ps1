#Requires -Version 7.4
<#
.SYNOPSIS
  #192: the noto:// link and the command line (ADR-022), on the real Noto.exe.

.DESCRIPTION
  Every Noto is started by the shell on an isolated data root, with the command line a user, a script or Windows (for
  a link) would give it: the switches, and a bare noto:// argument, which is how Windows passes a link to the
  registered handler. The scheme itself is never registered: only an installed copy's Velopack hooks do that, and
  the run checks the current user's noto key is exactly as it was. A second launch is caught the moment it appears
  and watched to the end (exit code, windows, message boxes), as in Invoke-SingleInstance.ps1. Noto's own
  Noto.Automation and Noto.Instance diagnostics are read from the debug-output channel; what each request made is read
  from the run's own database.

    1  first start, --new-folder Work (no Noto running) -> owns the root, shown, the folder made after the first load
    2  second start, --new-note <text> --folder Work -> hands off (exit 0, no window, kind=Uri); the note is in Work
    3  second start, a bare noto://new link (percent-encoded, two lines, a folder) -> the note in Work, decoded
    4  second start, noto://new-folder?name=Ideas -> the folder Ideas
    5  owner minimized, another app in front + --open-folder Work -> restored, Work's notes open
    6  --folders -> the folder list;  6b noto://open/<Work's id> -> Work's notes, by id
    7  --search <query> -> Search open with the query in its box
    8  a script-like text -> stored as the text it is
    9  refused: an unknown verb, an unknown parameter, a switch with no value -> each hands off as a plain launch
       (kind=Activate), the refusal traced by its reason; nothing is written
    10 first start with a bare noto:// link (the owner closed first) -> the note made once the folder list loads
    11 the current user's noto scheme key is untouched

  Run on a frozen Release build.

.EXAMPLE
  pwsh -NoProfile -File tools/validation/Invoke-Automation.ps1 -NotoExe <path>\Noto.exe
#>
param([Parameter(Mandatory)] [string] $NotoExe)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'NotoValidation.psm1') -Force
$NotoExe = (Resolve-Path $NotoExe).Path

if (-not ('NotoVal.Instance' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'helpers/single-instance.cs') }
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$AE = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]

$run = New-ValidationRun 'automation'
Write-Host "run $($run.Id)  exe $NotoExe"

# ---------------------------------------------------------------- helpers

function Scheme-Key {
    <# The current user's noto scheme as it is now: never written by this run. #>
    $k = 'HKCU:\Software\Classes\noto\shell\open\command'
    if (Test-Path $k) { [string](Get-Item $k).GetValue('') } else { '<absent>' }
}

function Quote([string] $Argument) { '"' + ($Argument -replace '"', '\"') + '"' }

function Launch([string[]] $Extra) {
    <# Starts Noto through the shell with the data root and $Extra, and returns the new process, its handle already open. #>
    Assert-UnderRun $run $data
    $line = (@("--data-root=`"$data`"") + @($Extra | ForEach-Object { Quote $_ })) -join ' '
    $view = [NotoVal.ShellLaunch]::DesktopViewDispatch()
    $since = Get-Date
    $view.Application.ShellExecute($NotoExe, $line, $run.Root, 'open', 1)
    $p = [NotoVal.Instance]::WaitNew('Noto', $run.Seen, 1, 30000, $since)
    if ($p.Count -ne 1) { throw 'the launched Noto never appeared' }
    $proc = $p[0]
    if ([NotoVal.Instance]::ParentOf($proc) -ne (Get-ShellPid)) { $proc.Kill(); throw "Noto $($proc.Id) was not started by the shell" }
    Add-Started $run $proc.Id $proc.StartTime.ToUniversalTime().Ticks
    $proc
}

function Launch-First([string[]] $Extra) {
    <# A first start with $Extra: the owner, once its window is shown. #>
    $proc = Launch $Extra
    $deadline = (Get-Date).AddSeconds(30); $h = [IntPtr]::Zero
    while ((Get-Date) -lt $deadline -and $h -eq [IntPtr]::Zero) {
        Start-Sleep -Milliseconds 250
        $h = [NotoVal.Win32]::TopWindows([uint32]$proc.Id) | Where-Object { [NotoVal.Win32]::Text($_) -eq 'Noto' -and [NotoVal.Win32]::IsWindowVisible($_) } | Select-Object -First 1
        if (-not $h) { $h = [IntPtr]::Zero }
        if ($proc.HasExited) { throw "Noto $($proc.Id) exited during start-up (code $($proc.ExitCode))" }
    }
    if ($h -eq [IntPtr]::Zero) { throw "Noto $($proc.Id) showed no window" }
    Start-Sleep -Seconds 2   # docking completes after the first frame
    [pscustomobject]@{ Pid = $proc.Id; Hwnd = $h; DataRoot = $data }
}

function Launch-Second([string[]] $Extra) {
    <# A second start with $Extra, watched to the end. #>
    $p = Launch $Extra
    $w = [NotoVal.Instance]::Watch($p, 20000, $true)
    $o = @{}; foreach ($kv in $w -split ';') { $k, $v = $kv -split '=', 2; $o[$k] = $v }
    [pscustomobject]@{ Pid = $p.Id; Exited = $o.exited -eq 'True'; Code = $o.code; MaxWindows = [int]$o.maxWindows; DialogMs = [int]$o.dialogMs; Trace = (Trace-Of $p.Id) -join ' | ' }
}

function Handed-Off($s, [string] $Kind) { $s.Exited -and $s.Code -eq '0' -and $s.MaxWindows -eq 0 -and $s.DialogMs -lt 0 -and $s.Trace -match 'outcome=HandedOff' -and $s.Trace -match "kind=$Kind" }

function Second-Detail($s) { "pid=$($s.Pid) exited=$($s.Exited) code=$($s.Code) windows=$($s.MaxWindows) dialogMs=$($s.DialogMs); trace: $($s.Trace)" }

function Trace-Of([int] $ProcessId) { @($log.Lines($ProcessId)) }

function Owner-Trace { (Trace-Of $noto.Pid) -join ' | ' }

function Scalar([string] $Sql, [hashtable] $Parameters = @{}) { Get-NotoScalar $run -Exe $NotoExe -DataRoot $data -Sql $Sql -Parameters $Parameters }

function Folder-Count([string] $Name) { [int](Scalar 'SELECT COUNT(*) FROM Folders WHERE Name = $n AND DeletedAt IS NULL' @{ '$n' = $Name }) }

function Note-Count([string] $Folder, [string] $Content) {
    [int](Scalar 'SELECT COUNT(*) FROM Notes n JOIN Folders f ON f.Id = n.FolderId WHERE f.Name = $f AND n.Content = $c AND n.DeletedAt IS NULL' @{ '$f' = $Folder; '$c' = $Content })
}

function Wait-For([scriptblock] $Condition, [int] $Seconds = 8) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) { if (& $Condition) { return $true }; Start-Sleep -Milliseconds 250 }
    [bool](& $Condition)
}

function By-Id([string] $Id) { $AE::FromHandle($noto.Hwnd).FindFirst($Scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::AutomationIdProperty, $Id)) }

function Is-Shown([string] $Id) { $e = By-Id $Id; [bool]($e -and -not $e.Current.IsOffscreen) }

function Box-Value { $e = By-Id 'SearchBox'; if ($e) { $e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value } else { '<no box>' } }

function Is-Visible { $e = Get-WindowEvidence $noto; $e.WsVisible -and -not $e.Minimized -and $e.Cloaked -eq 0 }

# ---------------------------------------------------------------- run

$log = [NotoVal.DebugLog]::new()
$schemeBefore = Scheme-Key
try {
    $code = Invoke-IsolatedRun $run -Title '#192: noto:// link and command line' -Body {
        if (@(Get-Process -Name Noto -ErrorAction SilentlyContinue).Count) { throw 'a Noto process is already running; close it and rerun' }
        $target = Start-TargetApp $run -Name 'target'

        # A workspace that exists, so the welcome (a new workspace's first steps, which a link never interrupts) is
        # behind it before any case.
        $script:data = Join-Path $run.Root 'data'; New-Item -ItemType Directory $data | Out-Null
        $seed = Start-Noto $run -Exe $NotoExe -DataRoot $data
        if (-not (Stop-Noto $seed)) { throw 'the seeding start did not exit cleanly' }

        # Hiding on deactivation off, so the owner stays shown for UI Automation while the harness has the foreground
        # (Invoke-SingleInstance.ps1 does the same); case 5 minimizes it instead.
        Set-NotoSetting $run -Exe $NotoExe -DataRoot $data -Key 'workspace.hide-on-deactivation' -Value 'False'

        # 1 - first start with a switch
        $script:noto = Launch-First @('--new-folder', 'Work')
        $made = Wait-For { (Folder-Count 'Work') -eq 1 }
        Add-Result $run '1  first start --new-folder Work -> owner, shown, the folder made' ($made -and (Is-Visible) -and (Owner-Trace) -match 'outcome=Owner') "Work=$(Folder-Count 'Work'); trace: $(Owner-Trace)"
        Add-Result $run '1  ... read at start and run once the folder list loaded' ((Owner-Trace) -match 'read verb=NewFolder from=start' -and (Owner-Trace) -match 'run verb=NewFolder') ''

        # 2 - second start with --new-note --folder
        $text2 = 'From the command line: milk & eggs, 50% #1'
        $s = Launch-Second @('--new-note', $text2, '--folder', 'Work')
        $made = Wait-For { (Note-Count 'Work' $text2) -eq 1 }
        Add-Result $run '2  second start --new-note --folder -> handed off as a link (exit 0, no window)' (Handed-Off $s 'Uri') (Second-Detail $s)
        Add-Result $run '2  ... the owner made the note in Work, its exact text' $made "count=$(Note-Count 'Work' $text2)"

        # 3 - second start with a bare link
        $s = Launch-Second @('noto://new?text=First%20line%0ASecond%20line%20%E2%82%AC&folder=work')
        $made = Wait-For { (Note-Count 'Work' "First line`nSecond line €") -eq 1 }
        Add-Result $run '3  bare noto://new link -> handed off; the note in Work (name ignoring case), decoded, two lines' ((Handed-Off $s 'Uri') -and $made) (Second-Detail $s)

        # 4 - a new folder by link
        $s = Launch-Second @('noto://new-folder?name=Ideas')
        $made = Wait-For { (Folder-Count 'Ideas') -eq 1 }
        Add-Result $run '4  noto://new-folder -> the folder Ideas' ((Handed-Off $s 'Uri') -and $made) (Second-Detail $s)

        # 5 - minimized owner, open a folder: Noto shows itself for the request
        $null = Set-ForegroundByKeyboard $target
        $minimized = Invoke-Minimize $noto
        $s = Launch-Second @('--open-folder', 'Work')
        $open = Wait-For { (Is-Visible) -and (Is-Shown 'NoteList') -and (By-Id 'NoteFolderTitle').Current.Name -eq 'Work' }
        Add-Result $run '5  owner minimized + --open-folder Work -> restored, with Work open' ($minimized -and (Handed-Off $s 'Uri') -and $open) "minimized-before=$minimized title=$((By-Id 'NoteFolderTitle').Current.Name); $(Second-Detail $s)"

        # 6 - the folder list, then a folder by its id
        $s = Launch-Second @('--folders')
        $list = Wait-For { (Is-Shown 'FolderList') -and -not (Is-Shown 'NoteList') }
        Add-Result $run '6  --folders -> the folder list' ((Handed-Off $s 'Uri') -and $list) (Second-Detail $s)
        $workId = [string](Scalar "SELECT Id FROM Folders WHERE Name = 'Work' AND DeletedAt IS NULL")
        $s = Launch-Second @("noto://open/$workId")
        $open = Wait-For { (Is-Shown 'NoteList') -and (By-Id 'NoteFolderTitle').Current.Name -eq 'Work' }
        Add-Result $run '6b noto://open/<id> -> Work open, by its id' ((Handed-Off $s 'Uri') -and $open) "id=$workId; $(Second-Detail $s)"

        # 7 - Search with a query
        $s = Launch-Second @('--search', 'milk')
        $searched = Wait-For { (Is-Shown 'SearchRoot') -and (Box-Value) -eq 'milk' }
        Add-Result $run '7  --search milk -> Search open, the query in its box' ((Handed-Off $s 'Uri') -and $searched) "box='$(Box-Value)'; $(Second-Detail $s)"

        # 8 - script-like text is text
        $markup = '<script>alert(1)</script> [x](javascript:alert(1))'
        $s = Launch-Second @('--new-note', $markup, '--folder', 'Ideas')
        $made = Wait-For { (Note-Count 'Ideas' $markup) -eq 1 }
        Add-Result $run '8  a script-like text -> stored as the text it is' ((Handed-Off $s 'Uri') -and $made) (Second-Detail $s)

        # 9 - refused requests: a plain launch, traced, nothing written
        $folders = [int](Scalar 'SELECT COUNT(*) FROM Folders'); $notes = [int](Scalar 'SELECT COUNT(*) FROM Notes'); $deleted = [int](Scalar 'SELECT COUNT(*) FROM Notes WHERE DeletedAt IS NOT NULL')
        foreach ($case in @(
                @{ Name = 'an unknown verb'; Args = @('noto://delete?folder=Work'); Reason = 'UnknownVerb' },
                @{ Name = 'an unknown parameter'; Args = @('noto://new?text=x&path=C:%5CWindows'); Reason = 'UnknownArgument' },
                @{ Name = 'a switch with no value'; Args = @('--new-note'); Reason = 'MissingArgument' })) {
            $s = Launch-Second $case.Args
            Add-Result $run "9  $($case.Name) -> a plain launch (kind=Activate), refused reason=$($case.Reason)" ((Handed-Off $s 'Activate') -and $s.Trace -match "refused reason=$($case.Reason)") (Second-Detail $s)
        }
        Start-Sleep -Seconds 1
        $after = "folders=$(Scalar 'SELECT COUNT(*) FROM Folders') notes=$(Scalar 'SELECT COUNT(*) FROM Notes') binned=$(Scalar 'SELECT COUNT(*) FROM Notes WHERE DeletedAt IS NOT NULL')"
        Add-Result $run '9  ... nothing written' ($after -eq "folders=$folders notes=$notes binned=$deleted" -and (Folder-Count 'Work') -eq 1) "before folders=$folders notes=$notes binned=$deleted; after $after"

        # 10 - first start with a bare link
        if (-not (Stop-Noto $noto)) { throw 'the owner did not exit cleanly' }
        $script:noto = Launch-First @('noto://new?text=Made%20at%20start&folder=Ideas')
        $made = Wait-For { (Note-Count 'Ideas' 'Made at start') -eq 1 }
        Add-Result $run '10 first start with a bare noto:// link -> the note, once the folder list loaded' ($made -and (Owner-Trace) -match 'outcome=Owner' -and (Owner-Trace) -match 'run verb=NewNote') "trace: $(Owner-Trace)"
        $null = Stop-Noto $noto

        # 11 - no start registered the scheme: only an installed copy's hooks do (ADR-008, ADR-022)
        $schemeAfter = Scheme-Key
        Add-Result $run "11 the current user's noto scheme key is untouched" ($schemeAfter -eq $schemeBefore) "before '$schemeBefore', after '$schemeAfter'"
    }
}
finally {
    $log.Dispose()
    Set-Content (Join-Path $run.Root 'trace.txt') ($log.All())
}
exit $code
