#define AppName "LoomX"

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#ifndef SourceDir
  #define SourceDir "..\artifacts\publish"
#endif

#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif

[Setup]
AppId={{B7E6A6A3-7C8D-4EAF-9D67-0C5E9B5F5A4C}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=LoomX
AppPublisherURL=https://github.com/Bian-Sh/Loom-X
DefaultDirName={localappdata}\Programs\LoomX
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=LoomX-{#AppVersion}-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
Uninstallable=yes
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\LoomX.exe
SetupLogging=yes
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "startmenuicon"; Description: "创建开始菜单快捷方式"
Name: "desktopicon"; Description: "创建桌面快捷方式"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion createallsubdirs

[Icons]
Name: "{autoprograms}\LoomX"; Filename: "{app}\LoomX.exe"; WorkingDir: "{app}"; Tasks: startmenuicon
Name: "{autodesktop}\LoomX"; Filename: "{app}\LoomX.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\LoomX.exe"; Description: "启动 LoomX"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent
