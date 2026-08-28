param(
    [string]$OutputDirectory = "artifacts"
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$versionFile = Join-Path $repoRoot "VERSION"

if (-not (Test-Path $versionFile)) {
    throw "VERSION file was not found at $versionFile"
}

$version = (Get-Content $versionFile -Raw).Trim()
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "VERSION is empty."
}

Push-Location $repoRoot
try {
    git rev-parse --is-inside-work-tree *> $null
    if ($LASTEXITCODE -ne 0) {
        throw "The project is not a Git working tree. Commit the repository before exporting a handoff archive."
    }

    $outputRoot = Join-Path $repoRoot $OutputDirectory
    New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

    $output = Join-Path $outputRoot "HTFManager-v$version-handoff.zip"
    if (Test-Path $output) {
        Remove-Item $output -Force
    }

    git archive --format=zip --output="$output" HEAD
    if ($LASTEXITCODE -ne 0) {
        throw "git archive failed."
    }

    Write-Host "Created source-only handoff archive:"
    Write-Host $output
}
finally {
    Pop-Location
}
