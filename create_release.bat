@echo off
echo Creating GitHub Release for ModbusForge v3.4.3
echo.
echo This script requires a GitHub Personal Access Token with 'repo' scope.
echo.
if "%GITHUB_TOKEN%"=="" (
    echo.
    echo GitHub token not found in environment variable.
    echo Please set it first:
    echo   set GITHUB_TOKEN=your_token_here
    echo Or set it permanently:
    echo   setx GITHUB_TOKEN "your_token_here"
    echo.
    pause
    exit /b 1
)

echo Using GitHub token from environment variable...

echo.
echo Creating release...
powershell -ExecutionPolicy Bypass -File create_release.ps1 -GitHubToken "%GITHUB_TOKEN%"

echo.
echo If the automatic creation failed, you can create the release manually:
echo 1. Go to: https://github.com/nokkies/ModbusForge/releases/new
echo 2. Tag: v3.4.3
echo 3. Target: fa092012eff352cd53ba29ca8f33ba2908cbd949
echo 4. Title: ModbusForge v3.4.3 - Enhanced Save/Load with Auto-Filename
echo 5. Copy release notes from: RELEASE_NOTES_v3.4.3.md
echo.
pause
