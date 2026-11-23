#Requires -Version 5.1
<#
.SYNOPSIS
  Convert docs/HOWTO.md into docs/SignalDx-HOWTO.pdf (Edge headless print).

.EXAMPLE
  .\scripts\build-howto-pdf.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$md = Join-Path $root 'docs\HOWTO.md'
$pdf = Join-Path $root 'docs\SignalDx-HOWTO.pdf'
$html = Join-Path $env:TEMP ("SignalDx-HOWTO-{0}.html" -f [guid]::NewGuid().ToString('N'))

if (-not (Test-Path -LiteralPath $md)) {
    throw "Missing $md"
}

$edge = @(
    "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
    "${env:ProgramFiles}\Microsoft\Edge\Application\msedge.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) {
    throw "Microsoft Edge not found (needed for headless PDF print)."
}

# Prefer Python markdown → HTML when available; otherwise a simple escape + pre wrap.
$bodyHtml = $null
$py = Get-Command python -ErrorAction SilentlyContinue
if ($py) {
    $converter = @'
import html, pathlib, re, sys
text = pathlib.Path(sys.argv[1]).read_text(encoding="utf-8")
try:
    import markdown
    body = markdown.markdown(text, extensions=["tables", "fenced_code", "toc"])
except Exception:
    body = "<pre>" + html.escape(text) + "</pre>"
print(body)
'@
    $converterPath = Join-Path $env:TEMP ("sdx-md2html-{0}.py" -f [guid]::NewGuid().ToString('N'))
    Set-Content -LiteralPath $converterPath -Value $converter -Encoding UTF8
    try {
        # Ensure markdown package for nicer rendering (best-effort).
        $prevEap = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        & $py.Source -m pip install --quiet markdown 1>$null 2>$null
        $bodyHtml = & $py.Source $converterPath $md 2>$null | Out-String
        $ErrorActionPreference = $prevEap
        if ([string]::IsNullOrWhiteSpace($bodyHtml) -or $bodyHtml.Length -lt 40) {
            $bodyHtml = $null
        }
    }
    catch {
        $bodyHtml = $null
    }
    finally {
        Remove-Item -LiteralPath $converterPath -Force -ErrorAction SilentlyContinue
    }
}

if ([string]::IsNullOrWhiteSpace($bodyHtml)) {
    $raw = Get-Content -LiteralPath $md -Raw -Encoding UTF8
    $escaped = [System.Net.WebUtility]::HtmlEncode($raw)
    $bodyHtml = "<pre style=`"white-space:pre-wrap;font-family:Consolas,monospace;font-size:11pt;`">$escaped</pre>"
}

$doc = @"
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8"/>
<title>Signal DX — How to get started</title>
<style>
  @page { margin: 18mm 16mm; }
  body {
    font-family: "Segoe UI", Calibri, Arial, sans-serif;
    font-size: 11pt;
    line-height: 1.45;
    color: #1a1a1a;
    max-width: 900px;
    margin: 0 auto;
  }
  h1 { font-size: 22pt; color: #0b5cab; border-bottom: 2px solid #0b5cab; padding-bottom: 6px; }
  h2 { font-size: 16pt; color: #0b5cab; margin-top: 1.4em; }
  h3 { font-size: 13pt; color: #244; margin-top: 1.1em; }
  code, pre { font-family: Consolas, "Courier New", monospace; font-size: 9.5pt; }
  pre { background: #f4f6f8; padding: 10px; border-radius: 4px; overflow-x: auto; }
  table { border-collapse: collapse; width: 100%; margin: 0.8em 0; font-size: 10pt; }
  th, td { border: 1px solid #ccc; padding: 6px 8px; text-align: left; vertical-align: top; }
  th { background: #e8eef5; }
  a { color: #0b5cab; }
  hr { border: none; border-top: 1px solid #ccd; margin: 1.5em 0; }
</style>
</head>
<body>
$bodyHtml
</body>
</html>
"@

Set-Content -LiteralPath $html -Value $doc -Encoding UTF8

# file:/// URL for Edge
$htmlUri = ([Uri]$html).AbsoluteUri
if (Test-Path -LiteralPath $pdf) {
    Remove-Item -LiteralPath $pdf -Force
}

Write-Host "Printing PDF with Edge…" -ForegroundColor Cyan
& $edge --headless --disable-gpu --no-pdf-header-footer --print-to-pdf="$pdf" $htmlUri
Start-Sleep -Seconds 2
Remove-Item -LiteralPath $html -Force -ErrorAction SilentlyContinue

if (-not (Test-Path -LiteralPath $pdf) -or (Get-Item $pdf).Length -lt 1000) {
    throw "PDF was not created at $pdf"
}

Write-Host "Wrote $pdf ($([math]::Round((Get-Item $pdf).Length / 1KB, 1)) KB)" -ForegroundColor Green
return $pdf
