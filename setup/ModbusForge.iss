; ModbusForge Inno Setup Script
; See https://jrsoftware.org/isinfo.php for documentation

#ifndef AppVersion
  #define AppVersion "5.0.0"
#endif

[Setup]
; Basic application info
AppName=ModbusForge
AppVersion={#AppVersion}
AppPublisher=ModbusForge
DefaultDirName={autopf}\ModbusForge
DefaultGroupName=ModbusForge
UninstallDisplayIcon={app}\ModbusForge.exe
CloseApplications=force
; SetupIconFile disabled temporarily (icon too large for Inno compiler)
; SetupIconFile=..\ModbusForge\Resources\ModbusForge.ico

; Setup output settings
WizardStyle=modern
OutputBaseFilename=ModbusForge-{#AppVersion}-setup
OutputDir=..\installers
Compression=lzma2
SolidCompression=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[InstallDelete]
; Remove stale WPF native runtime libraries from old installs. These are part of
; the shared Microsoft.WindowsDesktop.App runtime and should not be carried in
; the app folder, or a newer WPF managed assembly can load against an older native
; DLL and crash on startup (EntryPointNotFoundException in wpfgfx_cor3.dll).
Type: files; Name: "{app}\wpfgfx_cor3.dll"
Type: files; Name: "{app}\PresentationNative_cor3.dll"
Type: files; Name: "{app}\PenImc_cor3.dll"
Type: files; Name: "{app}\D3DCompiler_47_cor3.dll"
Type: files; Name: "{app}\vcruntime140_cor3.dll"

[Files]
; These files are expected to be in a 'publish' directory relative to the project root.
; The Inno Setup compiler should be run from the project root directory.
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\ModbusForge"; Filename: "{app}\ModbusForge.exe"
Name: "{autodesktop}\ModbusForge"; Filename: "{app}\ModbusForge.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\ModbusForge.exe"; Description: "{cm:LaunchProgram,ModbusForge}"; Flags: nowait postinstall skipifsilent
