param(
    [switch]$SkipBuild,
    [switch]$RequireInstallers
)

$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$csprojPath = Join-Path $root 'DungeonMasterCortex\DungeonMasterCortex.csproj'
$dmIssPath = Join-Path $root 'Installer\DMCortexSetup.iss'
$playerIssPath = Join-Path $root 'Installer\PlayerCortexSetup.iss'
$changelogPath = Join-Path $root 'DungeonMasterCortex\CHANGELOG.txt'
$buildInstallersPath = Join-Path $root 'build-installers.ps1'
$solutionPath = Join-Path $root 'Dungeon Master Cortex.sln'
$dotnetPath = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'

if (-not (Test-Path $dotnetPath)) {
    $dotnetPath = 'dotnet'
}

function Get-RegexValue {
    param(
        [string]$InputText,
        [string]$Pattern,
        [string]$Name
    )

    $match = [regex]::Match($InputText, $Pattern)
    if (-not $match.Success) {
        throw "Unable to find $Name."
    }

    return $match.Groups[1].Value.Trim()
}

function Assert-TextMatch {
    param(
        [string]$InputText,
        [string]$Pattern,
        [string]$FailureMessage
    )

    if (-not [regex]::IsMatch($InputText, $Pattern)) {
        throw $FailureMessage
    }
}

Write-Host '[Preflight] Reading project metadata...'
$csprojText = Get-Content -Raw -Path $csprojPath
$dmIssText = Get-Content -Raw -Path $dmIssPath
$playerIssText = Get-Content -Raw -Path $playerIssPath
$changelogText = Get-Content -Raw -Path $changelogPath
$buildInstallersText = Get-Content -Raw -Path $buildInstallersPath

$appVersion = Get-RegexValue -InputText $csprojText -Pattern '<Version>\s*([^<]+)\s*</Version>' -Name 'project version'
$assemblyVersion = Get-RegexValue -InputText $csprojText -Pattern '<AssemblyVersion>\s*([^<]+)\s*</AssemblyVersion>' -Name 'assembly version'
$fileVersion = Get-RegexValue -InputText $csprojText -Pattern '<FileVersion>\s*([^<]+)\s*</FileVersion>' -Name 'file version'
$escapedVersion = [regex]::Escape($appVersion)

Write-Host "[Preflight] Project version: $appVersion"

$expectedAssemblyVersion = "$appVersion.0"
if ($assemblyVersion -ne $expectedAssemblyVersion) {
    throw "AssemblyVersion mismatch. Expected '$expectedAssemblyVersion' but found '$assemblyVersion'."
}

if ($fileVersion -ne $expectedAssemblyVersion) {
    throw "FileVersion mismatch. Expected '$expectedAssemblyVersion' but found '$fileVersion'."
}

Assert-TextMatch -InputText $dmIssText -Pattern ('#define\s+MyAppVersion\s+"{0}"' -f $escapedVersion) -FailureMessage "DM installer version does not match project version $appVersion."
Assert-TextMatch -InputText $playerIssText -Pattern ('#define\s+MyAppVersion\s+"{0}"' -f $escapedVersion) -FailureMessage "Player installer version does not match project version $appVersion."

Assert-TextMatch -InputText $dmIssText -Pattern ('OutputBaseFilename\s*=\s*DMCodex-Setup-{0}' -f $escapedVersion) -FailureMessage "DM installer output filename does not include version $appVersion."
Assert-TextMatch -InputText $playerIssText -Pattern ('OutputBaseFilename\s*=\s*PlayerCodex-Setup-{0}' -f $escapedVersion) -FailureMessage "Player installer output filename does not include version $appVersion."

Assert-TextMatch -InputText $changelogText -Pattern ('(?m)^v{0}\s*\(' -f $escapedVersion) -FailureMessage "CHANGELOG is missing a v$appVersion entry."

Assert-TextMatch -InputText $buildInstallersText -Pattern '\$disableNetworkingBuild\s*=\s*if\s*\(\$EnableNetworkingFeatures\)\s*\{\s*''false''\s*\}\s*else\s*\{\s*''true''\s*\}' -FailureMessage "Offline default check failed in build-installers.ps1."

if (-not $SkipBuild) {
    Write-Host '[Preflight] Running release solution build...'
    & $dotnetPath build $solutionPath -c Release
    if ($LASTEXITCODE -ne 0) {
        throw "Release build failed with exit code $LASTEXITCODE."
    }
}
else {
    Write-Host '[Preflight] Skipping solution build (SkipBuild specified).'
}

if ($RequireInstallers) {
    Write-Host '[Preflight] Verifying installer artifacts for current version...'
    $dmInstallerPath = Join-Path $root ("artifacts\releases\dm\DMCodex-Setup-{0}.exe" -f $appVersion)
    $playerInstallerPath = Join-Path $root ("artifacts\releases\player\PlayerCodex-Setup-{0}.exe" -f $appVersion)

    if (-not (Test-Path $dmInstallerPath)) {
        throw "Missing DM installer artifact: $dmInstallerPath"
    }

    if (-not (Test-Path $playerInstallerPath)) {
        throw "Missing Player installer artifact: $playerInstallerPath"
    }
}

Write-Host ''
Write-Host '[Preflight] PASS'
Write-Host "[Preflight] Version: $appVersion"
Write-Host "[Preflight] Offline default networking toggle: verified"
Write-Host "[Preflight] Solution release build: $(if ($SkipBuild) { 'skipped' } else { 'passed' })"
Write-Host "[Preflight] Installer artifact check: $(if ($RequireInstallers) { 'passed' } else { 'skipped' })"