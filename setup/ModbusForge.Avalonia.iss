; ModbusForge Avalonia Inno Setup Script

#ifndef AppVersion
  #define AppVersion "2026.7.12"
#endif

[Setup]
AppName=ModbusForge
AppVersion={#AppVersion}
AppPublisher=ModbusForge
DefaultDirName={autopf}\ModbusForge
DefaultGroupName=ModbusForge
UninstallDisplayIcon={app}\ModbusForge.Avalonia.exe
CloseApplications=force

WizardStyle=modern
OutputBaseFilename=ModbusForge-Avalonia-{#AppVersion}-setup
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
Name: "{group}\ModbusForge"; Filename: "{app}\ModbusForge.Avalonia.exe"
Name: "{autodesktop}\ModbusForge"; Filename: "{app}\ModbusForge.Avalonia.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\ModbusForge.Avalonia.exe"; Description: "{cm:LaunchProgram,ModbusForge}"; Flags: nowait postinstall skipifsilent
