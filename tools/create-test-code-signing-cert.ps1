param(
    [string]$Subject = 'CN=Dungeon Master Cortex Test Signing',
    [string]$OutputDirectory = 'artifacts/codesign',
    [SecureString]$PfxPassword = $null,
    [string]$PfxPasswordPlainText = '',
    [int]$ValidYears = 2
)

$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$resolvedOutputDirectory = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    [System.IO.Path]::GetFullPath($OutputDirectory)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
}

if (-not (Test-Path $resolvedOutputDirectory)) {
    New-Item -ItemType Directory -Path $resolvedOutputDirectory -Force | Out-Null
}

$displayPassword = $null
$securePassword = $PfxPassword
if ($null -eq $securePassword) {
    if ([string]::IsNullOrWhiteSpace($PfxPasswordPlainText)) {
        $displayPassword = [Guid]::NewGuid().ToString('N')
    }
    else {
        $displayPassword = $PfxPasswordPlainText
    }

    $securePassword = ConvertTo-SecureString -String $displayPassword -AsPlainText -Force
}
$friendlyName = 'DungeonMasterCortex Test Code Signing'
$cert = New-SelfSignedCertificate `
    -Type CodeSigningCert `
    -Subject $Subject `
    -FriendlyName $friendlyName `
    -CertStoreLocation 'Cert:\CurrentUser\My' `
    -KeyAlgorithm RSA `
    -KeyLength 3072 `
    -HashAlgorithm SHA256 `
    -NotAfter (Get-Date).AddYears($ValidYears) `
    -KeyExportPolicy Exportable

if ($null -eq $cert) {
    throw 'Failed to create self-signed certificate.'
}

$pfxPath = Join-Path $resolvedOutputDirectory 'dmcortex-test-codesign.pfx'
$cerPath = Join-Path $resolvedOutputDirectory 'dmcortex-test-codesign.cer'

Export-PfxCertificate -Cert "Cert:\CurrentUser\My\$($cert.Thumbprint)" -FilePath $pfxPath -Password $securePassword | Out-Null
Export-Certificate -Cert "Cert:\CurrentUser\My\$($cert.Thumbprint)" -FilePath $cerPath | Out-Null

$base64Path = Join-Path $resolvedOutputDirectory 'dmcortex-test-codesign.pfx.base64.txt'
[System.Convert]::ToBase64String([System.IO.File]::ReadAllBytes($pfxPath)) | Set-Content -Path $base64Path -NoNewline

Write-Host "Created test code-signing certificate."
Write-Host "PFX: $pfxPath"
Write-Host "CER: $cerPath"
Write-Host "Base64 (for GitHub secret): $base64Path"
if (-not [string]::IsNullOrWhiteSpace($displayPassword)) {
    Write-Host "Password: $displayPassword"
}
else {
    Write-Host 'Password: [provided via SecureString parameter]'
}
Write-Host ''
Write-Host 'Note: This is a self-signed test certificate. It does NOT provide SmartScreen reputation for end users.'
