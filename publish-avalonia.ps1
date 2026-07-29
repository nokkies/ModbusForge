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
    dotnet publish $project -p:PublishProfile=$profilePath -c Release

    if ($LASTEXITCODE -ne 0) {
        throw "Publish failed for $profile"
    }

    $publishDir = Join-Path (Join-Path (Join-Path $repoRoot 'publish') 'avalonia') $profile
    Write-Output "Published to $publishDir"
}

Write-Output 'Done.'
