# GitHub Release Creation Script for ModbusForge
#
# The token is read from the GITHUB_TOKEN environment variable so it never
# appears on a command line (where other processes and ETW would see it):
#     $env:GITHUB_TOKEN = 'ghp_...' ; .\create_release.ps1
#
# Note: for a normal release you only need to push the v* tag - the
# release.yml workflow builds, signs, packages and publishes. This script is
# for the manual/emergency path.

param(
    [Parameter(Mandatory=$false)]
    [string]$RepoOwner = "nokkies",

    [Parameter(Mandatory=$false)]
    [string]$RepoName = "ModbusForge",

    [Parameter(Mandatory=$false)]
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$repoRoot = $PSScriptRoot
if (-not $repoRoot) { $repoRoot = (Get-Location).Path }

# The three policy-tracked csproj files must agree on the version (same rule as
# the CI release gate).
$csprojFiles = @(
    (Join-Path $repoRoot "ModbusForge\ModbusForge.csproj"),
    (Join-Path $repoRoot "ModbusForge.Core\ModbusForge.Core.csproj"),
    (Join-Path $repoRoot "ModbusForge.Headless\ModbusForge.Headless.csproj")
)

function Read-CsprojVersion([string]$path) {
    $match = Select-String -Path $path -Pattern '<Version>([^<]+)</Version>' | Select-Object -First 1
    if (-not $match) { throw "No <Version> element found in $path" }
    return $match.Matches[0].Groups[1].Value
}

$projectVersions = @{}
foreach ($csproj in $csprojFiles) {
    $projectVersions[$csproj] = Read-CsprojVersion $csproj
}

if (-not $Version) {
    $Version = $projectVersions[$csprojFiles[0]]
}

foreach ($csproj in $csprojFiles) {
    if ($projectVersions[$csproj] -ne $Version) {
        throw "Version mismatch: $(Split-Path $csproj -Leaf) is '$($projectVersions[$csproj])' but the release version is '$Version'."
    }
}

if (-not $env:GITHUB_TOKEN) {
    throw "GITHUB_TOKEN environment variable is not set. Export a token with 'repo' scope first."
}

$ReleaseTag = "v$Version"
$ReleaseName = "ModbusForge v$Version"
$CommitSha = (git -C $repoRoot rev-parse HEAD)
$ReleaseNotesPath = Join-Path $repoRoot "RELEASE_NOTES_v$Version.md"

# Read release notes
if (Test-Path $ReleaseNotesPath) {
    $ReleaseNotes = Get-Content $ReleaseNotesPath -Raw
} else {
    $ReleaseNotes = "Avalonia release v$Version. See README.md changelog for details."
}

# Create release payload
$ReleasePayload = @{
    tag_name = $ReleaseTag
    target_commitish = $CommitSha
    name = $ReleaseName
    body = $ReleaseNotes
    draft = $false
    prerelease = $false
} | ConvertTo-Json -Depth 10

Write-Host "Creating GitHub release for $ReleaseName..."
Write-Host "Repository: $RepoOwner/$RepoName"
Write-Host "Tag: $ReleaseTag"
Write-Host "Commit: $CommitSha"

# Create the release using GitHub API
$Headers = @{
    "Authorization" = "token $($env:GITHUB_TOKEN)"
    "Accept" = "application/vnd.github.v3+json"
    "Content-Type" = "application/json"
}

try {
    $Response = Invoke-RestMethod -Uri "https://api.github.com/repos/$RepoOwner/$RepoName/releases" `
                                -Method Post `
                                -Headers $Headers `
                                -Body $ReleasePayload
}
catch {
    $statusCode = "n/a"
    $content = $null
    if ($_.Exception.Response) {
        $statusCode = $_.Exception.Response.StatusCode.value__
        $reader = [System.IO.StreamReader]::new($_.Exception.Response.GetResponseStream())
        $content = $reader.ReadToEnd()
    }

    Write-Host "Error creating release:"
    Write-Host $_.Exception.Message
    Write-Host "Status Code: $statusCode"
    if ($content) { Write-Host "Content: $content" }

    Write-Host ""
    Write-Host "To create the release manually:"
    Write-Host "1. Go to: https://github.com/$RepoOwner/$RepoName/releases/new"
    Write-Host "2. Tag: $ReleaseTag"
    Write-Host "3. Target: $CommitSha"
    Write-Host "4. Title: $ReleaseName"
    if (Test-Path $ReleaseNotesPath) { Write-Host "5. Copy release notes from: $ReleaseNotesPath" }
    exit 1
}

Write-Host "Release created successfully!"
Write-Host "Release URL: $($Response.html_url)"
Write-Host "Tag: $($Response.tag_name)"
Write-Host ""
Write-Host "ModbusForge v$Version is ready for release!"
