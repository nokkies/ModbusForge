; ModbusForge Inno Setup Script
; See https://jrsoftware.org/isinfo.php for documentation

#ifndef AppVersion
  #define AppVersion "2026.8.20"
#endif

[Setup]
AppName=ModbusForge
AppVersion={#AppVersion}
AppPublisher=ModbusForge
DefaultDirName={autopf}\ModbusForge
DefaultGroupName=ModbusForge
UninstallDisplayIcon={app}\ModbusForge.exe
CloseApplications=force

WizardStyle=modern
OutputBaseFilename=ModbusForge-{#AppVersion}-setup
OutputDir=..\installers
Compression=lzma2
SolidCompression=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\publish\avalonia\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\ModbusForge"; Filename: "{app}\ModbusForge.exe"
Name: "{autodesktop}\ModbusForge"; Filename: "{app}\ModbusForge.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\ModbusForge.exe"; Description: "{cm:LaunchProgram,ModbusForge}"; Flags: nowait postinstall skipifsilent
