; Inno Setup script for Dungeon Master Codex (DM Edition)
; Build source expected in C:\Users\kelava\Documents\Projects\DMC Updates\DMCodex

#define MyAppName "Dungeon Master Codex"
#define MyAppVersion "1.4.01"
#define MyAppPublisher "Dungeon Master Codex"
#define MyAppExeName "DungeonMasterCortex.exe"
#ifndef BuildSourceDir
#define BuildSourceDir "C:\Users\kelava\Documents\Projects\DMC Updates\DMCodex"
#endif
#ifndef OutputDir
#define OutputDir "C:\Users\kelava\Documents\Projects\DMC Updates\DMCodex"
#endif
#define MyIconFile "..\\Assets\\Icons\\DM Codex Icon.ico"

#ifnexist AddBackslash(BuildSourceDir) + MyAppExeName
	#error BuildSourceDir does not contain {#MyAppExeName}. Run build-installers.ps1 before compiling installer.
#endif

[Setup]
AppId={{6C90CF36-E88D-4DAB-9E79-C2CBF36D7A6C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\DMCodex
DefaultGroupName=Dungeon Master Codex
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=DMCodex-Setup-1.4.01
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
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Dungeon Master Codex"; Flags: nowait postinstall skipifsilent
Filename: "{app}\{#MyAppExeName}"; Flags: nowait skipifnotsilent
