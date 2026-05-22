; Inno Setup script for Player Codex (Player Edition)
; Build source expected in C:\Users\kelava\Documents\Projects\DMC Updates\playercodex

#define MyAppName "Player Codex"
#define MyAppVersion "1.3.16"
#define MyAppPublisher "Dungeon Master Codex"
#define MyAppExeName "PlayerCortex.exe"
#ifndef BuildSourceDir
#define BuildSourceDir "C:\Users\kelava\Documents\Projects\DMC Updates\playercodex"
#endif
#ifndef OutputDir
#define OutputDir "C:\Users\kelava\Documents\Projects\DMC Updates\playercodex"
#endif
#define MyIconFile "..\\Assets\\Icons\\Player Codex Icon.ico"

#ifnexist AddBackslash(BuildSourceDir) + MyAppExeName
	#error BuildSourceDir does not contain {#MyAppExeName}. Run build-installers.ps1 before compiling installer.
#endif

[Setup]
AppId={{FEA8D4C3-84B7-4B8D-8A48-12A294BB305D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\PlayerCodex
DefaultGroupName=Player Codex
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=PlayerCodex-Setup-1.3.16
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
Type: filesandordirs; Name: "{autopf}\Player Codex"
Type: filesandordirs; Name: "{autopf}\PlayerCodex Old"

[Icons]
Name: "{autoprograms}\Player Codex"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{#MyIconFile}"
Name: "{autodesktop}\Player Codex"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; IconFilename: "{#MyIconFile}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Player Codex"; Flags: nowait postinstall skipifsilent
Filename: "{app}\{#MyAppExeName}"; Flags: nowait skipifnotsilent
