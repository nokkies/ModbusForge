---
description: How to create a versioned release with Inno Setup installer on GitHub
---

# ModbusForge Release Workflow (AI Instructions)

## Overview

Releases are **fully automated via GitHub Actions**. Pushing a version tag to GitHub
triggers `.github/workflows/release.yml`, which:

1. Builds the solution (Release)
2. Publishes the Avalonia self-contained single-file executable for `win-x64` and `linux-x64`
3. Compiles the Inno Setup installer (`setup/ModbusForge.iss`)
4. Packages ZIP / `.tar.gz` archives + SHA256 checksums
5. Creates the GitHub Release and uploads all assets automatically

---

## Step-by-Step: How to Cut a Release

### 1. Bump the version in the Avalonia csproj

File: `ModbusForge/ModbusForge.csproj`

```xml
<Version>YYYY.M.I</Version>
<AssemblyVersion>YYYY.M.I</AssemblyVersion>
<FileVersion>YYYY.M.I</FileVersion>
```

**Do NOT** edit `setup/ModbusForge.iss` — the version is passed in by the
workflow via `/DAppVersion=YYYY.M.I`.

### 2. Commit the version bump

```powershell
git add ModbusForge/ModbusForge.csproj
git commit -m "vYYYY.M.I: <short description of changes>"
git push origin master
```

### 3. Create and push the version tag

```powershell
git tag vYYYY.M.I
git push origin vYYYY.M.I
```

Pushing the tag is the **single trigger** that starts the release workflow.
The workflow runs on `windows-latest` and takes ~5–10 minutes.

### 4. Verify the release

- Actions progress: `https://github.com/nokkies/ModbusForge/actions`
- Completed release: `https://github.com/nokkies/ModbusForge/releases`

The release will contain:
- `ModbusForge-YYYY.M.I-win-x64.zip` — Windows self-contained build
- `ModbusForge-YYYY.M.I-win-x64.zip.sha256`
- `ModbusForge-YYYY.M.I-linux-x64.tar.gz` — Linux self-contained build
- `ModbusForge-YYYY.M.I-linux-x64.tar.gz.sha256`
- `ModbusForge-YYYY.M.I-setup.exe` — Inno Setup installer

---

## Release Notes

The workflow extracts release notes from the `README.md` `## What's New` section. If you want GitHub's auto-generated notes, set `generate_release_notes: true` in `.github/workflows/release.yml`.

To provide custom release notes, create `RELEASE-vYYYY.M.I.md` in the repo root
**before** tagging. The file is for human reference only — the workflow body
field shows the auto-generated notes from GitHub.

If you need to customise the GitHub Release body, edit
`.github/workflows/release.yml` around the `body:` field.

---

## Key Files Reference

| File | Purpose |
|------|---------|
| `ModbusForge/ModbusForge.csproj` | Version source — bump `<Version>` here |
| `.github/workflows/release.yml` | Main release workflow (triggered by `v*` tag) |
| `.github/workflows/avalonia.yml` | CI build/test for Avalonia |
| `.github/workflows/ci.yml` | CI build/test for the full solution |
| `setup/ModbusForge.iss` | Inno Setup script — reads from `publish/avalonia/win-x64/` |
| `build.ps1` | Local build helper (optional, not used by CI) |
| `publish/avalonia/win-x64/` | Windows self-contained publish output |
| `publish/avalonia/linux-x64/` | Linux self-contained publish output |
| `installers/` | Inno Setup `.exe` output directory |

---

## Inno Setup Details

- Script: `setup/ModbusForge.iss`
- Version injected at compile time: `iscc /DAppVersion=YYYY.M.I setup\ModbusForge.iss`
- Reads files from: `publish\avalonia\win-x64\*`
- Outputs installer to: `installers\ModbusForge-YYYY.M.I-setup.exe`
- Default install path: `%ProgramFiles%\ModbusForge`
- Creates Start Menu + optional desktop icon

---

## Troubleshooting

### Release workflow not triggered
- Verify the tag format is `v` followed by the version, e.g. `v2026.8.16`
- Check: `git tag -l` to confirm local tag exists
- Check: `git push origin vYYYY.M.I` output confirms the remote received it

### Installer not in release assets
- Check the Actions log for the **"Build Avalonia Inno Setup Installer"** step
- The workflow downloads Inno Setup if `iscc` is not on PATH — if the
  Inno Setup download URL changes, update the `$innoUrl` in `release.yml`

### Wrong version in installer
- The version is passed via `/DAppVersion` — confirm `<Version>` in
  `ModbusForge/ModbusForge.csproj` matches the tag you pushed

### Tag already exists locally but not on remote
```powershell
git push origin vYYYY.M.I
```

### Need to retrigger the workflow (e.g. after a fix)
Delete and recreate the tag:
```powershell
git tag -d vYYYY.M.I
git push origin :refs/tags/vYYYY.M.I
git tag vYYYY.M.I
git push origin vYYYY.M.I
```

---

## Complete Example (v2026.8.16)

```powershell
# 1. Bump version in ModbusForge/ModbusForge.csproj to 2026.8.16
# 2. Commit
git add ModbusForge/ModbusForge.csproj
git commit -m "v2026.8.16: Description of changes"
git push origin master

# 3. Tag and push — this triggers the full release pipeline
git tag v2026.8.16
git push origin v2026.8.16

# 4. Monitor at:
#    https://github.com/nokkies/ModbusForge/actions
#    https://github.com/nokkies/ModbusForge/releases
```
