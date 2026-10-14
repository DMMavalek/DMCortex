param(
	[string]$Tag,
	[string]$Title,
	[string]$Notes = '',
	[string]$NotesFile = '',
	[string]$Token = '',
	[switch]$Draft,
	[switch]$Prerelease,
	[switch]$RemoveOldInstallerVersions,
	[switch]$ForceInstallerBuild
)

$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '.')).Path
$buildScript = Join-Path $root 'build-installers.ps1'
$releaseRoot = $null
$dmOut = $null
$playerOut = $null

function Get-OriginRepositorySlug {
	$remote = (git -C $root config --get remote.origin.url).Trim()
	if ([string]::IsNullOrWhiteSpace($remote)) {
		throw 'No git origin remote configured.'
	}

	if ($remote -match 'github\.com[:/](?<owner>[^/]+)/(?<repo>[^/.]+)(?:\.git)?$') {
		return "$($Matches.owner)/$($Matches.repo)"
	}

	throw "Origin remote is not a GitHub repository: $remote"
}

function Get-ReleaseVersionFromProject {
	$projectPath = Join-Path $root 'DungeonMasterCortex\DungeonMasterCortex.csproj'
	[xml]$xml = Get-Content $projectPath
	$versionNode = $xml.Project.PropertyGroup.Version | Select-Object -First 1
	if ([string]::IsNullOrWhiteSpace($versionNode)) {
		throw 'Could not determine version from DungeonMasterCortex.csproj.'
	}
	return $versionNode.Trim()
}

function Get-VersionFromTag {
	param([string]$ReleaseTag)

	if ([string]::IsNullOrWhiteSpace($ReleaseTag)) {
		return ''
	}

	if ($ReleaseTag.StartsWith('v', [System.StringComparison]::OrdinalIgnoreCase)) {
		return $ReleaseTag.Substring(1)
	}

	return $ReleaseTag
}

function Get-ExistingInstallerAssets {
	param([string]$Version)

	$dmPath = Join-Path $root ("artifacts\releases\dm\DMCodex-Setup-$Version.exe")
	$playerPath = Join-Path $root ("artifacts\releases\player\PlayerCodex-Setup-$Version.exe")

	if ((Test-Path $dmPath) -and (Test-Path $playerPath)) {
		return @(
			(Get-Item $dmPath),
			(Get-Item $playerPath)
		)
	}

	return @()
}

function Get-GitHubHeaders([string]$githubToken) {
	if ([string]::IsNullOrWhiteSpace($githubToken)) {
		throw 'GitHub token is required. Pass -Token or set GITHUB_TOKEN.'
	}

	return @{
		Authorization = "Bearer $githubToken"
		Accept = 'application/vnd.github+json'
		'X-GitHub-Api-Version' = '2022-11-28'
		'User-Agent' = 'DMCortex-Release-Script'
	}
}

function Get-ReleaseNotesText {
	param([string]$InlineNotes, [string]$Path)

	if (-not [string]::IsNullOrWhiteSpace($Path)) {
		if (-not (Test-Path $Path)) {
			throw "Notes file not found: $Path"
		}
		return [System.IO.File]::ReadAllText((Resolve-Path $Path))
	}

	return $InlineNotes
}

function Test-ReleasePreflight {
	param(
		[string]$RepoSlug,
		[hashtable]$Headers
	)

	$repoApi = "https://api.github.com/repos/$RepoSlug"
	$repo = Invoke-RestMethod -Method Get -Headers $Headers -Uri $repoApi

	# When permissions are provided, enforce write access before doing any build work.
	if ($null -ne $repo.permissions -and
		$repo.permissions.PSObject.Properties.Name -contains 'push' -and
		-not [bool]$repo.permissions.push) {
		throw "GitHub token does not have write access to $RepoSlug. Grant repository Contents: Read and write."
	}

	Write-Host "Preflight passed for $RepoSlug"
}

function Get-OrCreateRelease {
	param(
		[string]$RepoSlug,
		[hashtable]$Headers,
		[string]$ReleaseTag,
		[string]$ReleaseTitle,
		[string]$Body,
		[bool]$IsDraft,
		[bool]$IsPrerelease
	)

	$repoApi = "https://api.github.com/repos/$RepoSlug"
	$tagUrl = "$repoApi/releases/tags/$ReleaseTag"

	try {
		return Invoke-RestMethod -Method Get -Headers $Headers -Uri $tagUrl
	}
	catch {
		$response = $_.Exception.Response
		if ($null -ne $response -and [int]$response.StatusCode -eq 404) {
			$payload = @{
				tag_name = $ReleaseTag
				name = $ReleaseTitle
				body = $Body
				draft = $IsDraft
				prerelease = $IsPrerelease
			} | ConvertTo-Json

			return Invoke-RestMethod -Method Post -Headers $Headers -Uri "$repoApi/releases" -Body $payload -ContentType 'application/json'
		}

		throw
	}
}

function Remove-ExistingAsset {
	param(
		[object]$Release,
		[hashtable]$Headers,
		[string]$AssetName,
		[string]$RepoSlug
	)

	$existing = @($Release.assets) | Where-Object { $_.name -eq $AssetName } | Select-Object -First 1
	if ($null -eq $existing) {
		return
	}

	$assetUrl = "https://api.github.com/repos/$RepoSlug/releases/assets/$($existing.id)"
	Invoke-RestMethod -Method Delete -Headers $Headers -Uri $assetUrl | Out-Null
}

function Upload-ReleaseAsset {
	param(
		[object]$Release,
		[hashtable]$Headers,
		[string]$RepoSlug,
		[string]$FilePath
	)

	$file = Get-Item $FilePath
	Remove-ExistingAsset -Release $Release -Headers $Headers -AssetName $file.Name -RepoSlug $RepoSlug

	$uploadBase = "https://uploads.github.com/repos/$RepoSlug/releases/$($Release.id)/assets"
	$uploadUrl = ('{0}?name={1}' -f $uploadBase, [uri]::EscapeDataString($file.Name))
	try {
		Invoke-RestMethod -Method Post -Headers $Headers -Uri $uploadUrl -InFile $file.FullName -ContentType 'application/octet-stream' | Out-Null
	}
	catch {
		$statusCode = $null
		$responseBody = $null
		if ($_.Exception.Response) {
			try { $statusCode = [int]$_.Exception.Response.StatusCode } catch { }
			try {
				$stream = $_.Exception.Response.GetResponseStream()
				if ($null -ne $stream) {
					$reader = New-Object System.IO.StreamReader($stream)
					$responseBody = $reader.ReadToEnd()
					$reader.Close()
				}
			} catch { }
		}
		throw "Failed to upload $($file.Name) to GitHub release. Status: $statusCode. Url: $uploadUrl. Response: $responseBody"
	}
	Write-Host "Uploaded $($file.Name)"
}

function Assert-ReleaseAssetsUploaded {
	param(
		[string]$RepoSlug,
		[hashtable]$Headers,
		[int]$ReleaseId,
		[string[]]$ExpectedAssetNames
	)

	$releaseApi = "https://api.github.com/repos/$RepoSlug/releases/$ReleaseId"
	$latest = Invoke-RestMethod -Method Get -Headers $Headers -Uri $releaseApi
	$assets = @($latest.assets)

	$missing = @()
	foreach ($name in $ExpectedAssetNames) {
		$asset = $assets | Where-Object { $_.name -eq $name -and $_.state -eq 'uploaded' } | Select-Object -First 1
		if ($null -eq $asset) {
			$missing += $name
		}
	}

	if ($missing.Count -gt 0) {
		$missingText = $missing -join ', '
		throw "Release verification failed. Missing uploaded assets: $missingText"
	}

	Write-Host "Release verification passed: $($ExpectedAssetNames -join ', ')"
}

if ([string]::IsNullOrWhiteSpace($Tag)) {
	$version = Get-ReleaseVersionFromProject
	$Tag = "v$version"
}

$releaseVersion = Get-VersionFromTag -ReleaseTag $Tag
if ([string]::IsNullOrWhiteSpace($releaseVersion)) {
	$releaseVersion = Get-ReleaseVersionFromProject
}

if ([string]::IsNullOrWhiteSpace($Title)) {
	$Title = $Tag
}

$releaseNotes = Get-ReleaseNotesText -InlineNotes $Notes -Path $NotesFile
$releaseRoot = Join-Path $root ('artifacts\github-release\' + $Tag + '-' + [guid]::NewGuid().ToString('N'))
$dmOut = Join-Path $releaseRoot 'dm'
$playerOut = Join-Path $releaseRoot 'player'

$tokenValue = if (-not [string]::IsNullOrWhiteSpace($Token)) { $Token } else { $env:GITHUB_TOKEN }
$headers = Get-GitHubHeaders $tokenValue
$repoSlug = Get-OriginRepositorySlug
Test-ReleasePreflight -RepoSlug $repoSlug -Headers $headers

New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
New-Item -ItemType Directory -Path $dmOut -Force | Out-Null
New-Item -ItemType Directory -Path $playerOut -Force | Out-Null

if (-not $ForceInstallerBuild) {
	$existingInstallers = Get-ExistingInstallerAssets -Version $releaseVersion
	if ($existingInstallers.Count -eq 2) {
		Write-Host "Using existing installers for version $releaseVersion from artifacts/releases."
		$dmInstaller = $existingInstallers | Where-Object { $_.Name -like 'DMCodex-Setup-*.exe' } | Select-Object -First 1
		$playerInstaller = $existingInstallers | Where-Object { $_.Name -like 'PlayerCodex-Setup-*.exe' } | Select-Object -First 1
	}
}

if ($null -eq $dmInstaller -or $null -eq $playerInstaller) {
	Write-Host 'Building installers into local GitHub release staging...'
	$buildArgs = @(
		'-ExecutionPolicy', 'Bypass',
		'-File', $buildScript,
		'-DmInstallerOutputDir', $dmOut,
		'-PlayerInstallerOutputDir', $playerOut
	)
	if ($RemoveOldInstallerVersions) {
		$buildArgs += '-RemoveOldInstallerVersions'
	}
	& powershell @buildArgs

	$dmInstaller = Get-ChildItem $dmOut -Filter 'DMCodex-Setup-*.exe' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
	$playerInstaller = Get-ChildItem $playerOut -Filter 'PlayerCodex-Setup-*.exe' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
}

if ($null -eq $dmInstaller -or $null -eq $playerInstaller) {
	throw 'Could not locate both DM and Player installer EXEs in local release staging.'
}

Write-Host "Publishing release $Tag to $repoSlug..."
$release = Get-OrCreateRelease -RepoSlug $repoSlug -Headers $headers -ReleaseTag $Tag -ReleaseTitle $Title -Body $releaseNotes -IsDraft:$Draft -IsPrerelease:$Prerelease

Upload-ReleaseAsset -Release $release -Headers $headers -RepoSlug $repoSlug -FilePath $dmInstaller.FullName
Upload-ReleaseAsset -Release $release -Headers $headers -RepoSlug $repoSlug -FilePath $playerInstaller.FullName
Assert-ReleaseAssetsUploaded -RepoSlug $repoSlug -Headers $headers -ReleaseId $release.id -ExpectedAssetNames @($dmInstaller.Name, $playerInstaller.Name)

Write-Host "Release ready: https://github.com/$repoSlug/releases/tag/$Tag"