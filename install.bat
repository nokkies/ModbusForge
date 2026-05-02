@echo off
echo ModbusForge v3.4.2 Installer
echo ============================
echo.

:: Create installation directory
set INSTALL_DIR=%ProgramFiles%\ModbusForge
echo Creating installation directory: %INSTALL_DIR%
if not exist "%INSTALL_DIR%" (
    mkdir "%INSTALL_DIR%"
)

:: Copy files
echo Copying ModbusForge files...
copy "ModbusForge.exe" "%INSTALL_DIR%\" /Y
copy "appsettings.json" "%INSTALL_DIR%\" /Y
copy "*.dll" "%INSTALL_DIR%\" /Y

:: Create desktop shortcut
echo Creating desktop shortcut...
powershell -Command "$WshShell = New-Object -comObject WScript.Shell; $Shortcut = $WshShell.CreateShortcut('%USERPROFILE%\Desktop\ModbusForge.lnk'); $Shortcut.TargetPath = '%INSTALL_DIR%\ModbusForge.exe'; $Shortcut.Save()"

:: Create Start Menu shortcut
echo Creating Start Menu shortcut...
if not exist "%ProgramData%\Microsoft\Windows\Start Menu\Programs\ModbusForge" (
    mkdir "%ProgramData%\Microsoft\Windows\Start Menu\Programs\ModbusForge"
)
powershell -Command "$WshShell = New-Object -comObject WScript.Shell; $Shortcut = $WshShell.CreateShortcut('%ProgramData%\Microsoft\Windows\Start Menu\Programs\ModbusForge\ModbusForge.lnk'); $Shortcut.TargetPath = '%INSTALL_DIR%\ModbusForge.exe'; $Shortcut.Save()"

echo.
echo Installation completed successfully!
echo.
echo ModbusForge has been installed to: %INSTALL_DIR%
echo You can launch it from:
echo - Desktop shortcut
echo - Start Menu > ModbusForge
echo - Directly from: %INSTALL_DIR%\ModbusForge.exe
echo.
pause
