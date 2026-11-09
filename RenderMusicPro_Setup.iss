#define MyAppName "RenderMusicPro"
#define MyAppVersion "1.5.3"
#define MyAppPublisher "TDN Media"
#define MyAppExeName "RenderMusicPro.exe"
#define MyAppBuildDir "Release_v1.5.3"

[Setup]
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\RenderMusicPro
DefaultGroupName=RenderMusicPro
OutputDir={#MyAppBuildDir}
OutputBaseFilename=RenderMusicPro_Setup_v{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}

[Files]
Source: "{#MyAppBuildDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\RenderMusicPro"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\RenderMusicPro"; Filename: "{app}\{#MyAppExeName}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Chạy RenderMusicPro"; Flags: nowait postinstall skipifsilent
