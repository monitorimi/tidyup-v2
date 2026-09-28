#define AppName "TidyUp"
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{F2ED7DB6-3785-4FBF-A346-51FB6CA57DE1}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Your Name
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=TidyUp-Setup
Compression=lzma2/ultra
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
UninstallDisplayIcon={app}\TidyUp.exe

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "..\publish\TidyUp.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\TidyUp.exe"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\TidyUp.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\TidyUp.exe"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent
