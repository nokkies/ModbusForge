# Publishes ModbusForge as self-contained single-file executables
# for Windows x64 and Linux x64.

[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'linux-x64', 'all')]
    [string]$Runtime = 'all'
)

$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
$project = Join-Path (Join-Path $repoRoot 'ModbusForge') 'ModbusForge.csproj'
Write-Output "Starting publish from $repoRoot"

$version = ((Get-Content $project | Select-String '<Version>(.*)</Version>').Matches[0].Groups[1].Value)
$packageDir = Join-Path $repoRoot 'packages'
New-Item -ItemType Directory -Path $packageDir -Force | Out-Null

$runtimes = @()
if ($Runtime -in @('win-x64', 'all')) { $runtimes += 'win-x64' }
if ($Runtime -in @('linux-x64', 'all')) { $runtimes += 'linux-x64' }

foreach ($rt in $runtimes) {
    $publishDir = Join-Path (Join-Path (Join-Path $repoRoot 'publish') 'avalonia') $rt

    Write-Output "Publishing ModbusForge for $rt..."
    dotnet publish $project -c Release -r $rt `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:PublishTrimmed=false `
        -p:Version=$version `
        -o $publishDir

    if ($LASTEXITCODE -ne 0) {
        throw "Publish failed for $rt"
    }

    Write-Output "Published to $publishDir"

    # Strip debug symbols from the published package; single-file bundles don't need them.
    Get-ChildItem -Path $publishDir -Filter '*.pdb' -Recurse | Remove-Item -Force -ErrorAction SilentlyContinue

    if ($rt -eq 'win-x64') {
        $zipPath = Join-Path $packageDir "ModbusForge-$version-win-x64.zip"
        Compress-Archive -Path "$publishDir\*" -DestinationPath $zipPath -Force
        Write-Output "Created $zipPath"

        $iscc = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
        $issPath = Join-Path $repoRoot 'setup\ModbusForge.iss'

        if (Test-Path $iscc) {
            Write-Output "Building Windows installer with Inno Setup..."
            & $iscc /dAppVersion=$version $issPath

            if ($LASTEXITCODE -ne 0) {
                throw "Inno Setup compiler failed"
            }

            $installerSource = Join-Path $repoRoot "installers\ModbusForge-$version-setup.exe"
            $installerDest = Join-Path $packageDir "ModbusForge-$version-setup.exe"

            if (Test-Path $installerSource) {
                Copy-Item -Path $installerSource -Destination $installerDest -Force
                Write-Output "Created $installerDest"
            }
        }
        else {
            Write-Warning "Inno Setup compiler not found at $iscc; skipping installer build"
        }
    }
    elseif ($rt -eq 'linux-x64') {
        $tarName = "ModbusForge-$version-linux-x64.tar.gz"
        $tarPath = Join-Path $packageDir $tarName
        $stagingName = 'ModbusForge'
        $staging = Join-Path $packageDir $stagingName
        if (Test-Path $staging) { Remove-Item -Path $staging -Recurse -Force }
        Copy-Item -Path $publishDir -Destination $staging -Recurse

        if (-not (Get-Command tar -ErrorAction SilentlyContinue)) {
            throw "tar was not found; cannot create Linux .tar.gz package"
        }

        tar -czf $tarPath -C $packageDir $stagingName

        Remove-Item -Path $staging -Recurse -Force
        Write-Output "Created $tarPath"
    }
}

Write-Output 'Done.'
