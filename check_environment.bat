@echo off
echo Checking .NET SDK installation...
echo.
echo [1] Checking .NET version
where dotnet
echo.

if errorlevel 1 (
    echo .NET SDK not found in PATH
) else (
    echo .NET SDK found
    dotnet --version
    echo.
    echo [2] Checking installed SDKs
    dotnet --list-sdks
    echo.
    echo [3] Checking installed runtimes
    dotnet --list-runtimes
)

echo.
echo [4] Checking environment variables
echo PATH=%PATH%
echo.

echo [5] Checking common .NET installation locations
echo.
if exist "C:\Program Files\dotnet" (
    echo .NET installation found in C:\Program Files\dotnet
    dir "C:\Program Files\dotnet\sdk" /b
) else (
    echo .NET not found in C:\Program Files\dotnet
)

echo.
if exist "C:\Program Files (x86)\dotnet" (
    echo .NET installation found in C:\Program Files (x86)\dotnet
    dir "C:\Program Files (x86)\dotnet\sdk" /b
) else (
    echo .NET not found in C:\Program Files (x86)\dotnet
)

echo.
pause
