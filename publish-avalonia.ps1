# Publishes ModbusForge.Avalonia as self-contained single-file executables
# for Windows x64 and Linux x64.

[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'linux-x64', 'all')]
    [string]$Runtime = 'all'
)

$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
$project = Join-Path (Join-Path $repoRoot 'ModbusForge.Avalonia') 'ModbusForge.Avalonia.csproj'
Write-Output "Starting publish from $repoRoot"

$profiles = @()
if ($Runtime -in @('win-x64', 'all')) { $profiles += 'win-x64' }
if ($Runtime -in @('linux-x64', 'all')) { $profiles += 'linux-x64' }

foreach ($profile in $profiles) {
    $profilePath = Join-Path (Join-Path (Join-Path 'ModbusForge.Avalonia' 'Properties') 'PublishProfiles') "$profile.pubxml"
    $fullProfile = Join-Path $repoRoot $profilePath

    if (-not (Test-Path $fullProfile)) {
        throw "Publish profile not found: $fullProfile"
    }

    Write-Output "Publishing ModbusForge.Avalonia for $profile..."
    dotnet publish $project -p:PublishProfile=$profile -c Release

    if ($LASTEXITCODE -ne 0) {
        throw "Publish failed for $profile"
    }

    $publishDir = Join-Path (Join-Path (Join-Path $repoRoot 'publish') 'avalonia') $profile
    Write-Output "Published to $publishDir"

    # Package the output
    $version = (Get-Content (Join-Path $repoRoot 'ModbusForge.Avalonia\ModbusForge.Avalonia.csproj') | Select-String '<Version>(.*)</Version>').Matches[0].Groups[1].Value
    $packageDir = Join-Path $repoRoot 'packages'
    New-Item -ItemType Directory -Path $packageDir -Force | Out-Null

    if ($profile -eq 'win-x64') {
        $zipPath = Join-Path $packageDir "ModbusForge-$version-win-x64-avalonia.zip"
        Compress-Archive -Path "$publishDir\*" -DestinationPath $zipPath -Force
        Write-Output "Created $zipPath"
    }
    elseif ($profile -eq 'linux-x64') {
        $tarName = "ModbusForge-$version-linux-x64-avalonia.tar.gz"
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
