# GitHub Release Creation Script for ModbusForge
# Note: This script requires a GitHub Personal Access Token with 'repo' scope

param(
    [Parameter(Mandatory=$true)]
    [string]$GitHubToken,

    [Parameter(Mandatory=$false)]
    [string]$RepoOwner = "nokkies",

    [Parameter(Mandatory=$false)]
    [string]$RepoName = "ModbusForge",

    [Parameter(Mandatory=$false)]
    [string]$Version = ""
)

$repoRoot = $PSScriptRoot
$csproj = Join-Path $repoRoot "ModbusForge.Avalonia\ModbusForge.Avalonia.csproj"
if (-not $Version) {
    $Version = ((Get-Content $csproj | Select-String '<Version>(.*)</Version>').Matches[0].Groups[1].Value)
}

$ReleaseTag = "v$Version"
$ReleaseName = "ModbusForge v$Version"
$CommitSha = (git rev-parse HEAD)
$ReleaseNotesPath = "RELEASE_NOTES_v$Version.md"

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
    "Authorization" = "token $GitHubToken"
    "Accept" = "application/vnd.github.v3+json"
    "Content-Type" = "application/json"
}

try {
    $Response = Invoke-RestMethod -Uri "https://api.github.com/repos/$RepoOwner/$RepoName/releases" `
                                -Method Post `
                                -Headers $Headers `
                                -Body $ReleasePayload

    Write-Host "Release created successfully!"
    Write-Host "Release URL: $($Response.html_url)"
    Write-Host "Tag: $($Response.tag_name)"

} catch {
    Write-Host "Error creating release:"
    Write-Host $_.Exception.Message
    Write-Host "Status Code: $($_.Exception.Response.StatusCode.value__)"
    Write-Host "Content: $($_.Exception.Response.Content)"

    Write-Host ""
    Write-Host "To create the release manually:"
    Write-Host "1. Go to: https://github.com/$RepoOwner/$RepoName/releases/new"
    Write-Host "2. Tag: $ReleaseTag"
    Write-Host "3. Target: $CommitSha"
    Write-Host "4. Title: $ReleaseName"
    Write-Host "5. Copy release notes from: $ReleaseNotesPath"
}

Write-Host ""
Write-Host "ModbusForge v$Version is ready for release!"
