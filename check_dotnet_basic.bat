@echo off
echo Checking for .NET installation...
echo.

echo [1] Checking if dotnet command is available:
where dotnet >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    echo   [SUCCESS] dotnet command is available
    echo.
    echo [2] Checking .NET version:
    dotnet --version
    echo.
    echo [3] Listing installed SDKs:
    dotnet --list-sdks
    echo.
    echo [4] Listing installed runtimes:
    dotnet --list-runtimes
) else (
    echo   [ERROR] dotnet command is not available in PATH
    echo.
    echo Please install the .NET 8.0 SDK from:
    echo https://dotnet.microsoft.com/download/dotnet/8.0
    echo.
    echo After installation, restart your computer and try again.
)

echo.
pause
