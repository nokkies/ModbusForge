@echo off
echo === Environment Information ===
where dotnet
echo.

echo === .NET Version ===
dotnet --version
echo.

echo === Installed SDKs ===
dotnet --list-sdks
echo.

echo === Cleaning Solution ===
dotnet clean -v detailed > clean.log 2>&1
type clean.log
del clean.log
echo.

echo === Restoring Packages ===
dotnet restore -v detailed > restore.log 2>&1
type restore.log
del restore.log
echo.

echo === Building Solution ===
dotnet build -v detailed -p:GenerateFullPaths=true > build.log 2>&1
type build.log
del build.log
echo.

if %ERRORLEVEL% EQU 0 (
    echo === Build Succeeded ===
    echo.
    echo === Running Application ===
    dotnet run --project ModbusForge
) else (
    echo === Build Failed with error code %ERRORLEVEL% ===
)

echo.
pause
