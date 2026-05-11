$ErrorActionPreference = 'Stop'

$root = 'C:\Users\kelava\Documents\Projects\Dungeon Master Cortex'
$project = Join-Path $root 'DungeonMasterCortex\DungeonMasterCortex.csproj'
$iscc = 'C:\Users\kelava\AppData\Local\Programs\Inno Setup 6\ISCC.exe'
$dmIss = Join-Path $root 'Installer\DMCortexSetup.iss'
$playerIss = Join-Path $root 'Installer\PlayerCortexSetup.iss'
$dmOut = 'C:\Users\kelava\Documents\Projects\DMC Updates\DMCodex'
$playerOut = 'C:\Users\kelava\Documents\Projects\DMC Updates\playercodex'
$dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'

function Clear-PublishOutput([string]$path)
{
	if (-not (Test-Path $path)) {
		New-Item -ItemType Directory -Path $path | Out-Null
		return
	}

	Get-ChildItem -Path $path -Force |
		Where-Object { $_.Name -notlike '*-Setup-*.exe' } |
		Remove-Item -Recurse -Force
}

function Publish-SingleFile([string]$editionConstants, [string]$assemblyName, [string]$outputPath)
{
	& $dotnet publish $project `
		-c Release `
		-r win-x64 `
		--self-contained true `
		-o $outputPath `
		-p:DefineConstants=$editionConstants `
		-p:AssemblyName=$assemblyName `
		-p:PublishSingleFile=true `
		-p:EnableCompressionInSingleFile=true `
		-p:IncludeNativeLibrariesForSelfExtract=true `
		-p:DebugType=None `
		-p:DebugSymbols=false
}

if (-not (Test-Path $dotnet)) {
	throw "dotnet.exe not found at $dotnet"
}

if (-not (Test-Path $iscc)) {
	throw "ISCC.exe not found at $iscc"
}

Write-Host 'Preparing output folders...'
Clear-PublishOutput $dmOut
Clear-PublishOutput $playerOut

Write-Host 'Publishing single-file DM build...'
Publish-SingleFile -editionConstants 'APP_EDITION_DM' -assemblyName 'DungeonMasterCortex' -outputPath $dmOut

Write-Host 'Publishing single-file Player build...'
Publish-SingleFile -editionConstants 'APP_EDITION_PLAYER' -assemblyName 'PlayerCortex' -outputPath $playerOut

if (-not (Test-Path (Join-Path $dmOut 'DungeonMasterCortex.exe'))) {
	throw 'DM publish did not produce DungeonMasterCortex.exe'
}

if (-not (Test-Path (Join-Path $playerOut 'PlayerCortex.exe'))) {
	throw 'Player publish did not produce PlayerCortex.exe'
}

Write-Host 'Building installers...'
& $iscc $dmIss
& $iscc $playerIss

Get-ChildItem $dmOut -Filter 'DMCodex-Setup-1.1.00.exe' | Format-Table FullName, Length, LastWriteTime
Get-ChildItem $playerOut -Filter 'PlayerCodex-Setup-1.1.00.exe' | Format-Table FullName, Length, LastWriteTime
