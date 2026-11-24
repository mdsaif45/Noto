#Requires -Version 7.4
<#
  Counts the items in Windows' Recycle Bin that were $Name in $Folder before they were deleted, and prints the count;
  with -Remove, and only when exactly one matches, permanently removes that one item (its $R content and $I record) and
  nothing else. For Invoke-DropImport.ps1's own uniquely named temporary file (#267). Run in a process of its own, as
  helpers/explorer-windows.ps1 is: a harness process that has wrapped the shell through Shell.Application cannot use it
  through NotoVal.ShellLaunch afterwards.

  The match is the item's original folder (System.Recycle.DeletedFrom) and its original name: the display name, with or
  without its extension (Explorer may hide extensions), and the stored file's extension.
#>
param([Parameter(Mandatory)] [string] $Folder, [Parameter(Mandatory)] [string] $Name, [switch] $Remove)
$ErrorActionPreference = 'Stop'
$bin = (New-Object -ComObject Shell.Application).Namespace(10)
$folderFull = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Folder))
$extension = [IO.Path]::GetExtension($Name)
$stem = [IO.Path]::GetFileNameWithoutExtension($Name)
$found = @($bin.Items() | Where-Object {
        $from = "$($_.ExtendedProperty('System.Recycle.DeletedFrom'))"
        $from -and [IO.Path]::TrimEndingDirectorySeparator($from) -ieq $folderFull -and
        ($_.Name -ieq $Name -or $_.Name -ieq $stem) -and [IO.Path]::GetExtension($_.Path) -ieq $extension
    })
if ($Remove) {
    if ($found.Count -ne 1) { throw "refusing to remove: $($found.Count) items match, not exactly one" }
    $content = $found[0].Path
    $leaf = Split-Path $content -Leaf
    if ($content -notmatch '\\\$Recycle\.Bin\\' -or -not $leaf.StartsWith('$R')) { throw "refusing to remove '$content': not a Recycle Bin item" }
    $record = Join-Path (Split-Path $content) ('$I' + $leaf.Substring(2))
    Remove-Item -LiteralPath $content -Force
    if (Test-Path -LiteralPath $record) { Remove-Item -LiteralPath $record -Force }
}
$found.Count
