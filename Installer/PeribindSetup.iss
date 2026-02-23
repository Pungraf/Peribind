; Peribind Windows installer (Inno Setup 6)
; Build from repository root:
;   "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" Installer\PeribindSetup.iss
;
; Before compile:
; 1) Build launcher to Build\Launcher
; 2) Update MyAppVersion for each release

#define MyAppName "Peribind"
#define MyAppVersion "0.2.2"
#define MyAppPublisher "Peribind Team"
#define MyAppURL "https://download.peribind.com"
#define MyAppExeName "PeribindLauncher.exe"

[Setup]
AppId={{3E6FCE12-9B9F-4F04-9DB0-E6B95A807D2C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
OutputDir=..\Build\Installer
OutputBaseFilename=PeribindSetup_{#MyAppVersion}
SetupLogging=yes
SetupIconFile=..\Launcher\PeribindLogo.ico

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
; Install all launcher runtime files produced in Build\Launcher.
; Exclude debug symbols from installer package.
Source: "..\Build\Launcher\*"; DestDir: "{app}"; Flags: ignoreversion; Excludes: "*.pdb"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
