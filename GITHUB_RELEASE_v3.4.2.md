# 🚀 GitHub Release Instructions for v3.4.2

## 📋 Step-by-Step Guide

### 1. Navigate to GitHub Releases
**URL**: https://github.com/nokkies/ModbusForge/releases/new

### 2. Fill Release Form
- **Tag version**: `v3.4.2`
- **Target branch**: `master`
- **Release title**: `ModbusForge v3.4.2`

### 3. Release Description (Copy This)
```
## v3.4.2 Features

### 🎯 Unit ID Dropdown for Server Mode
- **Unit ID Selection**: Added dropdown in server mode to select active Unit ID
- **Data Filtering**: Custom entries and register operations now filter by selected Unit ID
- **Auto-Refresh**: Custom entries automatically refresh when Unit ID selection changes
- **Smart Mode Detection**: Uses dropdown selection in server mode, global Unit ID in client mode

### 🔧 Improvements
- **Default Simulation**: Classic mode now defaults on startup (was Visual mode)
- **Clean Visual Editor**: Visual Node Editor starts empty without sample nodes
- **Publishing Port Support**: Enhanced server binding to all interfaces (0.0.0.0)

### 🧪 Testing
- **Unit Tests**: Added comprehensive tests for publishing port functionality
- **Multi-Client Support**: Verified multiple client connections to publishing port
- **Unit ID Parsing**: Tested range notation (e.g., "1,5,10-15,20")

### 🐛 Bug Fixes
- Fixed Unit ID dropdown not updating table data
- Resolved server binding to show actual interface IPs instead of 0.0.0.0

## Installation
Download the ZIP archive from the assets below.

## Usage
1. Start ModbusForge in **Server mode**
2. Configure Unit IDs (e.g., "1,2,3")
3. Connect server
4. Use **Active ID** dropdown to switch between Unit IDs
5. All Custom entries and register operations now respect the selected Unit ID
```

### 4. Upload Assets
- **File**: `ModbusForge-v3.4.2.zip`
- **Location**: `c:/Users/rvn/source/repos/ModbusForge/ModbusForge/ModbusForge-v3.4.2.zip`
- **Size**: 9.6 MB

### 5. Publish
Click **"Publish release"** button

## ✅ Pre-Release Verification

### Code Status
- [x] Version bumped to 3.4.2
- [x] Code committed (1898be8)
- [x] Release build completed
- [x] ZIP archive created

### Files Ready
- [x] `ModbusForge-v3.4.2.zip` (9,589,736 bytes)
- [x] Release notes prepared
- [x] Installation script ready

## 🎯 Post-Release Tasks
1. [ ] Verify ZIP downloads correctly
2. [ ] Test fresh installation
3. [ ] Update any documentation
4. [ ] Plan v3.5.0 for Unit ID isolation

## 🔗 Quick Links
- **Release Page**: https://github.com/nokkies/ModbusForge/releases/new
- **Repository**: https://github.com/nokkies/ModbusForge
- **Commit**: https://github.com/nokkies/ModbusForge/commit/1898be8

## 📋 Release Summary
- **Version**: 3.4.2
- **Features**: Unit ID dropdown, data filtering, auto-refresh
- **Improvements**: Default Classic mode, clean Visual editor
- **Tests**: Publishing port unit tests
- **Fixes**: Dropdown data updates, server binding display

---

**⚠️ Note**: For v3.5.0, see `UNIT_ID_ISOLATION_PLAN.md` for comprehensive Unit ID isolation and save structure redesign.
