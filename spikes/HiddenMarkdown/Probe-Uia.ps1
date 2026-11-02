<#
.SYNOPSIS
    SPIKE: what a UI Automation client (Narrator) reads from the hidden-markdown editor.

.DESCRIPTION
    Starts the spike with --hold, which loads a sample with its markup hidden and writes the source (markdown.txt) and
    what a reader should get (visible.txt). Reads the editor's TextPattern document range and compares it with both,
    then closes the spike. Nothing is typed or clicked.
#>
param(
    [string]$Exe = (Join-Path $PSScriptRoot 'bin\Release\net9.0-windows10.0.19041.0\win-x64\Noto.Spike.HiddenMarkdown.exe'),
    [string]$Dir = (Join-Path ([IO.Path]::GetTempPath()) "noto-spike-uia-$PID")
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

Remove-Item $Dir -Recurse -Force -ErrorAction SilentlyContinue
$proc = Start-Process $Exe -ArgumentList '--hold', $Dir -PassThru
try {
    $deadline = (Get-Date).AddSeconds(30)
    while (-not (Test-Path (Join-Path $Dir 'ready'))) {
        if ((Get-Date) -gt $deadline) { throw 'the spike never became ready' }
        Start-Sleep -Milliseconds 200
    }

    $markdown = [IO.File]::ReadAllText((Join-Path $Dir 'markdown.txt'))
    $visible = [IO.File]::ReadAllText((Join-Path $Dir 'visible.txt'))

    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $byPid = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)
    $byId = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'SpikeEditor')
    $editor = $null
    while (-not $editor -and (Get-Date) -lt $deadline) {
        $window = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $byPid)
        if ($window) { $editor = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $byId) }
        if (-not $editor) { Start-Sleep -Milliseconds 200 }
    }
    if (-not $editor) { throw 'editor not found in the UI Automation tree' }

    $pattern = $editor.GetCurrentPattern([System.Windows.Automation.TextPattern]::Pattern)
    $read = $pattern.DocumentRange.GetText(-1)
    $norm = { param($s) ($s -replace "`r`n", "`n") -replace "`r", "`n" }
    $readN = (& $norm $read).TrimEnd("`n")

    "uia control type: $($editor.Current.ControlType.ProgrammaticName); name: $($editor.Current.Name)"
    "uia TextPattern text length: $($read.Length) (source $($markdown.Length), visible $($visible.Length))"
    "uia text == source (markup read aloud): $($readN -eq $markdown.TrimEnd("`n"))"
    "uia text == visible (markup not read): $($readN -eq $visible.TrimEnd("`n"))"
    "uia first 80 chars: $(($read.Substring(0, [Math]::Min(80, $read.Length))) -replace "`r", '\r' -replace "`n", '\n')"
}
finally {
    if (-not $proc.HasExited) { $proc.Kill() }
    Remove-Item $Dir -Recurse -Force -ErrorAction SilentlyContinue
}
