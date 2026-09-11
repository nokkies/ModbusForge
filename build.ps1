# ModbusForge Build Automation Script
# Usage: .\build.ps1 -Task <Restore|Build|Publish|Installer|All> [-Configuration <Debug|Release>]

param (
    [Parameter(Mandatory = $false)]
    [ValidateSet("Restore", "Build", "Publish", "Installer", "All")]
    [string]$Task = "Build",

    [Parameter(Mandatory = $false)]
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

# Anchor at the script location so the script works no matter where it is invoked from.
$ProjectRoot = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
$SolutionFile = Join-Path $ProjectRoot "ModbusForge.sln"
$ProjectFile = Join-Path $ProjectRoot "ModbusForge\ModbusForge.csproj"
$PublishDir = Join-Path $ProjectRoot "publish"

# Read the version from ALL three policy-tracked csproj files and assert they
# agree; a silent drift would otherwise let the installer take the app's
# version while the other assemblies ship a different one.
$csprojFiles = @(
    $ProjectFile,
    (Join-Path $ProjectRoot "ModbusForge.Core\ModbusForge.Core.csproj"),
    (Join-Path $ProjectRoot "ModbusForge.Headless\ModbusForge.Headless.csproj")
)

$versionByProject = @{}
foreach ($csproj in $csprojFiles) {
    $match = Select-String -Path $csproj -Pattern '<Version>([^<]+)</Version>' | Select-Object -First 1
    if (-not $match) {
        throw "No <Version> element found in $csproj"
    }
    $versionByProject[$csproj] = $match.Matches[0].Groups[1].Value
}

$Version = $versionByProject[$csprojFiles[0]]
foreach ($csproj in $csprojFiles) {
    if ($versionByProject[$csproj] -ne $Version) {
        throw "Version mismatch: $(Split-Path $csproj -Leaf) is '$($versionByProject[$csproj])' but $ProjectFile is '$Version'. Update all three csproj files to the same version."
    }
}

Write-Host "Version: $Version (all three csproj files agree)" -ForegroundColor Gray

function Run-Restore {
    Write-Host "--- Restoring NuGet Packages ---" -ForegroundColor Cyan
    dotnet restore $SolutionFile
}

function Run-Build {
    Write-Host "--- Building Solution ($Configuration) ---" -ForegroundColor Cyan
    dotnet build $SolutionFile -c $Configuration
}

function Run-Publish {
    Write-Host "--- Publishing Avalonia Application ---" -ForegroundColor Cyan

    # Self-contained, single-file for the Windows installer
    $OutDir = Join-Path $PublishDir "avalonia\win-x64"
    Write-Host "Publishing self-contained single-file to $OutDir..." -ForegroundColor Gray
    dotnet publish $ProjectFile -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -o $OutDir
}

function Run-Installer {
    Write-Host "--- Building Inno Setup Installer ---" -ForegroundColor Cyan

    $IssFile = Join-Path $ProjectRoot "setup\ModbusForge.iss"
    if (-not (Test-Path $IssFile)) {
        throw "Inno Setup script not found at $IssFile"
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
        # The installer task must never report success without producing an installer.
        throw "Inno Setup Compiler (iscc.exe) not found. Please install Inno Setup or add it to PATH."
    }

    Write-Host "Using Inno Setup Compiler: $Iscc" -ForegroundColor Gray
    & $Iscc "/DAppVersion=$Version" $IssFile
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup compilation failed (exit code $LASTEXITCODE)."
    }
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
