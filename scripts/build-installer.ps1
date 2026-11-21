#Requires -Version 5.1
<#
.SYNOPSIS
  Publish Signal DX (self-contained win-x64) and build a Windows Setup.exe with Inno Setup.

.EXAMPLE
  .\scripts\build-installer.ps1
  .\scripts\build-installer.ps1 -Configuration Release -SkipPublish
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [switch] $SkipPublish,
    [switch] $FrameworkDependent
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
Set-Location $root

$csproj = Join-Path $root 'src\SignalDx\SignalDx.csproj'
$iss = Join-Path $root 'installer\SignalDx.iss'
$publishDir = Join-Path $root 'artifacts\publish\win-x64'
$outDir = Join-Path $root 'artifacts\installer'

function Get-AppVersion([string] $projectPath) {
    [xml] $xml = Get-Content -LiteralPath $projectPath -Raw
    $ver = $xml.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($ver)) {
        throw "Could not read <Version> from $projectPath"
    }
    return $ver.Trim()
}

function Find-Iscc {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:LocalAppData}\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 7\ISCC.exe"
    )
    foreach ($c in $candidates) {
        if (Test-Path -LiteralPath $c) { return $c }
    }
    $cmd = Get-Command iscc -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return $null
}

$version = Get-AppVersion $csproj
Write-Host "Signal DX $version — building Windows installer" -ForegroundColor Cyan

if (-not $SkipPublish) {
    if (Test-Path -LiteralPath $publishDir) {
        Remove-Item -LiteralPath $publishDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

    $publishArgs = @(
        'publish', $csproj,
        '-c', $Configuration,
        '-r', 'win-x64',
        '-o', $publishDir,
        '--nologo',
        '-p:DebugType=None',
        '-p:DebugSymbols=false',
        '-p:PublishReadyToRun=true'
    )
    if ($FrameworkDependent) {
        $publishArgs += '--self-contained', 'false'
        Write-Host "Publish: framework-dependent win-x64 (requires .NET 8 Desktop Runtime)" -ForegroundColor Yellow
    }
    else {
        $publishArgs += '--self-contained', 'true'
        Write-Host "Publish: self-contained win-x64" -ForegroundColor Green
    }

    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

    $exe = Join-Path $publishDir 'SignalDx.exe'
    if (-not (Test-Path -LiteralPath $exe)) {
        throw "Publish succeeded but SignalDx.exe not found in $publishDir"
    }

    # Bundle JTDX decoder, WSJT-X ft8code, and Hamlib (HRD stays optional).
    & (Join-Path $PSScriptRoot 'stage-bundled-tools.ps1') -Destination (Join-Path $publishDir 'Tools')
}

$iscc = Find-Iscc
if (-not $iscc) {
    throw @"
Inno Setup compiler (ISCC.exe) not found.
Install with: winget install --id JRSoftware.InnoSetup -e
"@
}

New-Item -ItemType Directory -Path $outDir -Force | Out-Null

Write-Host "Compiling installer with $iscc" -ForegroundColor Green
& $iscc `
    "/DMyAppVersion=$version" `
    "/DPublishDir=$publishDir" `
    $iss
if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)" }

$setup = Get-ChildItem -LiteralPath $outDir -Filter "SignalDx-$version-Setup.exe" | Select-Object -First 1
if (-not $setup) {
    throw "Installer not found under $outDir"
}

Write-Host ""
Write-Host "Installer ready:" -ForegroundColor Cyan
Write-Host "  $($setup.FullName)"
Write-Host "  Size: $([math]::Round($setup.Length / 1MB, 1)) MB"
return $setup.FullName
