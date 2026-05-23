param(
    [string]$Repo = 'DMMavalek/DMCortex',
    [string]$Tag = 'v1.3.18',
    [string]$Token = ''
)

$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
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

$headers = @{
    Authorization = "Bearer $resolvedToken"
    Accept = 'application/vnd.github+json'
    'X-GitHub-Api-Version' = '2022-11-28'
    'User-Agent' = 'DMCortex-Asset-Uploader'
}

$release = Invoke-RestMethod -Method Get -Headers $headers -Uri "https://api.github.com/repos/$Repo/releases/tags/$Tag"
$uploadBase = [string]$release.upload_url
$templateStart = $uploadBase.IndexOf('{')
if ($templateStart -ge 0) {
    $uploadBase = $uploadBase.Substring(0, $templateStart)
}
if (-not [System.Uri]::IsWellFormedUriString($uploadBase, [System.UriKind]::Absolute)) {
    throw "Invalid GitHub upload URL base: '$uploadBase'"
}

$assetsToUpload = @(
    (Join-Path $root 'artifacts\releases\dm\DMCodex-Setup-1.3.18.exe'),
    (Join-Path $root 'artifacts\releases\player\PlayerCodex-Setup-1.3.18.exe')
)

foreach ($assetPath in $assetsToUpload) {
    if (-not (Test-Path $assetPath)) {
        throw "Missing asset file: $assetPath"
    }

    $name = [System.IO.Path]::GetFileName($assetPath)
    $existing = @($release.assets | Where-Object { $_.name -eq $name } | Select-Object -First 1)
    if ($existing.Count -gt 0) {
        Invoke-RestMethod -Method Delete -Headers $headers -Uri "https://api.github.com/repos/$Repo/releases/assets/$($existing[0].id)" | Out-Null
    }

    $uploadUrl = ('{0}?name={1}' -f $uploadBase, [uri]::EscapeDataString($name))
    Invoke-RestMethod -Method Post -Headers $headers -Uri $uploadUrl -InFile $assetPath -ContentType 'application/octet-stream' | Out-Null
    Write-Host "Uploaded $name"
}

$verify = Invoke-RestMethod -Method Get -Headers $headers -Uri "https://api.github.com/repos/$Repo/releases/tags/$Tag"
Write-Host "Release $Tag now has $(@($verify.assets).Count) assets"
foreach ($asset in @($verify.assets)) {
    Write-Host (" - {0} [{1}]" -f $asset.name, $asset.state)
}
