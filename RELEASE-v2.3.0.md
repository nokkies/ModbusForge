# ModbusForge v2.3.0 Release Notes

**Release Date**: January 11, 2026

---

## 🎉 What's New in v2.3.0

### ✅ Package Compatibility Improvements
- **Removed PrivateAssets="all"** from OpenTK, OpenTK.GLWpfControl, and SkiaSharp.Views.WPF packages
- **Cleaner dependency management** - Better NuGet package resolution
- **Improved build performance** - Faster package restore and compilation

### ✅ Graceful Application Shutdown
- **Fixed application freeze** when closing with server running
- **2-second timeout** on disconnect during shutdown
- **Better error handling** during cleanup
- **Smooth exit** even with active connections

### ✅ Legacy Code Cleanup
- **Removed Options.cs** - Eliminated disabled code blocks and technical debt
- **XAML resource integration** - All options now properly defined in XAML
- **Cleaner codebase** - Removed duplicate definitions and architecture leftovers

---

## 🔧 Technical Improvements

### Package Updates
- NModbus4: 2.0.5516.31020 (standard reference)
- OpenTK: 3.3.1 (standard reference)
- OpenTK.GLWpfControl: 3.3.0 (standard reference)
- LiveCharts: 2.0.0-rc5.4 (stable for current use)

### Code Quality
- Improved dispose pattern with timeout handling
- Better async cleanup in MainViewModel
- Enhanced logging during shutdown

---

## 🐛 Bug Fixes

### Critical Fixes
- **Application Freeze on Close**: Fixed deadlock when closing application with server running
  - Added timeout mechanism to prevent indefinite waiting
  - Graceful fallback if disconnect takes too long
  - Proper error logging during cleanup

### Stability Improvements
- Server mode now works reliably on any available port
- Mode switching (Client ↔ Server) works correctly at runtime
- All register read/write operations function properly
- Simulation features work as expected

---

## 📊 Version Comparison

| Feature | v2.2.2 | v2.3.0 |
|---------|--------|--------|
| **Package Management** | Mixed PrivateAssets | ✅ Clean references |
| **Shutdown Behavior** | ❌ Freezes with server running | ✅ Graceful 2s timeout |
| **Legacy Code** | ❌ Options.cs with disabled blocks | ✅ Removed |
| **Code Quality** | Technical debt present | ✅ Cleaned up |
| **Server Functionality** | ✅ Working | ✅ Working |
| **Build Performance** | Standard | ✅ Improved |

---

## 🚀 Upgrade Instructions

### From v2.2.2 to v2.3.0

1. **Download** the latest release from GitHub
2. **Uninstall** previous version (optional)
3. **Install** v2.3.0
4. **Test** your existing configurations - all should work without changes

### Breaking Changes
- **None** - v2.3.0 is fully backward compatible with v2.2.2

---

## 🎯 What's Next?

### v2.4.0 - Coordinator Refactoring (Planned)
- Extract ConnectionCoordinator for connection logic
- Extract RegisterCoordinator for register operations
- Extract CustomEntryCoordinator for custom entry management
- Reduce MainViewModel complexity
- Improve code maintainability

---

## 📝 Known Issues

### Minor Issues
- Nullable reference warnings in build output (non-blocking)
- NU1701 warnings for legacy packages (suppressed)

### Workarounds
- All warnings are informational and do not affect functionality
- Application compiles and runs correctly

---

## 🙏 Acknowledgments

Thank you to all users who provided feedback and testing for this release!

---

## 📦 Download

**GitHub Release**: https://github.com/nokkies/ModbusForge/releases/tag/v2.3.0

### Installation Options
- **Windows Installer**: ModbusForge-2.3.0-setup.exe
- **Portable ZIP**: ModbusForge-2.3.0-win-x64.zip
- **Self-Contained**: ModbusForge-2.3.0-win-x64-sc.zip

---

## 📄 Full Changelog

**Commits**: https://github.com/nokkies/ModbusForge/compare/v2.2.2...v2.3.0

### Changes Summary
- 14 files changed
- 503 insertions
- 122 deletions
- Options.cs deleted
- Package references cleaned up
- Dispose pattern improved with timeout

---

**Enjoy ModbusForge v2.3.0!** 🎉
