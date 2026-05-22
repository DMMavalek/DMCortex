param(
	[switch]$RemoveOldInstallerVersions,
	[switch]$EnableNetworkingFeatures,
	[string]$DmInstallerOutputDir = 'artifacts\releases\dm',
	[string]$PlayerInstallerOutputDir = 'artifacts\releases\player'
)
$ErrorActionPreference = 'Stop'


$PinnedInstallerVersion = [version]'1.3.00'

$root = (Resolve-Path (Join-Path $PSScriptRoot '.')).Path
$project = Join-Path $root 'DungeonMasterCortex\DungeonMasterCortex.csproj'
$iscc = $null
$dmIss = Join-Path $root 'Installer\DMCortexSetup.iss'
$playerIss = Join-Path $root 'Installer\PlayerCortexSetup.iss'
$stageRoot = Join-Path $root 'artifacts\installer-publish'
$dmStage = Join-Path $stageRoot 'dm'
$playerStage = Join-Path $stageRoot 'player'
$dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'

function Resolve-IsccPath
{
	$candidates = @(
		(Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
		(Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
		(Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
	) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

	foreach ($candidate in $candidates) {
		if (Test-Path $candidate) {
			return $candidate
		}
	}

	$fromCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
	if ($null -ne $fromCommand -and -not [string]::IsNullOrWhiteSpace($fromCommand.Source)) {
		return $fromCommand.Source
	}

	throw 'ISCC.exe not found. Install Inno Setup 6 or add ISCC.exe to PATH.'
}

function Resolve-OutputDirectory([string]$path)
{
	if ([string]::IsNullOrWhiteSpace($path)) {
		throw 'Output directory cannot be empty.'
	}

	if ([System.IO.Path]::IsPathRooted($path)) {
		return [System.IO.Path]::GetFullPath($path)
	}

	return [System.IO.Path]::GetFullPath((Join-Path $root $path))
}

$dmOut = Resolve-OutputDirectory $DmInstallerOutputDir
$playerOut = Resolve-OutputDirectory $PlayerInstallerOutputDir
$disableNetworkingBuild = if ($EnableNetworkingFeatures) { 'false' } else { 'true' }

function Clear-PublishOutput([string]$path)
{
	if (-not (Test-Path $path)) {
		New-Item -ItemType Directory -Path $path | Out-Null
		return
	}

	# Keep all previously released installer EXEs by default.
	Get-ChildItem -Path $path -Force |
		Where-Object { $_.Name -notlike '*-Setup-*.exe' -and $_.Name -ne 'Backup' } |
		Remove-Item -Recurse -Force
}

function Get-VersionFromSetupName([string]$name)
{
	$match = [regex]::Match($name, '(\d+\.\d+\.\d+)')
	if ($match.Success) {
		return [version]$match.Groups[1].Value
	}

	return [version]'0.0.0'
}

function Get-BackupDirectory([string]$path)
{
	$backupPath = Join-Path $path 'Backup'
	if (-not (Test-Path $backupPath)) {
		New-Item -ItemType Directory -Path $backupPath | Out-Null
	}
	return $backupPath
}

function Move-ToBackup([System.IO.FileInfo]$file, [string]$backupPath)
{
	$destinationPath = Join-Path $backupPath $file.Name
	if (Test-Path $destinationPath) {
		$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
		$destinationPath = Join-Path $backupPath ("{0}-{1}{2}" -f $file.BaseName, $stamp, $file.Extension)
	}

	Move-Item -Path $file.FullName -Destination $destinationPath -Force
}

function Remove-OldSetupInstallers([string]$path)
{
	if (-not $RemoveOldInstallerVersions) {
		return
	}

	$setupFiles = Get-ChildItem -Path $path -Filter '*-Setup-*.exe' -File -ErrorAction SilentlyContinue

	if ($setupFiles.Count -le 1) {
		return
	}

	$latestInstaller = $setupFiles |
		Sort-Object @{ Expression = { Get-VersionFromSetupName $_.Name }; Descending = $true }, @{ Expression = { $_.LastWriteTime }; Descending = $true } |
		Select-Object -First 1

	$keepers = @($latestInstaller)
	$keepers += $setupFiles |
		Where-Object { (Get-VersionFromSetupName $_.Name) -eq $PinnedInstallerVersion }

	$keeperSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
	foreach ($k in $keepers) {
		if ($null -ne $k) {
			[void]$keeperSet.Add($k.FullName)
		}
	}

	$backupPath = Get-BackupDirectory $path
	$archivedCount = 0
	foreach ($setupFile in $setupFiles) {
		if (-not $keeperSet.Contains($setupFile.FullName)) {
			Move-ToBackup -file $setupFile -backupPath $backupPath
			$archivedCount++
		}
	}

	Write-Host ("Archived {0} setup installer(s) to {1} while preserving latest and pinned {2}" -f $archivedCount, $backupPath, $PinnedInstallerVersion)
	return
}

function Keep-InstallerArtifactsOnly([string]$path)
{
	if (-not (Test-Path $path)) {
		return
	}

	Get-ChildItem -Path $path -Force |
		Where-Object { $_.Name -notlike '*-Setup-*.exe' -and $_.Name -ne 'Backup' } |
		Remove-Item -Recurse -Force
}

function Reset-Directory([string]$path)
{
	if (Test-Path $path) {
		Remove-Item -Path $path -Recurse -Force
	}

	New-Item -ItemType Directory -Path $path | Out-Null
}

function Publish-SingleFile([string]$editionConstants, [string]$assemblyName, [string]$outputPath)
{
	& $dotnet publish $project `
		-c Release `
		-r win-x64 `
		--self-contained true `
		-o $outputPath `
		-p:SkipUpdateFolderStaging=true `
		-p:DisableNetworkingBuild=$disableNetworkingBuild `
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

$iscc = Resolve-IsccPath

Write-Host 'Preparing output folders...'
Clear-PublishOutput $dmOut
Clear-PublishOutput $playerOut
Reset-Directory $dmStage
Reset-Directory $playerStage

Write-Host 'Publishing single-file DM build...'
Publish-SingleFile -editionConstants 'APP_EDITION_DM' -assemblyName 'DungeonMasterCortex' -outputPath $dmStage

Write-Host 'Publishing single-file Player build...'
Publish-SingleFile -editionConstants 'APP_EDITION_PLAYER' -assemblyName 'PlayerCortex' -outputPath $playerStage

if (-not (Test-Path (Join-Path $dmStage 'DungeonMasterCortex.exe'))) {
	throw 'DM publish did not produce DungeonMasterCortex.exe'
}

if (-not (Test-Path (Join-Path $playerStage 'PlayerCortex.exe'))) {
	throw 'Player publish did not produce PlayerCortex.exe'
}

Write-Host 'Building installers...'
& $iscc "/DBuildSourceDir=$dmStage" "/DOutputDir=$dmOut" $dmIss
& $iscc "/DBuildSourceDir=$playerStage" "/DOutputDir=$playerOut" $playerIss

Write-Host 'Removing non-installer publish artifacts from release folders...'
Keep-InstallerArtifactsOnly -path $dmOut
Keep-InstallerArtifactsOnly -path $playerOut

Remove-OldSetupInstallers -path $dmOut
Remove-OldSetupInstallers -path $playerOut

Get-ChildItem $dmOut -Filter 'DMCodex-Setup-*.exe' | Sort-Object Name | Format-Table FullName, Length, LastWriteTime
Get-ChildItem $playerOut -Filter 'PlayerCodex-Setup-*.exe' | Sort-Object Name | Format-Table FullName, Length, LastWriteTime
