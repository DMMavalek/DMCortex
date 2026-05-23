param(
	[switch]$RemoveOldInstallerVersions,
	[switch]$EnableNetworkingFeatures,
	[switch]$SignInstallers,
	[string]$SigningCertPath = '',
	[string]$SigningCertPassword = '',
	[string]$TimestampUrl = 'http://timestamp.digicert.com',
	[string]$SignToolPath = '',
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
$stageRoot = Join-Path $root ('artifacts\installer-publish\' + [guid]::NewGuid().ToString('N'))
$dmStage = Join-Path $stageRoot 'dm'
$playerStage = Join-Path $stageRoot 'player'
$dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'

function Resolve-SignToolPath([string]$explicitPath)
{
	if (-not [string]::IsNullOrWhiteSpace($explicitPath)) {
		if (Test-Path $explicitPath) {
			return (Resolve-Path $explicitPath).Path
		}

		throw "signtool.exe not found at explicit path: $explicitPath"
	}

	$fromCommand = Get-Command signtool.exe -ErrorAction SilentlyContinue
	if ($null -ne $fromCommand -and -not [string]::IsNullOrWhiteSpace($fromCommand.Source)) {
		return $fromCommand.Source
	}

	$kitRoots = @(
		(Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'),
		(Join-Path $env:ProgramFiles 'Windows Kits\10\bin')
	) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path $_) }

	$matches = New-Object System.Collections.Generic.List[string]
	foreach ($kitRoot in $kitRoots) {
		$paths = Get-ChildItem -Path $kitRoot -Filter signtool.exe -Recurse -ErrorAction SilentlyContinue |
			Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
			ForEach-Object { $_.FullName }

		foreach ($p in $paths) {
			[void]$matches.Add($p)
		}
	}

	if ($matches.Count -eq 0) {
		throw 'signtool.exe not found. Install Windows SDK/Windows Kits Signing Tools or add signtool.exe to PATH.'
	}

	return $matches |
		Sort-Object {
			$path = $_
			$versionSegment = Split-Path (Split-Path $path -Parent) -Leaf
			try { [version]$versionSegment } catch { [version]'0.0.0.0' }
		} -Descending |
		Select-Object -First 1
}

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

function Sign-InstallerFile(
	[string]$signTool,
	[string]$certPath,
	[string]$certPassword,
	[string]$timestamp,
	[string]$installerPath)
{
	if (-not (Test-Path $installerPath)) {
		throw "Installer not found for signing: $installerPath"
	}

	$signArgs = @(
		'sign',
		'/fd', 'SHA256',
		'/td', 'SHA256',
		'/tr', $timestamp,
		'/f', $certPath
	)

	if (-not [string]::IsNullOrWhiteSpace($certPassword)) {
		$signArgs += @('/p', $certPassword)
	}

	$signArgs += $installerPath
	& $signTool @signArgs

	$signature = Get-AuthenticodeSignature -FilePath $installerPath
	if ($signature.Status -eq [System.Management.Automation.SignatureStatus]::NotSigned) {
		throw "Signature was not applied to installer: $installerPath"
	}
}

if (-not (Test-Path $dotnet)) {
	throw "dotnet.exe not found at $dotnet"
}

$iscc = Resolve-IsccPath
$effectiveSigningCertPath = if (-not [string]::IsNullOrWhiteSpace($SigningCertPath)) { $SigningCertPath } else { $env:SIGNING_CERT_PATH }
$effectiveSigningCertPassword = if (-not [string]::IsNullOrWhiteSpace($SigningCertPassword)) { $SigningCertPassword } else { $env:SIGNING_CERT_PASSWORD }
$effectiveTimestampUrl = if (-not [string]::IsNullOrWhiteSpace($TimestampUrl)) { $TimestampUrl } else { 'http://timestamp.digicert.com' }
$resolvedSignToolPath = $null

if ($SignInstallers) {
	if ([string]::IsNullOrWhiteSpace($effectiveSigningCertPath)) {
		throw 'Signing requested but no certificate path was provided. Use -SigningCertPath or set SIGNING_CERT_PATH.'
	}

	$resolvedSigningCertPath = if ([System.IO.Path]::IsPathRooted($effectiveSigningCertPath)) {
		[System.IO.Path]::GetFullPath($effectiveSigningCertPath)
	} else {
		[System.IO.Path]::GetFullPath((Join-Path $root $effectiveSigningCertPath))
	}

	if (-not (Test-Path $resolvedSigningCertPath)) {
		throw "Signing certificate not found: $resolvedSigningCertPath"
	}

	$effectiveSigningCertPath = $resolvedSigningCertPath
	$resolvedSignToolPath = Resolve-SignToolPath -explicitPath $SignToolPath
	Write-Host "Signing enabled with certificate: $effectiveSigningCertPath"
}

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

if ($SignInstallers) {
	Write-Host 'Signing installer executables...'
	$dmInstaller = Get-ChildItem $dmOut -Filter 'DMCodex-Setup-*.exe' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
	$playerInstaller = Get-ChildItem $playerOut -Filter 'PlayerCodex-Setup-*.exe' | Sort-Object LastWriteTime -Descending | Select-Object -First 1

	if ($null -eq $dmInstaller -or $null -eq $playerInstaller) {
		throw 'Signing requested but installer output files were not found.'
	}

	Sign-InstallerFile -signTool $resolvedSignToolPath -certPath $effectiveSigningCertPath -certPassword $effectiveSigningCertPassword -timestamp $effectiveTimestampUrl -installerPath $dmInstaller.FullName
	Sign-InstallerFile -signTool $resolvedSignToolPath -certPath $effectiveSigningCertPath -certPassword $effectiveSigningCertPassword -timestamp $effectiveTimestampUrl -installerPath $playerInstaller.FullName
}

Write-Host 'Removing non-installer publish artifacts from release folders...'
Keep-InstallerArtifactsOnly -path $dmOut
Keep-InstallerArtifactsOnly -path $playerOut

Remove-OldSetupInstallers -path $dmOut
Remove-OldSetupInstallers -path $playerOut

Get-ChildItem $dmOut -Filter 'DMCodex-Setup-*.exe' | Sort-Object Name | Format-Table FullName, Length, LastWriteTime
Get-ChildItem $playerOut -Filter 'PlayerCodex-Setup-*.exe' | Sort-Object Name | Format-Table FullName, Length, LastWriteTime
