param(
	[string]$Tag,
	[string]$Title,
	[string]$Notes = '',
	[string]$NotesFile = '',
	[string]$Token = '',
	[switch]$Draft,
	[switch]$Prerelease,
	[switch]$RemoveOldInstallerVersions
)

$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '.')).Path
$buildScript = Join-Path $root 'build-installers.ps1'
$releaseRoot = Join-Path $root 'artifacts\github-release'
$dmOut = Join-Path $releaseRoot 'dm'
$playerOut = Join-Path $releaseRoot 'player'

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

	$uploadBase = ($Release.upload_url -replace '\{\?name,label\}$', '')
	$uploadUrl = "$uploadBase?name=$([uri]::EscapeDataString($file.Name))"
	Invoke-RestMethod -Method Post -Headers $Headers -Uri $uploadUrl -InFile $file.FullName -ContentType 'application/octet-stream' | Out-Null
	Write-Host "Uploaded $($file.Name)"
}

if ([string]::IsNullOrWhiteSpace($Tag)) {
	$version = Get-ReleaseVersionFromProject
	$Tag = "v$version"
}

if ([string]::IsNullOrWhiteSpace($Title)) {
	$Title = $Tag
}

$releaseNotes = Get-ReleaseNotesText -InlineNotes $Notes -Path $NotesFile

if (Test-Path $releaseRoot) {
	Remove-Item $releaseRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
New-Item -ItemType Directory -Path $dmOut -Force | Out-Null
New-Item -ItemType Directory -Path $playerOut -Force | Out-Null

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

if ($null -eq $dmInstaller -or $null -eq $playerInstaller) {
	throw 'Could not locate both DM and Player installer EXEs in local release staging.'
}

$tokenValue = if (-not [string]::IsNullOrWhiteSpace($Token)) { $Token } else { $env:GITHUB_TOKEN }
$headers = Get-GitHubHeaders $tokenValue
$repoSlug = Get-OriginRepositorySlug

Write-Host "Publishing release $Tag to $repoSlug..."
$release = Get-OrCreateRelease -RepoSlug $repoSlug -Headers $headers -ReleaseTag $Tag -ReleaseTitle $Title -Body $releaseNotes -IsDraft:$Draft -IsPrerelease:$Prerelease

Upload-ReleaseAsset -Release $release -Headers $headers -RepoSlug $repoSlug -FilePath $dmInstaller.FullName
Upload-ReleaseAsset -Release $release -Headers $headers -RepoSlug $repoSlug -FilePath $playerInstaller.FullName

Write-Host "Release ready: https://github.com/$repoSlug/releases/tag/$Tag"