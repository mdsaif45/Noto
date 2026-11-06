#Requires -Version 7.4
<#
.SYNOPSIS
  Builds Noto's installer and release packages (ADR-008): a self-contained publish, packed by Velopack.

.DESCRIPTION
  Writes to -Output (default artifacts\release):
    Noto.App-win-Setup.exe        the installer: per user, no administrator, no wizard; a splash while it installs,
                                  then Noto starts. Installs to %LOCALAPPDATA%\Noto.App, never %LOCALAPPDATA%\Noto,
                                  which holds the notes (an uninstall removes the install folder, nothing else).
    Noto.App-<version>-full.nupkg the release package an installer of a later version upgrades from
    releases.win.json, assets.win.json
                                  the release feed (no update check reads it in v0.1: Noto has no network access)

  Unsigned unless -SignParams is given (signtool parameters, ADR-008 Signing): Windows SmartScreen then warns on the
  installer's first run ("Windows protected your PC" > More info > Run anyway).

.EXAMPLE
  pwsh -NoProfile -File tools/packaging/New-Release.ps1 -Version 0.1.0
#>
param(
    [Parameter(Mandatory)] [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')] [string] $Version,
    [string] $Output = 'artifacts/release',
    [string] $SignParams = ''
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Push-Location $root
try {
    $publish = Join-Path $root 'artifacts/publish/win-x64'
    $out = [IO.Path]::GetFullPath((Join-Path $root $Output))
    foreach ($dir in $publish, $out) { if (Test-Path $dir) { Remove-Item $dir -Recurse -Force } }

    # Self-contained (the project's PublishSelfContained): the .NET runtime and the Windows App SDK ship inside, so
    # nothing has to be installed first. x64, the project's own runtime. Neither is given on the command line, where it
    # would also reach the Noto.UI library, whose resources WinUI then looks for in a folder it never writes (PRI175).
    dotnet publish src/Noto.Windows/Noto.Windows.csproj --configuration Release "-p:Version=$Version" --output $publish -nologo
    if ($LASTEXITCODE) { throw "dotnet publish failed ($LASTEXITCODE)" }
    if (-not (Test-Path (Join-Path $publish 'Noto.exe')) -or -not (Test-Path (Join-Path $publish 'coreclr.dll'))) { throw 'the publish is not a self-contained Noto' }

    # The splash: the icon's largest image, so the brand lives in one file.
    Add-Type -AssemblyName System.Drawing
    $splash = Join-Path $root 'artifacts/splash.png'
    $icon = [System.Drawing.Icon]::new((Join-Path $root 'src/Noto.Windows/Assets/Noto.ico'), 256, 256)
    try { $bitmap = $icon.ToBitmap(); try { $bitmap.Save($splash, [System.Drawing.Imaging.ImageFormat]::Png) } finally { $bitmap.Dispose() } } finally { $icon.Dispose() }

    dotnet tool restore | Out-Null
    $pack = @(
        'vpk', 'pack',
        '--packId', 'Noto.App', '--packVersion', $Version, '--packDir', $publish, '--mainExe', 'Noto.exe',
        '--packTitle', 'Noto', '--packAuthors', 'Noto',
        '--icon', 'src/Noto.Windows/Assets/Noto.ico', '--splashImage', $splash,
        '--shortcuts', 'StartMenuRoot', '--noPortable',
        '--runtime', 'win-x64', '--outputDir', $out
    )
    if ($SignParams) { $pack += @('--signParams', $SignParams) }
    dotnet @pack
    if ($LASTEXITCODE) { throw "vpk pack failed ($LASTEXITCODE)" }

    $setup = Join-Path $out 'Noto.App-win-Setup.exe'
    if (-not (Test-Path $setup)) { throw "no installer at $setup" }
    Get-ChildItem $out -File | ForEach-Object { "{0,-40} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB) }
}
finally { Pop-Location }
