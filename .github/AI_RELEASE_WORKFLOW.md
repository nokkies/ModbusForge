---
description: How to create a versioned release with Inno Setup installer on GitHub
---

# ModbusForge Release Workflow (AI Instructions)

## Overview

Releases are **fully automated via GitHub Actions**. Pushing a version tag to GitHub
triggers `.github/workflows/release.yml`, which:

1. Builds the application (Release, `win-x64`, single-file)
2. Publishes framework-dependent and self-contained executables
3. Compiles the Inno Setup installer (`setup/ModbusForge.iss`)
4. Packages ZIP archives + SHA256 checksums
5. Creates the GitHub Release and uploads all assets automatically

---

## Step-by-Step: How to Cut a Release

### 1. Bump the version in the csproj

File: `ModbusForge/ModbusForge.csproj`

```xml
<Version>X.Y.Z</Version>
<AssemblyVersion>X.Y.Z.0</AssemblyVersion>
<FileVersion>X.Y.Z.0</FileVersion>
```

**Do NOT** edit `setup/ModbusForge.iss` — the version is passed in by the
workflow via `/DAppVersion=X.Y.Z`.

### 2. Commit the version bump

```powershell
git add ModbusForge/ModbusForge.csproj
git commit -m "vX.Y.Z: <short description of changes>"
git push origin master
```

### 3. Create and push the version tag

```powershell
git tag vX.Y.Z
git push origin vX.Y.Z
```

Pushing the tag is the **single trigger** that starts the release workflow.  
The workflow runs on `windows-latest` and takes ~5–10 minutes.

### 4. Verify the release

- Actions progress: `https://github.com/nokkies/ModbusForge/actions`
- Completed release: `https://github.com/nokkies/ModbusForge/releases`

The release will contain:
- `ModbusForge-X.Y.Z-win-x64.zip` — framework-dependent build
- `ModbusForge-X.Y.Z-win-x64-sc.zip` — self-contained build
- `ModbusForge-X.Y.Z-win-x64.zip.sha256` / `-sc.zip.sha256`
- `ModbusForge-X.Y.Z-setup.exe` — Inno Setup installer

---

## Release Notes

The workflow auto-generates release notes from commits (`generate_release_notes: true`).

To provide custom release notes, create `RELEASE-vX.Y.Z.md` in the repo root
**before** tagging. The file is for human reference only — the workflow body
field shows the auto-generated notes from GitHub.

If you need to customise the GitHub Release body, edit
`.github/workflows/release.yml` around line 248:

```yaml
body: |
  Your custom release notes here.
```

---

## Key Files Reference

| File | Purpose |
|------|---------|
| `ModbusForge/ModbusForge.csproj` | Version source — bump `<Version>` here |
| `.github/workflows/release.yml` | Main release workflow (triggered by `v*` tag) |
| `.github/workflows/build-installer.yml` | CI build on every push to `main` |
| `setup/ModbusForge.iss` | Inno Setup script — reads from `publish/win-x64/` |
| `build.ps1` | Local build helper (optional, not used by CI) |
| `publish/win-x64/` | Framework-dependent publish output |
| `publish/win-x64-sc/` | Self-contained publish output |
| `installers/` | Inno Setup `.exe` output directory |

---

## Inno Setup Details

- Script: `setup/ModbusForge.iss`
- Version injected at compile time: `iscc /DAppVersion=X.Y.Z setup\ModbusForge.iss`
- Reads files from: `publish\win-x64\*`
- Outputs installer to: `installers\ModbusForge-X.Y.Z-setup.exe`
- Default install path: `%ProgramFiles%\ModbusForge`
- Creates Start Menu + optional desktop icon

---

## Troubleshooting

### Release workflow not triggered
- Verify the tag format is `v` followed by the version, e.g. `v3.4.2` not `3.4.2`
- Check: `git tag -l` to confirm local tag exists
- Check: `git push origin vX.Y.Z` output confirms the remote received it

### Installer not in release assets
- Check the Actions log for the **"Build Inno Setup Installer"** step
- The workflow downloads Inno Setup if `iscc` is not on PATH — if the
  Inno Setup download URL changes, update the `$innoUrl` in `release.yml` line ~127

### Wrong version in installer
- The version is passed via `/DAppVersion` — confirm `<Version>` in
  `ModbusForge.csproj` matches the tag you pushed

### Tag already exists locally but not on remote
```powershell
git push origin vX.Y.Z
```

### Need to retrigger the workflow (e.g. after a fix)
Delete and recreate the tag:
```powershell
git tag -d vX.Y.Z
git push origin :refs/tags/vX.Y.Z
git tag vX.Y.Z
git push origin vX.Y.Z
```

---

## Complete Example (v3.4.2)

```powershell
# 1. Bump version in ModbusForge.csproj to 3.4.2
# 2. Commit
git add ModbusForge/ModbusForge.csproj
git commit -m "v3.4.2: Add Unit ID dropdown for server mode"
git push origin master

# 3. Tag and push — this triggers the full release pipeline
git tag v3.4.2
git push origin v3.4.2

# 4. Monitor at:
#    https://github.com/nokkies/ModbusForge/actions
#    https://github.com/nokkies/ModbusForge/releases
```
