# Check .NET installation in registry
Write-Host "=== .NET Installation Check via Registry ===" -ForegroundColor Cyan

# Check for .NET Framework
$netFrameworkPath = "HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP"
if (Test-Path $netFrameworkPath) {
    Write-Host "[✓] .NET Framework installation found in registry" -ForegroundColor Green
    Get-ChildItem -Path $netFrameworkPath -Recurse -ErrorAction SilentlyContinue | 
        Where-Object { $_.PSChildName -match '^v[0-9]' -or $_.PSChildName -eq 'Full' } |
        ForEach-Object {
            $version = $_.GetValue("Version", "")
            $release = $_.GetValue("Release", "")
            Write-Host "  - $($_.PSPath.Split('\')[-1]): $version (Release: $release)" -ForegroundColor DarkGray
        }
} else {
    Write-Host "[ ] No .NET Framework installation found in registry" -ForegroundColor Gray
}

# Check for .NET Core/5.0+
$netCorePath = "HKLM:\SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedhost"
if (Test-Path $netCorePath) {
    Write-Host "`n[✓] .NET Core/5.0+ installation found in registry" -ForegroundColor Green
    $sharedHostVersion = (Get-ItemProperty -Path $netCorePath -Name "Version" -ErrorAction SilentlyContinue).Version
    Write-Host "  - Shared Host Version: $sharedHostVersion" -ForegroundColor DarkGray
    
    # Check SDK installation
    $sdkPath = "HKLM:\SOFTWARE\dotnet\Setup\InstalledVersions\x64\sdk"
    if (Test-Path $sdkPath) {
        Write-Host "  - SDK Versions:" -ForegroundColor DarkGray
        Get-ItemProperty -Path "$sdkPath\*" | 
            Where-Object { $_.PSChildName -match '^[0-9]' } |
            ForEach-Object {
                $version = $_.PSChildName
                $installPath = $_.InstallLocation
                Write-Host "    - $version" -ForegroundColor DarkGray
                if ($installPath) {
                    Write-Host "      Path: $installPath" -ForegroundColor DarkGray
                }
            }
    } else {
        Write-Host "  [ ] No SDK versions found in registry" -ForegroundColor Gray
    }
} else {
    Write-Host "`n[ ] No .NET Core/5.0+ installation found in registry" -ForegroundColor Gray
}

# Check for .NET 8.0 specifically
$net8Path = "HKLM:\SOFTWARE\dotnet\Setup\InstalledVersions\x64\sdk\8."
$net8Installed = Get-ChildItem -Path "HKLM:\SOFTWARE\dotnet\Setup\InstalledVersions\x64\sdk" -ErrorAction SilentlyContinue | 
    Where-Object { $_.PSChildName -like "8.*" }

if ($net8Installed) {
    Write-Host "`n[✓] .NET 8.0 SDK is installed" -ForegroundColor Green
    $net8Installed | ForEach-Object {
        $version = $_.PSChildName
        $installPath = (Get-ItemProperty -Path $_.PSPath -Name "InstallLocation" -ErrorAction SilentlyContinue).InstallLocation
        Write-Host "  - Version: $version" -ForegroundColor DarkGray
        if ($installPath) {
            Write-Host "    Path: $installPath" -ForegroundColor DarkGray
        }
    }
} else {
    Write-Host "`n[!] .NET 8.0 SDK is NOT installed" -ForegroundColor Red
    Write-Host "    Download from: https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Yellow
}

Write-Host "`n=== Recommendations ===" -ForegroundColor Cyan
if (-not $net8Installed) {
    Write-Host "1. Install the .NET 8.0 SDK from: https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Yellow
    Write-Host "2. Restart your computer after installation" -ForegroundColor Yellow
    Write-Host "3. Open a new terminal/command prompt and verify with 'dotnet --version'" -ForegroundColor Yellow
} else {
    Write-Host "[✓] .NET 8.0 SDK appears to be installed but may not be in PATH" -ForegroundColor Green
    Write-Host "1. Try restarting your computer" -ForegroundColor Yellow
    Write-Host "2. Open a new terminal/command prompt and verify with 'dotnet --version'" -ForegroundColor Yellow
    Write-Host "3. If still not working, repair the .NET 8.0 SDK installation" -ForegroundColor Yellow
}

Write-Host "`nPress any key to exit..." -ForegroundColor Cyan
$null = $Host.UI.RawUI.ReadKey('NoEcho,IncludeKeyDown')
