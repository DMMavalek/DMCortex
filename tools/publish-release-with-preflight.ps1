param(
    [string]$Tag = '',
    [string]$Title = '',
    [string]$Notes = '',
    [string]$NotesFile = '',
    [string]$Token = '',
    [switch]$Draft,
    [switch]$Prerelease,
    [switch]$RemoveOldInstallerVersions
)

$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$preflightScript = Join-Path $root 'tools\release-preflight.ps1'
$publishScript = Join-Path $root 'publish-github-release.ps1'

$resolvedToken = $Token
if ([string]::IsNullOrWhiteSpace($resolvedToken)) {
    $resolvedToken = $env:GITHUB_TOKEN
}
if ([string]::IsNullOrWhiteSpace($resolvedToken)) {
    $resolvedToken = [Environment]::GetEnvironmentVariable('GITHUB_TOKEN', 'User')
}

Write-Host '[Release] Running preflight checks (version/changelog/offline defaults)...'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $preflightScript -SkipBuild
if ($LASTEXITCODE -ne 0) {
    throw "Preflight failed with exit code $LASTEXITCODE"
}

Write-Host '[Release] Publishing release to GitHub...'
$publishArgs = @(
    '-NoProfile',
    '-ExecutionPolicy', 'Bypass',
    '-File', $publishScript
)

if (-not [string]::IsNullOrWhiteSpace($Tag)) {
    $publishArgs += @('-Tag', $Tag)
}

if (-not [string]::IsNullOrWhiteSpace($Title)) {
    $publishArgs += @('-Title', $Title)
}

if (-not [string]::IsNullOrWhiteSpace($Notes)) {
    $publishArgs += @('-Notes', $Notes)
}

if (-not [string]::IsNullOrWhiteSpace($NotesFile)) {
    $publishArgs += @('-NotesFile', $NotesFile)
}

if (-not [string]::IsNullOrWhiteSpace($resolvedToken)) {
    $publishArgs += @('-Token', $resolvedToken)
}

if ($Draft) {
    $publishArgs += '-Draft'
}

if ($Prerelease) {
    $publishArgs += '-Prerelease'
}

if ($RemoveOldInstallerVersions) {
    $publishArgs += '-RemoveOldInstallerVersions'
}

& powershell.exe @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "GitHub release publish failed with exit code $LASTEXITCODE"
}

Write-Host '[Release] GitHub publish complete.'