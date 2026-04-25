$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
$project = Join-Path $repoRoot "CampusAutoLogin.Agent\CampusAutoLogin.Agent.csproj"
$output = Join-Path $repoRoot "artifacts\CampusAutoLogin"

New-Item -ItemType Directory -Force -Path $output | Out-Null

dotnet publish $project `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  -o $output

Write-Host "Published: $output\CampusAutoLogin.exe"
