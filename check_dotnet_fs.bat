@echo off
setlocal enabledelayedexpansion

echo Checking for .NET installation...
echo.

echo [1] Checking common .NET installation directories:
set "dotnet_found=0"

for %%d in (
    "C:\Program Files\dotnet\dotnet.exe"
    "C:\Program Files (x86)\dotnet\dotnet.exe"
    "%USERPROFILE%\.dotnet\dotnet.exe"
) do (
    if exist "%%~d" (
        echo Found: %%~d
        set "dotnet_found=1"
        "%%~d" --version
        echo.
        echo [2] Installed SDKs:
        "%%~d" --list-sdks
        echo.
        echo [3] Installed Runtimes:
        "%%~d" --list-runtimes
        goto :end_check
    )
)

if "!dotnet_found!"=="0" (
    echo No .NET installation found in common locations.
    echo.
    echo Please install the .NET 8.0 SDK from:
    echo https://dotnet.microsoft.com/download/dotnet/8.0
    echo.
    echo After installation, restart your computer and try again.
)

:end_check
echo.
pause
