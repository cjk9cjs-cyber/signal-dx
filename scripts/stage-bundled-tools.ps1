#Requires -Version 5.1
<#
.SYNOPSIS
  Stage JTDX decoder, WSJT-X ft8code, and Hamlib into a Tools folder for the installer.
  HRD is not bundled (commercial) — Signal DX keeps it as a CAT backend option.
#>
[CmdletBinding()]
param(
    [string] $Destination = ""
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
if ([string]::IsNullOrWhiteSpace($Destination)) {
    $Destination = Join-Path $root 'artifacts\publish\win-x64\Tools'
}

function Copy-Required([string] $SourceDir, [string[]] $Names, [string] $DestDir) {
    if (-not (Test-Path -LiteralPath $SourceDir)) {
        throw "Source not found: $SourceDir"
    }
    New-Item -ItemType Directory -Path $DestDir -Force | Out-Null
    foreach ($name in $Names) {
        $src = Join-Path $SourceDir $name
        if (-not (Test-Path -LiteralPath $src)) {
            throw "Missing required file: $src"
        }
        Copy-Item -LiteralPath $src -Destination (Join-Path $DestDir $name) -Force
    }
}

Write-Host "Staging bundled tools → $Destination" -ForegroundColor Cyan
if (Test-Path -LiteralPath $Destination) {
    Remove-Item -LiteralPath $Destination -Recurse -Force
}

# --- JTDX contest decoder ---
$jtdxBin = @(
    'C:\JTDX_contest\bin',
    'C:\Program Files\JTDX_contest\bin',
    'C:\Program Files (x86)\JTDX_contest\bin'
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $jtdxBin) { throw "JTDX contest bin not found (need jtdxjt9.exe). Install JTDX Contest Edition." }

$jtdxDest = Join-Path $Destination 'JTDX'
Copy-Required $jtdxBin @(
    'jtdxjt9.exe',
    'libfftw3f-3.dll',
    'libgcc_s_seh-1.dll',
    'libgomp-1.dll',
    'libquadmath-0.dll',
    'libstdc++-6.dll',
    'libwinpthread-1.dll',
    'Qt5Core.dll',
    'Qt5Gui.dll'
) $jtdxDest

# Optional wisdom cache speeds first decode.
$wisdom = Join-Path $jtdxBin 'jt9_wisdom.dat'
if (Test-Path $wisdom) {
    Copy-Item $wisdom (Join-Path $jtdxDest 'jt9_wisdom.dat') -Force
}

# Qt platform plugin (required by Qt5Core on Windows).
$qtPlugin = @(
    (Join-Path (Split-Path $jtdxBin -Parent) 'plugins\platforms\qwindows.dll'),
    (Join-Path $jtdxBin 'plugins\platforms\qwindows.dll')
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $qtPlugin) { throw "Qt qwindows.dll not found beside JTDX." }
$platDir = Join-Path $jtdxDest 'plugins\platforms'
New-Item -ItemType Directory -Path $platDir -Force | Out-Null
Copy-Item $qtPlugin (Join-Path $platDir 'qwindows.dll') -Force
Set-Content -LiteralPath (Join-Path $jtdxDest 'qt.conf') -Value "[Paths]`r`nPlugins = plugins`r`n" -Encoding ASCII

# --- WSJT-X FT8 encoder ---
$wsjtBin = @(
    'C:\WSJT\wsjtx\bin',
    'C:\WSJT-X\bin',
    'C:\Program Files\WSJT\wsjtx\bin',
    'C:\Program Files (x86)\WSJT\wsjtx\bin'
) | Where-Object { Test-Path (Join-Path $_ 'ft8code.exe') } | Select-Object -First 1
if (-not $wsjtBin) { throw "WSJT-X bin not found (need ft8code.exe)." }

$wsjtDest = Join-Path $Destination 'WSJT'
$wsjtFiles = @(
    'ft8code.exe',
    'wsprd.exe',
    'libgfortran-4.dll',
    'libstdc++-6.dll',
    'libgcc_s_seh-1.dll',
    'libquadmath-0.dll',
    'libwinpthread-1.dll'
)
Copy-Required $wsjtBin $wsjtFiles $wsjtDest
if (Test-Path (Join-Path $wsjtBin 'ft4code.exe')) {
    Copy-Item (Join-Path $wsjtBin 'ft4code.exe') (Join-Path $wsjtDest 'ft4code.exe') -Force
}

# --- Hamlib CAT daemon (HRD stays optional / external) ---
$hamCandidates = [System.Collections.Generic.List[string]]::new()
foreach ($c in @(
        'C:\Program Files (x86)\hamlib-w32-4.7.2\bin',
        'C:\Program Files\hamlib\bin',
        'C:\hamlib\bin')) {
    $hamCandidates.Add($c)
}
foreach ($root in @(${env:ProgramFiles(x86)}, $env:ProgramFiles, 'C:\')) {
    if ([string]::IsNullOrWhiteSpace($root) -or -not (Test-Path $root)) { continue }
    Get-ChildItem -LiteralPath $root -Directory -Filter 'hamlib*' -ErrorAction SilentlyContinue |
        ForEach-Object { $hamCandidates.Add((Join-Path $_.FullName 'bin')) }
}
$hamBin = $hamCandidates |
    Where-Object { Test-Path (Join-Path $_ 'rigctld.exe') } |
    Select-Object -First 1
if (-not $hamBin) { throw "Hamlib bin not found (need rigctld.exe)." }

$hamDest = Join-Path $Destination 'Hamlib'
$hamFiles = @('rigctld.exe', 'rigctl.exe')
Get-ChildItem $hamBin -Filter '*.dll' | ForEach-Object { $hamFiles += $_.Name }
Copy-Required $hamBin ($hamFiles | Select-Object -Unique) $hamDest

# Third-party notice (GPL/LGPL upstreams).
$notice = @"
Signal DX bundles redistributable tools from:

- JTDX (jtdxjt9) — https://jtdx.tech / contest builds — GPL
- WSJT-X (ft8code, wsprd) — https://wsjt.sourceforge.io — GPL
- Hamlib (rigctld) — https://hamlib.github.io — LGPL

Ham Radio Deluxe (HRD) is NOT included. Use Settings → CAT → Ham Radio Deluxe
if you already have HRD Rig Control installed.

Obtain corresponding source from the upstream projects to satisfy GPL/LGPL.
"@
Set-Content -LiteralPath (Join-Path $Destination 'THIRD-PARTY.txt') -Value $notice -Encoding UTF8

Write-Host "Bundled:" -ForegroundColor Green
Write-Host "  JTDX   → $(Join-Path $jtdxDest 'jtdxjt9.exe')"
Write-Host "  WSJT   → $(Join-Path $wsjtDest 'ft8code.exe')"
Write-Host "  Hamlib → $(Join-Path $hamDest 'rigctld.exe')"
return $Destination
