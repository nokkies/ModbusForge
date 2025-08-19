Write-Host "=== .NET SDK Check ===" -ForegroundColor Cyan

# Check if dotnet command is available
$dotnetPath = Get-Command dotnet -ErrorAction SilentlyContinue

if ($dotnetPath) {
    Write-Host "[✓] .NET SDK is installed" -ForegroundColor Green
    
    # Get .NET version
    $version = & dotnet --version
    Write-Host "[✓] .NET Version: $version" -ForegroundColor Green
    
    # List installed SDKs
    Write-Host "`n=== Installed SDKs ===" -ForegroundColor Cyan
    & dotnet --list-sdks
    
    # List installed runtimes
    Write-Host "`n=== Installed Runtimes ===" -ForegroundColor Cyan
    & dotnet --list-runtimes
    
    # Check for required .NET 8.0 SDK
    $sdkInstalled = & dotnet --list-sdks | Select-String "8.0"
    if ($sdkInstalled) {
        Write-Host "`n[✓] .NET 8.0 SDK is installed" -ForegroundColor Green
    } else {
        Write-Host "`n[!] .NET 8.0 SDK is NOT installed" -ForegroundColor Red
    }
} else {
    Write-Host "[!] .NET SDK is not installed or not in PATH" -ForegroundColor Red
}

# Check common installation paths
Write-Host "`n=== Installation Paths ===" -ForegroundColor Cyan
$paths = @(
    "C:\Program Files\dotnet\sdk",
    "C:\Program Files (x86)\dotnet\sdk",
    "$env:USERPROFILE\.dotnet\sdk"
)

foreach ($path in $paths) {
    if (Test-Path $path) {
        Write-Host "[✓] Found .NET SDK at: $path" -ForegroundColor Green
        Get-ChildItem -Path $path -Directory | Select-Object -ExpandProperty Name
    } else {
        Write-Host "[ ] Not found: $path" -ForegroundColor Gray
    }
}

# Check environment variables
Write-Host "`n=== Environment Variables ===" -ForegroundColor Cyan
$envVars = @("DOTNET_ROOT", "DOTNET_MULTILEVEL_LOOKUP", "PATH")

foreach ($var in $envVars) {
    $value = [Environment]::GetEnvironmentVariable($var)
    if ($value) {
        Write-Host "[✓] $var is set" -ForegroundColor Green
        if ($var -eq "PATH") {
            $value -split ';' | Where-Object { $_ -like "*dotnet*" } | ForEach-Object {
                Write-Host "    $_" -ForegroundColor DarkGray
            }
        } else {
            Write-Host "    $value" -ForegroundColor DarkGray
        }
    } else {
        Write-Host "[ ] $var is not set" -ForegroundColor Gray
    }
}

Write-Host "`n=== Recommendations ===" -ForegroundColor Cyan
if (-not $dotnetPath) {
    Write-Host "1. Install the .NET 8.0 SDK from: https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Yellow
    Write-Host "2. Restart your terminal/IDE after installation" -ForegroundColor Yellow
} elseif (-not $sdkInstalled) {
    Write-Host "1. Install the .NET 8.0 SDK from: https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Yellow
    Write-Host "2. Restart your terminal/IDE after installation" -ForegroundColor Yellow
} else {
    Write-Host "[✓] .NET 8.0 SDK is properly installed" -ForegroundColor Green
}

Write-Host "`nPress any key to exit..." -ForegroundColor Cyan
$null = $Host.UI.RawUI.ReadKey('NoEcho,IncludeKeyDown')
