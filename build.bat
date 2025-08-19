@echo off
echo === Cleaning solution ===
dotnet clean -v detailed
if %ERRORLEVEL% NEQ 0 (
    echo Error during clean
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo === Restoring packages ===
dotnet restore -v detailed
if %ERRORLEVEL% NEQ 0 (
    echo Error during restore
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo === Building solution ===
dotnet build -v detailed -p:GenerateFullPaths=true
if %ERRORLEVEL% NEQ 0 (
    echo Error during build
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo === Build succeeded! ===
echo.
echo === Starting application ===
dotnet run --project ModbusForge

pause
