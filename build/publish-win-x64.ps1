$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$version = (Get-Content (Join-Path $root "VERSION") -Raw).Trim()
if ([string]::IsNullOrWhiteSpace($version)) { throw "VERSION is empty." }

$publishDir = Join-Path $root "artifacts/publish/win-x64"
$releaseDir = Join-Path $root "artifacts/release/v$version"
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
if (Test-Path $releaseDir) { Remove-Item $releaseDir -Recurse -Force }
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null

$project = Join-Path $root "src/HTFManager.App/HTFManager.App.csproj"
dotnet publish $project `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishProfile=win-x64 `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:PublishTrimmed=false `
  -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

$sourceExe = Join-Path $publishDir "HTFManager.exe"
if (-not (Test-Path $sourceExe)) { throw "Published HTFManager.exe was not found." }

$assetName = "HTFManager.exe"
$assetPath = Join-Path $releaseDir $assetName
Copy-Item $sourceExe $assetPath -Force

$hash = (Get-FileHash $assetPath -Algorithm SHA256).Hash.ToUpperInvariant()
$size = (Get-Item $assetPath).Length
$manifest = [ordered]@{
  schemaVersion = 1
  channel = "stable"
  version = $version
  rid = "win-x64"
  asset = $assetName
  size = $size
  sha256 = $hash
  publishedAt = [DateTimeOffset]::UtcNow.ToString("o")
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $releaseDir "update-manifest.json") -Encoding utf8
"$hash  $assetName" | Set-Content (Join-Path $releaseDir "SHA256SUMS.txt") -Encoding ascii

Write-Host "Published release assets to $releaseDir"
Write-Host "Executable: $assetName"
Write-Host "SHA-256: $hash"
