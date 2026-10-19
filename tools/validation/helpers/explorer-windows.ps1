#Requires -Version 7.4
<#
  Counts the Explorer windows showing $Uri, and with -Close closes them; prints the count found. Run in a process of
  its own: the shell's window collection is one object, and a harness process that has wrapped it through
  Shell.Application can no longer use it through NotoVal.ShellLaunch, which every later shell launch needs (measured:
  "Unable to cast ... to type 'ShellWindowsClass'" at the next Start-Noto).
#>
param([Parameter(Mandatory)] [string] $Uri, [switch] $Close)
$ErrorActionPreference = 'Stop'
$shell = New-Object -ComObject Shell.Application
$windows = @($shell.Windows() | Where-Object { "$($_.LocationURL)".TrimEnd('/') -eq $Uri })
if ($Close) { $windows | ForEach-Object { $_.Quit() } }
$windows.Count
