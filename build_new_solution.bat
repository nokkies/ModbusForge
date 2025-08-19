@echo off
setlocal

echo Creating new solution file...
del /f /q ModbusForge.sln >nul 2>&1

echo Creating new solution...
dotnet new sln -n ModbusForge

if %ERRORLEVEL% NEQ 0 (
    echo Failed to create new solution
    exit /b %ERRORLEVEL%
)

echo Adding projects to solution...
dotnet sln add ModbusForge\ModbusForge.csproj

if %ERRORLEVEL% NEQ 0 (
    echo Failed to add project to solution
    exit /b %ERRORLEVEL%
)

echo Solution created successfully!
echo.
echo === Cleaning solution ===
dotnet clean

if %ERRORLEVEL% NEQ 0 (
    echo Clean failed with error %ERRORLEVEL%
    exit /b %ERRORLEVEL%
)

echo.
echo === Restoring packages ===
dotnet restore

if %ERRORLEVEL% NEQ 0 (
    echo Restore failed with error %ERRORLEVEL%
    exit /b %ERRORLEVEL%
)

echo.
echo === Building solution ===
dotnet build -v detailed

if %ERRORLEVEL% NEQ 0 (
    echo Build failed with error %ERRORLEVEL%
    exit /b %ERRORLEVEL%
)

echo.
echo === Build completed successfully! ===
echo.
pause
