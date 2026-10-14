$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'DungeonMasterCortex\DungeonMasterCortex.csproj'
$iss = Join-Path $root 'Installer\DMCortexSetup.iss'
$iscc = 'C:\Users\kelava\AppData\Local\Programs\Inno Setup 6\ISCC.exe'
$stage = Join-Path $root ('artifacts\installer-publish\dm-' + [guid]::NewGuid().ToString('N'))
$outputDir = Join-Path $env:USERPROFILE 'Documents\Projects\DMC Updates\DMCodex'

if (-not (Test-Path $iscc)) {
	throw "ISCC.exe not found at $iscc"
}

New-Item -ItemType Directory -Path $stage -Force | Out-Null

& dotnet publish $project `
	-c Release `
	-r win-x64 `
	--self-contained true `
	-o $stage `
	-p:SkipUpdateFolderStaging=true `
	-p:DisableNetworkingBuild=true `
	-p:DefineConstants=APP_EDITION_DM `
	-p:AssemblyName=DungeonMasterCortex `
	-p:PublishSingleFile=true `
	-p:EnableCompressionInSingleFile=true `
	-p:IncludeNativeLibrariesForSelfExtract=true `
	-p:DebugType=None `
	-p:DebugSymbols=false

if ($LASTEXITCODE -ne 0) {
	throw "dotnet publish failed with exit code $LASTEXITCODE"
}

& $iscc "/DBuildSourceDir=$stage" "/DOutputDir=$outputDir" $iss
if ($LASTEXITCODE -ne 0) {
	throw "ISCC compile failed with exit code $LASTEXITCODE"
}
