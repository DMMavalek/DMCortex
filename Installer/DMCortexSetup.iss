; Inno Setup script for DMCortex (DM Edition)
; Build source expected in C:\Users\kelava\Documents\Projects\DMC Updates\dmcortex

#define MyAppName "DMCortex"
#define MyAppVersion "1.0.26"
#define MyAppPublisher "DungeonMasterCortex"
#define MyAppExeName "DungeonMasterCortex.exe"
#define BuildSourceDir "C:\Users\kelava\Documents\Projects\DMC Updates\dmcortex"
#define MyIconFile "C:\Users\kelava\Documents\Projects\Dungeon Master Cortex\Assets\Icons\DM Codex Icon.ico"

[Setup]
AppId={{6C90CF36-E88D-4DAB-9E79-C2CBF36D7A6C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\DMCortex
DefaultGroupName=DMCortex
DisableProgramGroupPage=yes
OutputDir=C:\Users\kelava\Documents\Projects\DMC Updates\dmcortex
OutputBaseFilename=DMCortex-Setup-1.0.26
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
Name: "{autoprograms}\DMCortex"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{#MyIconFile}"
Name: "{autodesktop}\DMCortex"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; IconFilename: "{#MyIconFile}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch DMCortex"; Flags: nowait postinstall
