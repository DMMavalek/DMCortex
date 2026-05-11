param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "DungeonMasterCortex\DungeonMasterCortex.csproj"
$outRoot = Join-Path $root "artifacts\install"

$dmOut = Join-Path $outRoot "DungeonMaster-Demo"
$playerOut = Join-Path $outRoot "Player-Demo"

if (Test-Path $outRoot) {
    Remove-Item -Recurse -Force $outRoot
}

New-Item -ItemType Directory -Force -Path $dmOut | Out-Null
New-Item -ItemType Directory -Force -Path $playerOut | Out-Null

Write-Host "Publishing DM Edition (demo default)..."
dotnet publish $project -c $Configuration -r $Runtime --self-contained false -o $dmOut -p:DefineConstants="APP_EDITION_DM"

Write-Host "Publishing Player Edition (demo default)..."
dotnet publish $project -c $Configuration -r $Runtime --self-contained false -o $playerOut -p:DefineConstants="APP_EDITION_PLAYER"

$dmZip = Join-Path $outRoot "DungeonMaster-Demo.zip"
$playerZip = Join-Path $outRoot "Player-Demo.zip"

if (Test-Path $dmZip) { Remove-Item -Force $dmZip }
if (Test-Path $playerZip) { Remove-Item -Force $playerZip }

Compress-Archive -Path (Join-Path $dmOut "*") -DestinationPath $dmZip
Compress-Archive -Path (Join-Path $playerOut "*") -DestinationPath $playerZip

Write-Host "Done. Demo packages created:"
Write-Host "  $dmZip"
Write-Host "  $playerZip"
