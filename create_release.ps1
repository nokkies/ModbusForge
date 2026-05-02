# GitHub Release Creation Script for ModbusForge v3.4.3
# Note: This script requires a GitHub Personal Access Token with 'repo' scope

param(
    [Parameter(Mandatory=$true)]
    [string]$GitHubToken,
    
    [Parameter(Mandatory=$false)]
    [string]$RepoOwner = "nokkies",
    
    [Parameter(Mandatory=$false)]
    [string]$RepoName = "ModbusForge"
)

$ReleaseTag = "v3.4.3"
$ReleaseName = "ModbusForge v3.4.3 - Enhanced Save/Load with Auto-Filename"
$CommitSha = "fa092012eff352cd53ba29ca8f33ba2908cbd949"
$ReleaseNotesPath = "RELEASE_NOTES_v3.4.3.md"

# Read release notes
if (Test-Path $ReleaseNotesPath) {
    $ReleaseNotes = Get-Content $ReleaseNotesPath -Raw
} else {
    $ReleaseNotes = "Enhanced save/load functionality with auto-filename generation and complete Unit ID isolation."
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
    
    Write-Host "✅ Release created successfully!"
    Write-Host "Release URL: $($Response.html_url)"
    Write-Host "Tag: $($Response.tag_name)"
    
} catch {
    Write-Host "❌ Error creating release:"
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
Write-Host "🚀 ModbusForge v3.4.3 is ready for release!"
