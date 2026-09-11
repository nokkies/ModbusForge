---
description: How to create a versioned release with Inno Setup installer on GitHub
---

# ModbusForge Release Workflow (AI Instructions)

## Overview

Releases are **fully automated via GitHub Actions**. Pushing a version tag to GitHub
triggers `.github/workflows/release.yml`, which:

1. Computes the version from the tag and **verifies it matches the `<Version>`
   element in all three csproj files** (`ModbusForge`, `ModbusForge.Core`,
   `ModbusForge.Headless`) — a mismatch fails the release
2. Builds the solution (Release)
3. Runs the test suites (core, headless, Avalonia; UI/FlaUI smoke tests excluded)
4. Publishes self-contained single-file executables: Avalonia (`win-x64`,
   `linux-x64`) and Headless (`win-x64`, `linux-x64`)
5. Optionally code-signs the Windows executables and installer (when the
   `CODESIGN_CERTIFICATE_*` secrets are set)
6. Compiles the Inno Setup installer (`setup/ModbusForge.iss`, Inno Setup 6.4.1
   via `choco install innosetup`)
7. Packages ZIP / `.tar.gz` archives + SHA256 checksums
8. Creates the GitHub Release and uploads all assets automatically

The job has a 90-minute timeout and a per-ref concurrency group, so a stuck or
duplicate release run cannot occupy the release lane.

---

## Step-by-Step: How to Cut a Release

### 1. Bump the version in ALL THREE csproj files

Files: `ModbusForge/ModbusForge.csproj`, `ModbusForge.Core/ModbusForge.Core.csproj`,
`ModbusForge.Headless/ModbusForge.Headless.csproj`

```xml
<Version>YYYY.M.I</Version>
<AssemblyVersion>YYYY.M.I</AssemblyVersion>
<FileVersion>YYYY.M.I</FileVersion>
```

All three must agree with each other **and with the tag**, otherwise the release
workflow fails its version-agreement gate. Version format is CalVer
`YYYY.M.INCREMENT` (no leading zero on the month).

**Do NOT** edit `setup/ModbusForge.iss` — the version is passed in by the
workflow via `/DAppVersion=YYYY.M.I`.

### 2. Commit the version bump

```powershell
git add ModbusForge/ModbusForge.csproj ModbusForge.Core/ModbusForge.Core.csproj ModbusForge.Headless/ModbusForge.Headless.csproj
git commit -m "vYYYY.M.I: <short description of changes>"
git push origin master
```

### 3. Create and push the version tag

```powershell
git tag vYYYY.M.I
git push origin vYYYY.M.I
```

Pushing the tag is the **single trigger** that starts the release workflow.
The workflow runs on `windows-latest` and takes ~10–20 minutes.

### 4. Verify the release

- Actions progress: `https://github.com/nokkies/ModbusForge/actions`
- Completed release: `https://github.com/nokkies/ModbusForge/releases`

The release will contain:
- `ModbusForge-YYYY.M.I-win-x64.zip` — Windows self-contained build
- `ModbusForge-YYYY.M.I-win-x64.zip.sha256`
- `ModbusForge-YYYY.M.I-linux-x64.tar.gz` — Linux self-contained build
- `ModbusForge-YYYY.M.I-linux-x64.tar.gz.sha256`
- `ModbusForge-YYYY.M.I-headless-win-x64.zip` — Windows headless build
- `ModbusForge-YYYY.M.I-headless-win-x64.zip.sha256`
- `ModbusForge-YYYY.M.I-headless-linux-x64.tar.gz` — Linux headless build
- `ModbusForge-YYYY.M.I-headless-linux-x64.tar.gz.sha256`
- `ModbusForge-YYYY.M.I-setup.exe` — Inno Setup installer
- `ModbusForge-YYYY.M.I-setup.exe.sha256`

---

## Release Notes

The workflow does **not** use GitHub's auto-generated notes
(`generate_release_notes: false`). It reads the `## What's New` section of
`README.md` and uses the first `### <version>` subsection under it as the
release body (falling back to a link to the README changelog if the section is
missing). So **update the README changelog before tagging**.

---

## Key Files Reference

| File | Purpose |
|------|---------|
| `ModbusForge/ModbusForge.csproj` | Version source (1 of 3) — bump `<Version>` here |
| `ModbusForge.Core/ModbusForge.Core.csproj` | Version source (2 of 3) — must match |
| `ModbusForge.Headless/ModbusForge.Headless.csproj` | Version source (3 of 3) — must match |
| `.github/workflows/release.yml` | Main release workflow (triggered by `v*` tag) |
| `.github/workflows/avalonia.yml` | CI build/test matrix (windows + ubuntu; core/headless/avalonia suites) |
| `.github/workflows/ci.yml` | CI build/test for the full solution (windows) |
| `setup/ModbusForge.iss` | Inno Setup script — reads from `publish/avalonia/win-x64/` |
| `build.ps1` | Local build helper (optional, not used by CI); asserts all three versions agree |
| `create_release.ps1` | Manual/emergency GitHub Release creation (token via `GITHUB_TOKEN` env var) |
| `publish/avalonia/{win,linux}-x64/` | Avalonia self-contained publish output |
| `publish/headless/{win,linux}-x64/` | Headless self-contained publish output |
| `installers/` | Inno Setup `.exe` output directory |

---

## Inno Setup Details

- Script: `setup/ModbusForge.iss`
- Version injected at compile time: `iscc /DAppVersion=YYYY.M.I setup\ModbusForge.iss`
- Reads files from: `publish\avalonia\win-x64\*`
- Outputs installer to: `installers\ModbusForge-YYYY.M.I-setup.exe`
- Default install path: `%ProgramFiles%\ModbusForge`
- Creates Start Menu + optional desktop icon
- Requires Windows 10 or later (`MinVersion=10.0.0.0`)

---

## Troubleshooting

### Release workflow not triggered
- Verify the tag format is `v` followed by the version, e.g. `v2026.8.16`
- Check: `git tag -l` to confirm local tag exists
- Check: `git push origin vYYYY.M.I` output confirms the remote received it
- The workflow ignores tag pushes made by `github-actions[bot]`

### Version mismatch failure
- The gate compares the tag version with `<Version>` in all three csproj files
- Fix: bump all three csproj files to the same version, commit, delete and
  re-push the tag (see below)

### Installer not in release assets
- Check the Actions log for the **"Build Inno Setup Installer"** step
- Inno Setup is installed via `choco install innosetup -y --version 6.4.1` —
  if the pinned version is unavailable on the runner, the step fails loudly

### Wrong version in installer
- The version is passed via `/DAppVersion` — the version-agreement gate now
  fails the release if any csproj disagrees with the tag

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

## Complete Example (v2026.8.27)

```powershell
# 1. Bump version in all three csproj files to 2026.8.27
#    (ModbusForge, ModbusForge.Core, ModbusForge.Headless)
# 2. Update the README "What's New" changelog section
# 3. Commit
git add -A
git commit -m "v2026.8.27: Description of changes"
git push origin master

# 4. Tag and push — this triggers the full release pipeline
git tag v2026.8.27
git push origin v2026.8.27

# 5. Monitor at:
#    https://github.com/nokkies/ModbusForge/actions
#    https://github.com/nokkies/ModbusForge/releases
```
