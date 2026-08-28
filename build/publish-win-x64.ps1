$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "artifacts/win-x64"

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Path $out | Out-Null

dotnet publish (Join-Path $root "src/HTFManager.App/HTFManager.App.csproj") `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:PublishTrimmed=true `
  -p:TrimMode=partial `
  -o $out

Write-Host "Published to $out"
