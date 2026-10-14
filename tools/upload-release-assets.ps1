param(
    [string]$Repo = 'DMMavalek/DMCortex',
    [string]$Tag = '',
    [string]$Version = '',
    [string]$Token = '',
    [int]$MaxRetries = 4,
    [int]$RetryDelaySeconds = 5
)

$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

if ([string]::IsNullOrWhiteSpace($Tag)) {
    $projectPath = Join-Path $root 'DungeonMasterCortex\DungeonMasterCortex.csproj'
    [xml]$projectXml = Get-Content -Raw -Path $projectPath
    $projectVersion = [string]($projectXml.Project.PropertyGroup.Version | Select-Object -First 1)
    if ([string]::IsNullOrWhiteSpace($projectVersion)) {
        throw 'Could not infer release tag: missing <Version> in DungeonMasterCortex.csproj.'
    }
    $Tag = "v$($projectVersion.Trim())"
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    if ($Tag.StartsWith('v', [System.StringComparison]::OrdinalIgnoreCase)) {
        $Version = $Tag.Substring(1)
    }
    else {
        $Version = $Tag
    }
}

$resolvedToken = $Token
if ([string]::IsNullOrWhiteSpace($resolvedToken)) {
    $resolvedToken = $env:GITHUB_TOKEN
}
if ([string]::IsNullOrWhiteSpace($resolvedToken)) {
    $resolvedToken = [Environment]::GetEnvironmentVariable('GITHUB_TOKEN', 'User')
}
if ([string]::IsNullOrWhiteSpace($resolvedToken)) {
    throw 'GitHub token is required. Pass -Token or set GITHUB_TOKEN.'
}

if ($MaxRetries -lt 1) {
    $MaxRetries = 1
}
if ($RetryDelaySeconds -lt 1) {
    $RetryDelaySeconds = 1
}

$headers = @{
    Authorization = "Bearer $resolvedToken"
    Accept = 'application/vnd.github+json'
    'X-GitHub-Api-Version' = '2022-11-28'
    'User-Agent' = 'DMCortex-Asset-Uploader'
}

function Invoke-WithRetry {
    param(
        [scriptblock]$Action,
        [string]$Description
    )

    $lastError = $null
    for ($attempt = 1; $attempt -le $MaxRetries; $attempt++) {
        try {
            return & $Action
        }
        catch {
            $lastError = $_
            if ($attempt -lt $MaxRetries) {
                Write-Host ("{0} failed (attempt {1}/{2}). Retrying in {3}s..." -f $Description, $attempt, $MaxRetries, $RetryDelaySeconds)
                Start-Sleep -Seconds $RetryDelaySeconds
            }
        }
    }

    throw "${Description} failed after ${MaxRetries} attempt(s): $($lastError.Exception.Message)"
}

function Get-ReleaseByTag([string]$RepoSlug, [string]$ReleaseTag, [hashtable]$Headers) {
    return Invoke-WithRetry -Description "Get release $ReleaseTag" -Action {
        Invoke-RestMethod -Method Get -Headers $Headers -Uri "https://api.github.com/repos/$RepoSlug/releases/tags/$ReleaseTag"
    }
}

$release = Get-ReleaseByTag -RepoSlug $Repo -ReleaseTag $Tag -Headers $headers
$uploadBase = [string]$release.upload_url
$templateStart = $uploadBase.IndexOf('{')
if ($templateStart -ge 0) {
    $uploadBase = $uploadBase.Substring(0, $templateStart)
}
if (-not [System.Uri]::IsWellFormedUriString($uploadBase, [System.UriKind]::Absolute)) {
    throw "Invalid GitHub upload URL base: '$uploadBase'"
}

$assetsToUpload = @(
    (Join-Path $root ("artifacts\\releases\\dm\\DMCodex-Setup-$Version.exe")),
    (Join-Path $root ("artifacts\\releases\\player\\PlayerCodex-Setup-$Version.exe"))
)

$tempRoot = Join-Path $env:TEMP ('DMCortexReleaseUpload-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

try {
    foreach ($assetPath in $assetsToUpload) {
        if (-not (Test-Path $assetPath)) {
            throw "Missing asset file: $assetPath"
        }

        $name = [System.IO.Path]::GetFileName($assetPath)
        $release = Get-ReleaseByTag -RepoSlug $Repo -ReleaseTag $Tag -Headers $headers
        $existing = @($release.assets | Where-Object { $_.name -eq $name } | Select-Object -First 1)
        if ($existing.Count -gt 0) {
            Invoke-WithRetry -Description "Delete existing asset $name" -Action {
                Invoke-RestMethod -Method Delete -Headers $headers -Uri "https://api.github.com/repos/$Repo/releases/assets/$($existing[0].id)" | Out-Null
            }
        }

        $tempAssetPath = Join-Path $tempRoot $name
        Copy-Item -Path $assetPath -Destination $tempAssetPath -Force

        $uploadUrl = ('{0}?name={1}' -f $uploadBase, [uri]::EscapeDataString($name))
        Invoke-WithRetry -Description "Upload asset $name" -Action {
            Invoke-RestMethod -Method Post -Headers $headers -Uri $uploadUrl -InFile $tempAssetPath -ContentType 'application/octet-stream' | Out-Null
        }
        Write-Host "Uploaded $name"
    }

    $verify = Get-ReleaseByTag -RepoSlug $Repo -ReleaseTag $Tag -Headers $headers
    $expectedAssetNames = $assetsToUpload | ForEach-Object { [System.IO.Path]::GetFileName($_) }
    foreach ($expectedName in $expectedAssetNames) {
        $found = @($verify.assets | Where-Object { $_.name -eq $expectedName -and $_.state -eq 'uploaded' } | Select-Object -First 1)
        if ($found.Count -eq 0) {
            throw "Release verification failed. Missing uploaded asset: $expectedName"
        }
    }

    Write-Host "Release $Tag now has $(@($verify.assets).Count) assets"
    foreach ($asset in @($verify.assets)) {
        Write-Host (" - {0} [{1}]" -f $asset.name, $asset.state)
    }
}
finally {
    Remove-Item -Path $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
