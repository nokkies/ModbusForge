# 🚀 Publish ModbusForge v3.4.2

## 📋 Quick Release Steps

### 1. Create GitHub Release
1. **Go to**: https://github.com/nokkies/ModbusForge/releases/new
2. **Tag version**: `v3.4.2`
3. **Target branch**: `master`
4. **Release title**: `ModbusForge v3.4.2`
5. **Description**: Copy from `RELEASE-v3.4.2.md`
6. **Assets**: Upload `ModbusForge-v3.4.2.zip` (9.6 MB)
7. **Publish**: Click "Publish release"

### 2. Release Description (Copy This)
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

## ✅ Pre-Release Checklist

### Code & Build
- [x] Version bumped to 3.4.2 in ModbusForge.csproj
- [x] Code committed and pushed to GitHub
- [x] Release build completed successfully
- [x] All tests passing

### Assets Prepared
- [x] `ModbusForge-v3.4.2.zip` (9,589,736 bytes) - Main executable
- [x] `RELEASE-v3.4.2.md` - Release notes
- [x] `install.bat` - Installation script
- [x] Documentation updated

### Files Changed in This Release
- `MainViewModel.cs` - Unit ID dropdown logic
- `MainWindow.xaml` - Dropdown UI
- `ModbusServerService.cs` - Enhanced BoundEndpoint
- `ModbusForge.Tests/` - Publishing port tests
- `ModbusForge.csproj` - Version bump

## 🎯 Post-Release Tasks
1. [ ] Verify ZIP downloads correctly
2. [ ] Test installation from ZIP
3. [ ] Update website/documentation
4. [ ] Announce release

## 🔗 Important Links
- **GitHub Releases**: https://github.com/nokkies/ModbusForge/releases/new
- **Repository**: https://github.com/nokkies/ModbusForge
- **Issues**: https://github.com/nokkies/ModbusForge/issues
- **Commit**: https://github.com/nokkies/ModbusForge/commit/1898be8

## 📊 Release Statistics
- **Version**: 3.4.2
- **Commit**: 1898be8
- **Files changed**: 6
- **Lines added**: 327
- **Lines removed**: 32
- **ZIP size**: 9.6 MB

## 🚨 Known Issues
- None identified for this release

## 🔄 Next Release Planning
- Per-Unit ID checkbox states (addressed below)
- Unified save/load structure
- Import/Export Unit ID configurations
