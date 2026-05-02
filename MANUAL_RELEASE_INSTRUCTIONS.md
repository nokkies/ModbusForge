# Manual GitHub Release Instructions for ModbusForge v3.4.3

Since GitHub CLI is not available, follow these steps to create the release manually:

## 📋 Prerequisites
- GitHub account with push access to nokkies/ModbusForge repository
- GitHub Personal Access Token (optional, for automated script)

## 🚀 Method 1: Manual Web Interface (Recommended)

1. **Go to GitHub Releases**
   - Navigate to: https://github.com/nokkies/ModbusForge/releases/new

2. **Create New Release**
   - **Tag**: `v3.4.3`
   - **Target**: `fa092012eff352cd53ba29ca8f33ba2908cbd949`
   - **Release Title**: `ModbusForge v3.4.3 - Enhanced Save/Load with Auto-Filename`

3. **Add Release Notes**
   - Copy the contents from `RELEASE_NOTES_v3.4.3.md`
   - Paste into the release description field

4. **Publish Release**
   - Click "Publish release"
   - The release will be automatically tagged and published

## 🤖 Method 2: Automated Script

If you have a GitHub Personal Access Token:

1. **Run the Batch File**
   ```cmd
   create_release.bat
   ```

2. **Enter Token**
   - When prompted, enter your GitHub Personal Access Token
   - The script will attempt to create the release automatically

3. **Manual Fallback**
   - If the script fails, follow Method 1 above

## 🔧 GitHub Personal Access Token Setup

If you want to use the automated script:

1. **Create Token**
   - Go to: https://github.com/settings/tokens
   - Click "Generate new token (classic)"
   - Select scopes: `repo` (Full control of private repositories)

2. **Copy Token**
   - Copy the generated token (it won't be shown again)
   - Use this token when running the script

## ✅ Verification

After creating the release:

1. **Check Release Page**
   - Visit: https://github.com/nokkies/ModbusForge/releases
   - Verify v3.4.3 appears in the list

2. **Check Tag**
   - Visit: https://github.com/nokkies/ModbusForge/tags
   - Verify v3.4.3 tag exists

3. **Download Test**
   - Try downloading the release to ensure it works

## 📦 Release Contents

The release should include:
- All source code changes
- New features:
  - Auto-filename generation with IP/Unit ID placeholders
  - Enhanced save/load functionality
  - Complete Unit ID isolation
- Critical bug fixes for TwoWay binding crashes

## 🎯 Release Highlights

- **Auto-Filename**: `MBIP127000000001_ID1_20260310_125500.mfp`
- **Mode-Aware**: Different save/load for Client vs Server
- **Unit ID Export/Import**: Export Unit ID 1 as Unit ID X
- **Stability**: Fixed fatal startup crashes

## 🚀 Post-Release Tasks

1. **Update Documentation**
   - Update README if needed
   - Update any version references

2. **Community Notification**
   - Announce release in GitHub Discussions
   - Update any external documentation

3. **Monitor Issues**
   - Watch for any release-related issues
   - Respond to user feedback

---

**Release Information:**
- **Version**: v3.4.3
- **Commit**: fa092012eff352cd53ba29ca8f33ba2908cbd949
- **Date**: March 10, 2026
- **Repository**: nokkies/ModbusForge
