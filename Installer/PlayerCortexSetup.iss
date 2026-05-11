; Inno Setup script for PlayerCortex (Player Edition)
; Build source expected in C:\Users\kelava\Documents\Projects\DMC Updates\playercortex

#define MyAppName "PlayerCortex"
#define MyAppVersion "1.0.26"
#define MyAppPublisher "DungeonMasterCortex"
#define MyAppExeName "PlayerCortex.exe"
#define BuildSourceDir "C:\Users\kelava\Documents\Projects\DMC Updates\playercortex"
#define MyIconFile "C:\Users\kelava\Documents\Projects\Dungeon Master Cortex\Assets\Icons\Player Codex Icon.ico"

[Setup]
AppId={{FEA8D4C3-84B7-4B8D-8A48-12A294BB305D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\PlayerCortex
DefaultGroupName=PlayerCortex
DisableProgramGroupPage=yes
OutputDir=C:\Users\kelava\Documents\Projects\DMC Updates\playercortex
OutputBaseFilename=PlayerCortex-Setup-1.0.26
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
Source: "{#BuildSourceDir}\*"; DestDir: "{app}"; Excludes: "*-Setup-*.exe"; Flags: recursesubdirs createallsubdirs ignoreversion

[InstallDelete]
Type: filesandordirs; Name: "{autopf}\Player Cortex"
Type: filesandordirs; Name: "{autopf}\PlayerCortex Old"

[Icons]
Name: "{autoprograms}\PlayerCortex"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{#MyIconFile}"
Name: "{autodesktop}\PlayerCortex"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; IconFilename: "{#MyIconFile}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch PlayerCortex"; Flags: nowait postinstall
