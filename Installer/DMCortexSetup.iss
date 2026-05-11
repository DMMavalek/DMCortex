; Inno Setup script for Dungeon Master Codex (DM Edition)
; Build source expected in C:\Users\kelava\Documents\Projects\DMC Updates\DMCodex

#define MyAppName "Dungeon Master Codex"
#define MyAppVersion "1.1.00"
#define MyAppPublisher "Dungeon Master Codex"
#define MyAppExeName "DungeonMasterCortex.exe"
#define BuildSourceDir "C:\Users\kelava\Documents\Projects\DMC Updates\DMCodex"
#define MyIconFile "C:\Users\kelava\Documents\Projects\Dungeon Master Cortex\Assets\Icons\DM Codex Icon.ico"

[Setup]
AppId={{6C90CF36-E88D-4DAB-9E79-C2CBF36D7A6C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\DMCodex
DefaultGroupName=Dungeon Master Codex
DisableProgramGroupPage=yes
OutputDir=C:\Users\kelava\Documents\Projects\DMC Updates\DMCodex
OutputBaseFilename=DMCodex-Setup-1.1.00
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
Type: filesandordirs; Name: "{autopf}\DungeonMasterCortex"
Type: filesandordirs; Name: "{autopf}\DMCortex Old"

[Icons]
Name: "{autoprograms}\Dungeon Master Codex"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{#MyIconFile}"
Name: "{autodesktop}\Dungeon Master Codex"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; IconFilename: "{#MyIconFile}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Dungeon Master Codex"; Flags: nowait postinstall
