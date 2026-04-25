$ErrorActionPreference = "Stop"

$root = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSCommandPath))
$src = Join-Path $root "tools\autologin-agent\CampusAutoLogin.cs"
$outDir = Join-Path $root "artifacts\CampusAutoLogin"
$out = Join-Path $outDir "CampusAutoLogin.exe"
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not (Test-Path $csc)) {
    throw "csc.exe not found: $csc"
}

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

& $csc /nologo /target:winexe /optimize+ `
    /out:$out `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    $src

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "Built: $out"
