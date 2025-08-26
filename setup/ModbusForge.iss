; ModbusForge Inno Setup Script
; See https://jrsoftware.org/isinfo.php for documentation

[Setup]
; Basic application info
AppName=ModbusForge
AppVersion=1.2.1
AppPublisher=ModbusForge
DefaultDirName={autopf}\ModbusForge
DefaultGroupName=ModbusForge
UninstallDisplayIcon={app}\ModbusForge.exe

; Setup output settings
WizardStyle=modern
OutputBaseFilename=ModbusForge-1.2.1-setup
OutputDir=.\installers
Compression=lzma2
SolidCompression=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; These files are expected to be in a 'publish' directory relative to the project root.
; The Inno Setup compiler should be run from the project root directory.
Source: ".\publish\win-x64\ModbusForge.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: ".\publish\win-x64\appsettings.json"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\ModbusForge"; Filename: "{app}\ModbusForge.exe"
Name: "{autodesktop}\ModbusForge"; Filename: "{app}\ModbusForge.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\ModbusForge.exe"; Description: "{cm:LaunchProgram,ModbusForge}"; Flags: nowait postinstall skipifsilent
