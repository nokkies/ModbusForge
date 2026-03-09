# ModbusForge Build Automation Script
# Usage: .\build.ps1 -Task <Restore|Build|Publish|All> [-Configuration <Debug|Release>]

param (
    [Parameter(Mandatory = $false)]
    [ValidateSet("Restore", "Build", "Publish", "Installer", "All")]
    [string]$Task = "Build",

    [Parameter(Mandatory = $false)]
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Get-Location
$SolutionFile = Join-Path $ProjectRoot "ModbusForge.sln"
$ProjectFile = Join-Path $ProjectRoot "ModbusForge\ModbusForge.csproj"
$PublishDir = Join-Path $ProjectRoot "publish"
$Version = "3.0.3" # Current version from Inno Setup script

function Run-Restore {
    Write-Host "--- Restoring NuGet Packages ---" -ForegroundColor Cyan
    dotnet restore $SolutionFile
}

function Run-Build {
    Write-Host "--- Building Solution ($Configuration) ---" -ForegroundColor Cyan
    dotnet build $SolutionFile -c $Configuration
}

function Run-Publish {
    Write-Host "--- Publishing Application ---" -ForegroundColor Cyan
    
    # 1. Framework-dependent, single-file
    $OutDir1 = Join-Path $PublishDir "win-x64"
    Write-Host "Publishing framework-dependent to $OutDir1..." -ForegroundColor Gray
    dotnet publish $ProjectFile -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:PublishTrimmed=false -o $OutDir1

    # 2. Self-contained, single-file
    $OutDir2 = Join-Path $PublishDir "win-x64-sc"
    Write-Host "Publishing self-contained to $OutDir2..." -ForegroundColor Gray
    dotnet publish $ProjectFile -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -o $OutDir2
}

function Run-Installer {
    Write-Host "--- Building Inno Setup Installer ---" -ForegroundColor Cyan
    
    $IssFile = Join-Path $ProjectRoot "setup\ModbusForge.iss"
    if (-not (Test-Path $IssFile)) {
        Write-Error "Inno Setup script not found at $IssFile"
        return
    }

    # Common Inno Setup locations
    $IsccPaths = @(
        "C:\Program Files (x86)\Inno Setup 6\Iscc.exe",
        "C:\Program Files\Inno Setup 6\Iscc.exe",
        "C:\Program Files (x86)\Inno Setup 5\Iscc.exe"
    )

    $Iscc = $null
    foreach ($path in $IsccPaths) {
        if (Test-Path $path) { $Iscc = $path; break }
    }

    if ($null -eq $Iscc) {
        $Iscc = Get-Command iscc.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source
    }

    if ($null -eq $Iscc) {
        Write-Warning "Inno Setup Compiler (iscc.exe) not found. Please install Inno Setup or add it to PATH."
        return
    }

    Write-Host "Using Inno Setup Compiler: $Iscc" -ForegroundColor Gray
    & $Iscc "/DAppVersion=$Version" $IssFile
}

# Main Execution Logic
switch ($Task) {
    "Restore" { Run-Restore }
    "Build" { Run-Build }
    "Publish" { Run-Restore; Run-Publish }
    "Installer" { Run-Restore; Run-Publish; Run-Installer }
    "All" { Run-Restore; Run-Build; Run-Publish; Run-Installer }
    Default { Run-Build }
}

Write-Host "--- Task '$Task' Completed Successfully ---" -ForegroundColor Green
