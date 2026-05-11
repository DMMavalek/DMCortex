; Inno Setup script for Dungeon Master Codex Activation Tool
; Build first:
; dotnet publish "C:\Users\kelava\Documents\Projects\Dungeon Master Cortex\DungeonMasterCortex.Tools\DungeonMasterCortex.Tools.csproj" -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o "C:\Users\kelava\Documents\Projects\DMC Updates\activation-tool"

#define MyAppName "Dungeon Master Codex Activation Tool"
#define MyAppVersion "1.1.00"
#define MyAppPublisher "Dungeon Master Codex"
#define MyAppExeName "DungeonMasterCortex.Tools.exe"
#define BuildSourceDir "C:\Users\kelava\Documents\Projects\DMC Updates\activation-tool"
#define MyIconFile "C:\Users\kelava\Documents\Projects\Dungeon Master Cortex\Assets\Icons\License Activation Icon.ico"

[Setup]
AppId={{8D1B5A2E-0A2D-4BF8-B3E9-E980C4956D58}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\DungeonMasterCodex Activation Tool
DefaultGroupName=Dungeon Master Codex Activation Tool
DisableProgramGroupPage=yes
OutputDir=C:\Users\kelava\Documents\Projects\DMC Updates\activation-tool
OutputBaseFilename=DungeonMasterCodex-ActivationTool-Setup-1.1.00
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
SetupIconFile={#MyIconFile}
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"

[Files]
Source: "{#BuildSourceDir}\*"; DestDir: "{app}"; Excludes: "*-Setup.exe, *-Setup-*.exe"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{autoprograms}\Dungeon Master Codex Activation Tool"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{#MyIconFile}"
Name: "{autodesktop}\Dungeon Master Codex Activation Tool"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; IconFilename: "{#MyIconFile}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Dungeon Master Codex Activation Tool"; Flags: nowait postinstall skipifsilent
