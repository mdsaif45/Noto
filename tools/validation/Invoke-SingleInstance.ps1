#Requires -Version 7.4
<#
.SYNOPSIS
  A17: single instance and activation handoff, on the real Noto.exe.

.DESCRIPTION
  Every Noto is started by the shell on an isolated data root. A second launch is caught the moment it appears
  (its handle opened at once, so its exit code survives it), and watched until it exits: its exit code, every
  visible window it showed, and whether a message box appeared. Noto's own diagnostics (Trace, read through the
  debug-output channel) name each launch's claim, the pipe's status, each launch request's action and the moment
  shutdown begins. The mutex and pipe are probed by name, from the harness's own independent computation of the
  key. Every claim that Noto took the foreground is bracketed by a negative control; any grant makes the run INVALID.

    1  first launch -> exactly one owner: mutex held, pipe served by it, one window
    2  second launch -> exits 0, never shows a window, no second process; the owner is activated
    2b ... and the second launch never opens the database (it is locked exclusively throughout)
    3  hidden owner -> shown, docked, in front, keyboard focus
    4  minimized owner -> restored, docked, in front
    5  owner behind another app -> brought forward;  5b owner already in front -> left as it is (never hidden)
    6  owner shown on another virtual desktop -> Windows switches to it; Noto in front
    7  hidden owner, launch from another desktop -> shown on the CURRENT desktop; no switch
    8  second launch during the owner's startup -> one process survives, shown
    9  five simultaneous launches -> exactly one owner; the other four hand off (exit 0); one window
    10 launch during the owner's shutdown -> it waits, then takes over (owner frozen at the instant shutdown began)
    11 kill the owner -> the next launch owns at once;  12 no mutex or pipe left behind by the dead owner
    13 owner unreachable (process suspended) -> the second launch gives up after about 5 s, message box, exit 2
    14 malformed requests -> Rejected; the owner is not activated and stays healthy
    15 oversized requests -> never accepted; the owner stays healthy
    16 clean shutdown -> exit 0; mutex and pipe gone;  17 no Noto process left
    18 negative control: an unrelated process cannot take the mutex or create the pipe while the owner lives
    19 pipe squatting -> detected and recorded by the owner; no second workspace
    20 unauthorized clients (user SID disabled, low integrity, anonymous) -> refused by the pipe's ACL;
       a pipe served by another user (a real Windows system pipe) -> refused by Noto's client before sending
    21 unsupported version -> Rejected;  22 unknown and reserved kinds -> Rejected

  The elevated launch (23) needs a UAC approval and is a separate script: Invoke-SingleInstanceElevated.ps1.

.EXAMPLE
  pwsh -NoProfile -File tools/validation/Invoke-SingleInstance.ps1 -NotoExe <path>\Noto.exe
#>
param([Parameter(Mandatory)] [string] $NotoExe)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'NotoValidation.psm1') -Force
$NotoExe = (Resolve-Path $NotoExe).Path

if (-not ('NotoVal.Instance' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'helpers/single-instance.cs') }

$run = New-ValidationRun 'single-instance'
Write-Host "run $($run.Id)  exe $NotoExe"

# ---------------------------------------------------------------- helpers

function Fg { [NotoVal.Win32]::GetForegroundWindow() }
function Ev($Noto) { Get-WindowEvidence $Noto }
function Show-State($e) { "visible=$($e.WsVisible) minimized=$($e.Minimized) cloaked=$($e.Cloaked) fg=$($e.IsForeground)(pid $($e.ForegroundPid)) focus=$($e.KeyboardFocus) outer=$($e.Outer)" }
function Is-Shown-Docked($e) { $e.WsVisible -and -not $e.Minimized -and $e.Cloaked -eq 0 -and (Test-DockedAt $e 'Right') }

function Locked([string] $Label) { $null = Assert-ForegroundLocked $run -A $noto.Hwnd -B $target.Hwnd -Label $Label }

function Focus-Shell {
    <#
      What a user does to launch Noto: act in the shell. A real click on an empty patch of the desktop makes the
      shell (explorer) the foreground process, so what it launches may take the foreground and pass it on.
      Refused unless the point is the desktop itself.
    #>
    $mi = New-Object NotoVal.Win32+MONITORINFO; $mi.cbSize = [Runtime.InteropServices.Marshal]::SizeOf($mi)
    [void][NotoVal.Win32]::GetMonitorInfo([NotoVal.Win32]::MonitorFromWindow([NotoVal.Win32]::FindWindow('Shell_TrayWnd', [NullString]::Value), 1), [ref]$mi)
    $w = $mi.rcWork
    foreach ($fx in 0.35, 0.5, 0.25, 0.6) {
        foreach ($fy in 0.92, 0.8, 0.6) {
            $x = [int]($w.Left + ($w.Right - $w.Left) * $fx); $y = [int]($w.Top + ($w.Bottom - $w.Top) * $fy)
            if (@('Progman', 'WorkerW') -notcontains [NotoVal.Instance]::RootClassAt($x, $y)) { continue }
            [void][NotoVal.Win32]::SetCursorPos($x, $y); Start-Sleep -Milliseconds 150
            if (@('Progman', 'WorkerW') -notcontains [NotoVal.Instance]::RootClassAt($x, $y)) { continue }
            [NotoVal.Win32]::mouse_event(2, 0, 0, 0, [IntPtr]::Zero); [NotoVal.Win32]::mouse_event(4, 0, 0, 0, [IntPtr]::Zero)
            Start-Sleep -Milliseconds 400
            $fgPid = 0; [void][NotoVal.Win32]::GetWindowThreadProcessId((Fg), [ref]$fgPid)
            if ($fgPid -eq (Get-ShellPid)) { return }
        }
    }
    throw 'could not make the shell the foreground with a desktop click'
}

function Wait-Presence($Noto, [scriptblock] $Condition, [int] $Seconds = 5) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) { $e = Ev $Noto; if (& $Condition $e) { return $e }; Start-Sleep -Milliseconds 150 }
    Ev $Noto
}

function Root-Processes([string] $Data) {
    @(Get-CimInstance Win32_Process -Filter "Name='Noto.exe'" | Where-Object { $_.CommandLine -and $_.CommandLine.Contains("--data-root=`"$Data`"") })
}

function Workspace-Windows([string] $Data) {
    @(Root-Processes $Data | ForEach-Object { [NotoVal.Instance]::VisibleWindows([int]$_.ProcessId, $false) | Where-Object { [NotoVal.Win32]::Text($_) -eq 'Noto' } }).Count
}

function Trace-Of([int] $ProcessId) { @($log.Lines($ProcessId)) }

function Launch([string] $Data) {
    <# Starts Noto through the shell and returns the new process, its handle already open. #>
    Assert-UnderRun $run $Data
    $view = [NotoVal.ShellLaunch]::DesktopViewDispatch()
    $view.Application.ShellExecute($NotoExe, "--data-root=`"$Data`"", $run.Root, 'open', 1)
    $p = [NotoVal.Instance]::WaitNew('Noto', $run.Seen, 1, 30000)
    if ($p.Count -ne 1) { throw 'the launched Noto never appeared' }
    $proc = $p[0]
    if ([NotoVal.Instance]::ParentOf($proc) -ne (Get-ShellPid)) { $proc.Kill(); throw "Noto $($proc.Id) was not started by the shell" }
    Add-Started $run $proc.Id $proc.StartTime.ToUniversalTime().Ticks
    $proc
}

function Launch-Second([string] $Data, [int] $TimeoutMs = 20000, [bool] $Dismiss = $true, [switch] $FromBackground) {
    <#
      A second launch, watched to the end. Returns its outcome. By default the user's act is modelled: the shell is
      made the foreground by a real desktop click first. -FromBackground launches with no user act at all.
    #>
    if (-not $FromBackground) { Focus-Shell }
    $p = Launch $Data
    $w = [NotoVal.Instance]::Watch($p, $TimeoutMs, $Dismiss)
    $o = @{}; foreach ($kv in $w -split ';') { $k, $v = $kv -split '=', 2; $o[$k] = $v }
    [pscustomobject]@{ Pid = $p.Id; Exited = $o.exited -eq 'True'; Code = $o.code; Ms = [int]$o.ms; MaxWindows = [int]$o.maxWindows; DialogMs = [int]$o.dialogMs; Dialog = $o.dialog; Raw = $w; Trace = (Trace-Of $p.Id) -join ' | ' }
}

function Second-Detail($s) { "pid=$($s.Pid) exited=$($s.Exited) code=$($s.Code) in $($s.Ms) ms, windows shown=$($s.MaxWindows), dialog=$(if ($s.DialogMs -ge 0) { "'$($s.Dialog)' at $($s.DialogMs) ms" } else { 'none' }); trace: $($s.Trace)" }

function Handed-Off($s) { $s.Exited -and $s.Code -eq '0' -and $s.MaxWindows -eq 0 -and $s.DialogMs -lt 0 -and $s.Trace -match 'outcome=HandedOff' }

function Owner-Identity($Noto, [string] $Pipe) {
    $server = [NotoVal.Instance]::PipeServerPid($Pipe)
    $user = (Get-CimInstance Win32_Process -Filter "ProcessId=$($Noto.Pid)" | Invoke-CimMethod -MethodName GetOwnerSid).Sid
    [pscustomobject]@{ Pid = $Noto.Pid; PipeServer = $server; Sid = $user; Mutex = [NotoVal.Instance]::MutexExists($Pipe); Pipe = [NotoVal.Instance]::PipeExists($Pipe) }
}

function Identity-Detail($i) { "owner pid=$($i.Pid) sid=$($i.Sid) pipe-server pid=$($i.PipeServer) mutex=$($i.Mutex) pipe=$($i.Pipe)" }

function Bring-NotoForward($Noto, $Target) {
    if ((Fg) -eq $Noto.Hwnd) { return }
    $null = Set-ForegroundByKeyboard $Target
    Send-ActivationChord -AllowedForeground @($Target.Hwnd)
    if ((Fg) -ne $Noto.Hwnd) { throw 'the hotkey did not bring Noto forward' }
}

function Hide-Noto($Noto, $Target) {
    Bring-NotoForward $Noto $Target
    Send-ActivationChord -AllowedForeground @($Noto.Hwnd)
    $h = Wait-Presence $Noto { param($e) -not $e.WsVisible }
    if ($h.WsVisible) { throw 'Noto did not hide' }
}

function Desktop-Text { $d = Get-DesktopState; "current=$($d.Current) count=$($d.All.Count)" }

# ---------------------------------------------------------------- run

$log = [NotoVal.DebugLog]::new()
try {
    $code = Invoke-IsolatedRun $run -Title 'A17: single instance and activation handoff' -Body {
        Assert-ChordFree $run
        if (@(Get-Process -Name Noto -ErrorAction SilentlyContinue).Count) { throw 'a Noto process is already running; close it and rerun' }
        $target = Start-TargetApp $run -Name 'target'
        $bystander = Start-TargetApp $run -Name 'bystander'
        $null = Set-ForegroundByKeyboard $bystander

        $data = Join-Path $run.Root 'data'; New-Item -ItemType Directory $data | Out-Null
        $pipe = [NotoVal.Instance]::PipeName($data)
        Write-Host "  key: $pipe"
        if ([NotoVal.Instance]::MutexExists($pipe) -or [NotoVal.Instance]::PipeExists($pipe)) { throw 'the fresh root already has a mutex or pipe' }

        # 1 - first launch
        $noto = Start-Noto $run -Exe $NotoExe -DataRoot $data
        $id = Owner-Identity $noto $pipe
        $procs = Root-Processes $data
        Add-Result $run '1  first launch -> one owner: mutex held, pipe served by the owner' ($id.Mutex -and $id.Pipe -and $id.PipeServer -eq $noto.Pid -and $id.Sid -eq ([Security.Principal.WindowsIdentity]::GetCurrent().User.Value)) (Identity-Detail $id)
        Add-Result $run '1  ... exactly one Noto process and one workspace window' ($procs.Count -eq 1 -and (Workspace-Windows $data) -eq 1) "processes=$($procs.Count) windows=$(Workspace-Windows $data)"
        Add-Result $run '1  ... its own trace: claim Owner, pipe Started' (((Trace-Of $noto.Pid) -join ' ') -match 'outcome=Owner' -and ((Trace-Of $noto.Pid) -join ' ') -match 'pipe status=Started') ((Trace-Of $noto.Pid) -join ' | ')

        # 18 - negative control: an unrelated process cannot take ownership
        Add-Result $run '18 an unrelated process cannot take the mutex while the owner lives' (-not [NotoVal.Instance]::TryTakeMutex($pipe)) ''
        $pipeErr = [NotoVal.Instance]::TryCreatePipe($pipe)
        Add-Result $run '18 ... nor create an instance of the pipe beside it' ($pipeErr -ne 0) "CreateNamedPipe error $pipeErr"

        # 20 - clients the ACL must refuse
        foreach ($who in 'no-user', 'low', 'anonymous') {
            $err = [NotoVal.Instance]::OpenAs($pipe, $who)
            Add-Result $run "20 a client running as '$who' is refused by the pipe" ($err -eq 5) "CreateFile error $err (5 = access denied)"
        }

        # 5 / 2 - owner behind another app
        $null = Set-ForegroundByKeyboard $target
        Locked '5 before'
        $s = Launch-Second $data
        $e = Wait-Presence $noto { param($e) $e.IsForeground }
        if ($e.IsForeground) { Locked '5 after' }   # bracket a foreground claim; a failed one needs none
        Add-Result $run '2  second launch -> exit 0, no window, no message box, handed off' (Handed-Off $s) (Second-Detail $s)
        Add-Result $run '2  ... still exactly one Noto process and one window' ((Root-Processes $data).Count -eq 1 -and (Workspace-Windows $data) -eq 1) ''
        Add-Result $run '5  owner behind another app -> brought forward, docked, keyboard focus' ((Is-Shown-Docked $e) -and $e.IsForeground -and $e.KeyboardFocus) (Show-State $e)
        Add-Result $run '5  ... the owner traced the request as Focus' (((Trace-Of $noto.Pid) -join ' ') -match 'launch action=Focus') ''

        # 5b - already in front: never hidden. Launched with no user act, so Noto stays in front until it arrives.
        if ((Fg) -ne $noto.Hwnd) { Bring-NotoForward $noto $target }
        $s = Launch-Second $data -FromBackground
        Start-Sleep -Milliseconds 800
        $e = Ev $noto
        Add-Result $run '5b owner already in front + launch -> left as it is, never hidden' ((Handed-Off $s) -and (Is-Shown-Docked $e) -and $e.IsForeground) "$(Show-State $e); $(Second-Detail $s)"
        Add-Result $run '5b ... the owner traced the request as None' (((Trace-Of $noto.Pid) -join ' ') -match 'launch action=None') ''

        # 4 - minimized
        $null = Invoke-Minimize $noto
        $null = Set-ForegroundByKeyboard $target
        Locked '4 before'
        $s = Launch-Second $data
        $e = Wait-Presence $noto { param($e) -not $e.Minimized -and $e.IsForeground }
        if ($e.IsForeground) { Locked '4 after' }   # bracket a foreground claim; a failed one needs none
        Add-Result $run '4  minimized owner + launch -> restored, docked, in front, focus' ((Handed-Off $s) -and (Is-Shown-Docked $e) -and $e.IsForeground -and $e.KeyboardFocus) "$(Show-State $e); $(Second-Detail $s)"

        # 3 - hidden
        Hide-Noto $noto $target
        $null = Set-ForegroundByKeyboard $target
        Locked '3 before'
        $s = Launch-Second $data
        $e = Wait-Presence $noto { param($e) $e.WsVisible -and $e.IsForeground }
        if ($e.IsForeground) { Locked '3 after' }   # bracket a foreground claim; a failed one needs none
        Add-Result $run '3  hidden owner + launch -> shown, docked, in front, focus' ((Handed-Off $s) -and (Is-Shown-Docked $e) -and $e.IsForeground -and $e.KeyboardFocus) "$(Show-State $e); $(Second-Detail $s)"

        # 2b - the second launch never opens the database. The owner keeps it open (connection pool), so it cannot
        # be locked; instead a temporary deny entry on this run's own database file refuses every NEW open while
        # the owner's existing handles go on working. A launch that opened the database first would crash.
        $null = Set-ForegroundByKeyboard $target
        $db = Join-Path $data 'noto.db'; Assert-UnderRun $run $db
        $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
        $null = & icacls.exe $db /deny "*${sid}:(R,W)"
        $probe = try { [IO.File]::Open($db, 'Open', 'Read', 'ReadWrite').Dispose(); 'opened' } catch { 'refused' }
        try { $s = Launch-Second $data } finally { $null = & icacls.exe $db /remove:d "*$sid" }
        $after = try { [IO.File]::Open($db, 'Open', 'Read', 'ReadWrite').Dispose(); 'opened' } catch { 'refused' }
        Add-Result $run '2b database closed to new opens -> the second launch still hands off (exit 0)' ($probe -eq 'refused' -and (Handed-Off $s) -and $after -eq 'opened') "new open while denied: $probe; after: $after; $(Second-Detail $s)"

        # 14 / 15 / 21 / 22 - malformed requests, while the owner is hidden
        Hide-Noto $noto $target
        $bad = [ordered]@{
            '14 bad magic'                 = [byte[]](@(0x58, 0x4F, 0x54, 0x4F, 1, 1, 0, 0))
            '14 truncated header'          = [byte[]](@(0x4E, 0x4F, 0x54, 0x4F, 1))
            '14 length longer than sent'   = [NotoVal.Instance]::Frame(1, 1, [byte[]]@(), 10)
            '14 activate with a payload'   = [NotoVal.Instance]::Frame(1, 1, [Text.Encoding]::UTF8.GetBytes('x'), 1)
            '21 unsupported version 2'     = [NotoVal.Instance]::Frame(2, 1, [byte[]]@(), 0)
            '21 unsupported version 0'     = [NotoVal.Instance]::Frame(0, 1, [byte[]]@(), 0)
            '22 unknown kind 9'            = [NotoVal.Instance]::Frame(1, 9, [byte[]]@(), 0)
            '22 reserved kind 2 (Uri)'     = [NotoVal.Instance]::Frame(1, 2, [Text.Encoding]::UTF8.GetBytes('noto://x'), 8)
            '22 reserved kind 3 (Cli)'     = [NotoVal.Instance]::Frame(1, 3, [Text.Encoding]::UTF8.GetBytes('--x'), 3)
            '15 oversized: 8193 bytes'     = [NotoVal.Instance]::Frame(1, 1, [byte[]]::new(8185), 8185)
            '15 oversized: 64 KiB'         = [NotoVal.Instance]::Frame(1, 1, [byte[]]::new(65528), 65528)
        }
        foreach ($k in $bad.Keys) {
            $reply = [NotoVal.Instance]::SendRaw($pipe, $bad[$k])
            Start-Sleep -Milliseconds 300
            $e = Ev $noto
            $ok = if ($k -like '15*') { $reply -eq 2 -or $reply -eq -1 } else { $reply -eq 2 }
            Add-Result $run "$k -> Rejected; owner alive and NOT activated (still hidden)" ($ok -and -not $e.WsVisible -and [bool](Get-Process -Id $noto.Pid -ErrorAction SilentlyContinue)) "reply=$reply (2 = Rejected, -1 = cut off) $(Show-State $e)"
        }
        $null = Set-ForegroundByKeyboard $target
        $s = Launch-Second $data
        $e = Wait-Presence $noto { param($e) $e.WsVisible -and $e.IsForeground }
        Add-Result $run '14 ... afterwards a real launch still hands off and shows the owner' ((Handed-Off $s) -and (Is-Shown-Docked $e) -and $e.IsForeground) "$(Show-State $e); $(Second-Detail $s)"

        # 6 - owner shown on desktop A, launch from desktop B
        $homeDesk = (Get-DesktopState).Current; $countBefore = (Get-DesktopState).All.Count
        $null = Set-ForegroundByKeyboard $target
        Send-ShellChord $run @($VK.LWIN, $VK.CTRL) $VK.D
        $other = (Get-DesktopState).Current
        if ($other -eq $homeDesk) { Add-Invalid $run '6 no second temporary desktop was created' }
        else {
            $cloakedThere = (Ev $noto).Cloaked
            $s = Launch-Second $data
            $e = Wait-Presence $noto { param($e) $e.IsForeground }
            $now6 = (Get-DesktopState).Current
            Add-Result $run '6  owner shown on desktop A + launch from desktop B -> Windows switches to A' ((Handed-Off $s) -and $cloakedThere -ne 0 -and $now6 -eq $homeDesk) "cloaked-on-B=$cloakedThere home=$homeDesk now=$now6; $(Second-Detail $s)"
            Add-Result $run '6  ... and Noto is in front, docked' ((Is-Shown-Docked $e) -and $e.IsForeground) (Show-State $e)
            if ((Get-DesktopState).All -contains $other) { Step-ToDesktop $run $other; if ((Get-DesktopState).Current -eq $other) { Send-ShellChord $run @($VK.LWIN, $VK.CTRL) $VK.F4 } }
            Step-ToDesktop $run $homeDesk
        }

        # 7 - hidden owner, launch from another desktop
        Hide-Noto $noto $target
        Send-ShellChord $run @($VK.LWIN, $VK.CTRL) $VK.D
        $other = (Get-DesktopState).Current
        if ($other -eq $homeDesk) { Add-Invalid $run '7 no second temporary desktop was created' }
        else {
            $s = Launch-Second $data
            $e = Wait-Presence $noto { param($e) $e.WsVisible -and $e.IsForeground }
            $now7 = (Get-DesktopState).Current
            Add-Result $run '7  hidden owner + launch from desktop B -> shown on B, no switch' ((Handed-Off $s) -and $now7 -eq $other -and $e.Cloaked -eq 0) "home=$homeDesk other=$other now=$now7; $(Second-Detail $s)"
            Add-Result $run '7  ... docked and in front' ((Is-Shown-Docked $e) -and $e.IsForeground) (Show-State $e)
            if ((Get-DesktopState).All -contains $other) { Step-ToDesktop $run $other; if ((Get-DesktopState).Current -eq $other) { Send-ShellChord $run @($VK.LWIN, $VK.CTRL) $VK.F4 } }
            Step-ToDesktop $run $homeDesk
            Add-Result $run '7  cleanup: back on the run desktop, the extra desktop gone' (((Get-DesktopState).Current -eq $homeDesk) -and ((Get-DesktopState).All.Count -eq $countBefore)) (Desktop-Text)
        }

        # 13 - owner unreachable: its process suspended
        $null = Set-ForegroundByKeyboard $target
        [NotoVal.Instance]::Suspend($noto.Pid)
        try { $s = Launch-Second $data -TimeoutMs 30000 } finally { [NotoVal.Instance]::Resume($noto.Pid) }
        Add-Result $run '13 owner suspended -> message box after about 5 s, then exit 2' ($s.Exited -and $s.Code -eq '2' -and $s.DialogMs -ge 4500 -and $s.DialogMs -le 9000 -and $s.MaxWindows -eq 0) (Second-Detail $s)
        Add-Result $run '13 ... the message says the running Noto could not be reached' ($s.Dialog -match 'could not be reached') "'$($s.Dialog)'"
        Start-Sleep -Seconds 1
        Add-Result $run '13 ... the owner is alive and responsive once resumed; still one process' (-not (Get-Process -Id $noto.Pid).HasExited -and (Root-Processes $data).Count -eq 1 -and [NotoVal.Instance]::PipeServerPid($pipe) -eq $noto.Pid) ''

        # 10 - launch during the owner's shutdown: the owner's UI thread is frozen the instant shutdown begins.
        # Its pipe thread keeps answering, so the new launch is told ShuttingDown and waits on the mutex.
        $null = Set-ForegroundByKeyboard $target
        $old = Get-Process -Id $noto.Pid; $null = $old.Handle
        $ownerPid = 0; $uiThread = [int][NotoVal.Win32]::GetWindowThreadProcessId($noto.Hwnd, [ref]$ownerPid)
        $log.FreezeOn($noto.Pid, $uiThread, 'shutting-down')
        [void][NotoVal.Win32]::PostMessage($noto.Hwnd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
        if (-not $log.Frozen.Wait(10000)) { throw 'the owner never traced shutting-down' }
        $frozenPipe = [NotoVal.Instance]::PipeExists($pipe); $frozenMutex = [NotoVal.Instance]::MutexExists($pipe)
        try {
            $next = Launch $data
            Start-Sleep -Seconds 3
            $waiting = -not $next.HasExited -and [NotoVal.Instance]::VisibleWindows($next.Id, $false).Count -eq 0
        }
        finally { [NotoVal.Instance]::ResumeThreadId($uiThread) }
        $oldExited = $old.WaitForExit(10000)
        $h10 = [IntPtr]::Zero; $deadline = (Get-Date).AddSeconds(20)
        while ((Get-Date) -lt $deadline -and $h10 -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 200; $h10 = [NotoVal.Instance]::VisibleWindows($next.Id, $false) | Where-Object { [NotoVal.Win32]::Text($_) -eq 'Noto' } | Select-Object -First 1; if (-not $h10) { $h10 = [IntPtr]::Zero } }
        Start-Sleep -Seconds 2
        $trace10 = (Trace-Of $next.Id) -join ' | '
        Add-Result $run '10 owner frozen mid-shutdown: mutex and pipe still held' ($frozenPipe -and $frozenMutex) "pipe=$frozenPipe mutex=$frozenMutex"
        Add-Result $run '10 ... the new launch waits (alive, no window) instead of exiting' $waiting ''
        Add-Result $run '10 ... the old owner exits cleanly once resumed (code 0)' ($oldExited -and $old.ExitCode -eq 0) "exited=$oldExited code=$(if ($oldExited) { $old.ExitCode })"
        Add-Result $run '10 ... the new launch takes over: claim Owner, tookOver=True, one window' ($h10 -ne [IntPtr]::Zero -and $trace10 -match 'outcome=Owner' -and $trace10 -match 'tookOver=True' -and (Root-Processes $data).Count -eq 1) "trace: $trace10"
        Add-Result $run '10 ... a clean handover: the old owner let go (recovered=False), not abandoned at exit' ($trace10 -match 'recovered=False') ''
        $noto = [pscustomobject]@{ Pid = $next.Id; Hwnd = $h10; DataRoot = $data }
        $id = Owner-Identity $noto $pipe
        Add-Result $run '10 ... and it now serves the pipe' ($id.PipeServer -eq $next.Id -and $id.Mutex) (Identity-Detail $id)

        # 11 / 12 - kill the owner
        Stop-Process -Id $noto.Pid -Force
        Start-Sleep -Milliseconds 500
        $leftPipe = [NotoVal.Instance]::PipeExists($pipe); $leftMutex = [NotoVal.Instance]::MutexExists($pipe)
        Add-Result $run '12 killed owner leaves no pipe and no mutex behind' (-not $leftPipe -and -not $leftMutex) "pipe=$leftPipe mutex=$leftMutex"
        $clock = [Diagnostics.Stopwatch]::StartNew()
        $noto = Start-Noto $run -Exe $NotoExe -DataRoot $data
        $clock.Stop()
        $id = Owner-Identity $noto $pipe
        Add-Result $run '11 the next launch owns at once and shows its window' ($id.PipeServer -eq $noto.Pid -and $id.Mutex -and ((Trace-Of $noto.Pid) -join ' ') -match 'outcome=Owner') "window after $($clock.ElapsedMilliseconds) ms (incl. 2 s settle); $(Identity-Detail $id); trace: $((Trace-Of $noto.Pid) -join ' | ')"

        # 16 / 17 - clean shutdown
        $p16 = Get-Process -Id $noto.Pid; $null = $p16.Handle
        $clean = Stop-Noto $noto
        Start-Sleep -Milliseconds 500
        Add-Result $run '16 clean shutdown -> exit 0; mutex and pipe gone' ($clean -and $p16.ExitCode -eq 0 -and -not [NotoVal.Instance]::PipeExists($pipe) -and -not [NotoVal.Instance]::MutexExists($pipe)) "code=$($p16.ExitCode) pipe=$([NotoVal.Instance]::PipeExists($pipe)) mutex=$([NotoVal.Instance]::MutexExists($pipe)); trace: $((Trace-Of $noto.Pid) -join ' | ')"
        Add-Result $run '17 no Noto process left for the root' ((Root-Processes $data).Count -eq 0) ''

        # 8 - second launch during the owner's startup
        foreach ($delay in 0, 150, 400) {
            $null = Set-ForegroundByKeyboard $target
            $view = [NotoVal.ShellLaunch]::DesktopViewDispatch()
            $view.Application.ShellExecute($NotoExe, "--data-root=`"$data`"", $run.Root, 'open', 1)
            Start-Sleep -Milliseconds $delay
            $view.Application.ShellExecute($NotoExe, "--data-root=`"$data`"", $run.Root, 'open', 1)
            $pair = [NotoVal.Instance]::WaitNew('Noto', $run.Seen, 2, 30000)
            foreach ($p in $pair) { Add-Started $run $p.Id $p.StartTime.ToUniversalTime().Ticks }
            Start-Sleep -Seconds 6
            $alive = @($pair | Where-Object { -not $_.HasExited })
            $gone = @($pair | Where-Object { $_.HasExited })
            $survivor = if ($alive.Count -eq 1) { [pscustomobject]@{ Pid = $alive[0].Id; Hwnd = ([NotoVal.Instance]::VisibleWindows($alive[0].Id, $false) | Where-Object { [NotoVal.Win32]::Text($_) -eq 'Noto' } | Select-Object -First 1) } }
            $e = if ($survivor -and $survivor.Hwnd) { Ev $survivor }
            $traces = ($pair | ForEach-Object { "pid $($_.Id): $((Trace-Of $_.Id) -join ' / ')" }) -join ' | '
            Add-Result $run "8  launch during the owner's startup ($delay ms apart) -> one process survives, shown" ($pair.Count -eq 2 -and $alive.Count -eq 1 -and $gone.Count -eq 1 -and $gone[0].ExitCode -eq 0 -and $e -and $e.WsVisible -and -not $e.Minimized -and (Workspace-Windows $data) -eq 1) "launched=$($pair.Count) alive=$($alive.Count) exit codes=$(($gone | ForEach-Object ExitCode) -join ',') $(if ($e) { Show-State $e }); $traces"
            if ($survivor -and $survivor.Hwnd) { $null = Stop-Noto $survivor }
            Start-Sleep -Milliseconds 500
        }

        # 9 - five simultaneous launches
        $null = Set-ForegroundByKeyboard $target
        $view = [NotoVal.ShellLaunch]::DesktopViewDispatch()
        1..5 | ForEach-Object { $view.Application.ShellExecute($NotoExe, "--data-root=`"$data`"", $run.Root, 'open', 1) }
        $five = [NotoVal.Instance]::WaitNew('Noto', $run.Seen, 5, 30000)
        foreach ($p in $five) { Add-Started $run $p.Id $p.StartTime.ToUniversalTime().Ticks }
        $deadline = (Get-Date).AddSeconds(20)
        while ((Get-Date) -lt $deadline -and @($five | Where-Object { -not $_.HasExited }).Count -gt 1) { Start-Sleep -Milliseconds 200 }
        Start-Sleep -Seconds 3
        $alive = @($five | Where-Object { -not $_.HasExited }); $gone = @($five | Where-Object { $_.HasExited })
        $traces = ($five | ForEach-Object { "pid $($_.Id): $((Trace-Of $_.Id) -join ' / ')" }) -join ' | '
        $owners = @($five | Where-Object { ((Trace-Of $_.Id) -join ' ') -match 'outcome=Owner' }).Count
        $handed = @($five | Where-Object { ((Trace-Of $_.Id) -join ' ') -match 'outcome=HandedOff' }).Count
        Add-Result $run '9  five simultaneous launches -> exactly one owner, four handed off (exit 0)' ($five.Count -eq 5 -and $alive.Count -eq 1 -and $owners -eq 1 -and $handed -eq 4 -and @($gone | Where-Object { $_.ExitCode -ne 0 }).Count -eq 0) "launched=$($five.Count) alive=$($alive.Count) owners=$owners handed-off=$handed exit codes=$(($gone | ForEach-Object ExitCode) -join ','); $traces"
        Add-Result $run '9  ... one Noto process and one workspace window' ((Root-Processes $data).Count -eq 1 -and (Workspace-Windows $data) -eq 1) "processes=$((Root-Processes $data).Count) windows=$(Workspace-Windows $data)"
        if ($alive.Count -eq 1) { $null = Stop-Noto ([pscustomobject]@{ Pid = $alive[0].Id; Hwnd = ([NotoVal.Instance]::VisibleWindows($alive[0].Id, $false) | Select-Object -First 1) }) }

        # 19 - pipe squatting
        $data2 = Join-Path $run.Root 'squatted'; New-Item -ItemType Directory $data2 | Out-Null
        $pipe2 = [NotoVal.Instance]::PipeName($data2)
        $squatter = [NotoVal.Squatter]::new($pipe2)
        try {
            $owner2 = Start-Noto $run -Exe $NotoExe -DataRoot $data2
            $t2 = (Trace-Of $owner2.Pid) -join ' | '
            Add-Result $run '19 squatted pipe name -> Noto still owns its root and starts' ($t2 -match 'outcome=Owner' -and [NotoVal.Instance]::MutexExists($pipe2)) "trace: $t2"
            Add-Result $run '19 ... and records the squatting (pipe status=NameTaken)' ($t2 -match 'pipe status=NameTaken') ''
            $null = Set-ForegroundByKeyboard $target
            $s = Launch-Second $data2 -TimeoutMs 30000
            Add-Result $run '19 ... a second launch never becomes a second workspace (exit 2, message box)' ($s.Exited -and $s.Code -eq '2' -and $s.MaxWindows -eq 0 -and (Root-Processes $data2).Count -eq 1) (Second-Detail $s)
            $got = $squatter.Received
            Add-Result $run '19 ... what the same-user squatter received: the 8-byte Activate request only' ($got -eq '4E-4F-54-4F-01-01-00-00') $got
            $null = Stop-Noto $owner2
        }
        finally { $squatter.Dispose() }

        # 20 - a pipe served by another user, through Noto's own client code
        $platform = Join-Path (Split-Path $NotoExe) 'Noto.Platform.Windows.dll'
        $asm = [Reflection.Assembly]::LoadFrom($platform)
        $client = $asm.GetType('Noto.Platform.Windows.ActivationPipeClient')
        $sendRaw = $client.GetMethod('SendRaw', [Reflection.BindingFlags]'NonPublic,Static')
        $encode = $asm.GetType('Noto.Platform.Windows.ActivationEnvelope').GetMethod('Encode')
        $activate = $asm.GetType('Noto.Platform.Windows.ActivationRequest').GetProperty('Activate').GetValue($null)
        $msg = $encode.Invoke($null, @($activate))
        $status = $sendRaw.Invoke($null, @('epmapper', $msg, [TimeSpan]::FromSeconds(2), [Security.Principal.WindowsIdentity]::GetCurrent().User.Value))
        Add-Result $run "20 a pipe served by another user (Windows' epmapper, session 0) is refused by Noto's client" ("$status" -eq 'WrongUser') "status=$status"
    }
}
finally {
    $log.Dispose()
    Set-Content (Join-Path $run.Root 'trace.txt') ($log.All())
}
exit $code
