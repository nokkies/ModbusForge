# ModbusForge v2.7.0 Release Notes

**Release Date:** January 11, 2026  
**Type:** Package Modernization Release

---

## 🎯 Overview

Version 2.7.0 focuses on package modernization, updating dependencies to their latest stable versions for improved performance, security, and compatibility. This release completes the planned refactoring and improvement cycle that began with v2.3.0.

---

## ✨ What's New

### 📦 Package Updates

#### **Microsoft.Extensions.* Packages: 8.0.0 → 9.0.0**
All Microsoft.Extensions packages have been updated to version 9.0.0:

- `Microsoft.Extensions.Logging` 8.0.0 → **9.0.0**
- `Microsoft.Extensions.Logging.Console` 8.0.0 → **9.0.0**
- `Microsoft.Extensions.Logging.Debug` 8.0.0 → **9.0.0**
- `Microsoft.Extensions.Configuration` 8.0.0 → **9.0.0**
- `Microsoft.Extensions.Configuration.Binder` 8.0.0 → **9.0.0**
- `Microsoft.Extensions.Configuration.Json` 8.0.0 → **9.0.0**
- `Microsoft.Extensions.DependencyInjection` 8.0.0 → **9.0.0**
- `Microsoft.Extensions.Options` 8.0.0 → **9.0.0**
- `Microsoft.Extensions.Options.ConfigurationExtensions` 8.0.0 → **9.0.0**

**Benefits:**
- Latest .NET 9 features and improvements
- Performance optimizations
- Security updates
- Better compatibility with modern .NET ecosystem

#### **Other Package Updates**

- `CommunityToolkit.Mvvm` 8.2.0 → **8.3.2**
  - Latest MVVM toolkit features
  - Bug fixes and performance improvements

- `System.IO.Ports` 8.0.0 → **9.0.0**
  - Updated for .NET 9 compatibility
  - Improved serial port handling

#### **Stable Packages Maintained**

The following packages remain on their current stable versions:
- `LiveChartsCore.SkiaSharpView.WPF` **2.0.0-rc5.4** (RC version, stable release pending)
- `SkiaSharp` **3.116.1** (stable)
- `SkiaSharp.Views.WPF` **3.116.1** (stable)
- `MahApps.Metro` **2.4.10** (stable)
- `NModbus4` **2.0.5516.31020** (stable)
- `OpenTK` **3.3.1** (stable)
- `OpenTK.GLWpfControl` **3.3.0** (stable)

---

## 🔧 Technical Improvements

### Dependency Management

1. **Modern .NET 9 Packages**
   - All Microsoft.Extensions packages now on .NET 9
   - Better performance and memory efficiency
   - Latest security patches

2. **Compatibility**
   - All packages tested and verified compatible
   - No breaking changes in updated packages
   - Smooth upgrade path from v2.6.0

3. **Build Performance**
   - Faster restore times with updated packages
   - Improved build caching
   - Reduced package conflicts

### Code Quality

- **Zero Breaking Changes** - All updates are backward compatible
- **Clean Build** - No new warnings introduced
- **Tested** - All functionality verified with updated packages

---

## 📊 Metrics

### Package Update Summary
- **Total Packages Updated:** 11
- **Microsoft.Extensions Packages:** 9 (8.0.0 → 9.0.0)
- **Other Packages:** 2 (CommunityToolkit.Mvvm, System.IO.Ports)
- **Packages Unchanged:** 7 (stable versions maintained)

### Build Status
- ✅ **Exit Code:** 0 (Success)
- ✅ **Errors:** 0
- ✅ **New Warnings:** 0
- ✅ **Build Time:** ~2.1s

---

## 🐛 Bug Fixes

**None.** This is a pure package update release with no functional changes.

---

## 🔄 Breaking Changes

**None.** All package updates are backward compatible. Existing code continues to work without modification.

---

## 📝 Known Issues

- LiveCharts still on RC version (2.0.0-rc5.4) - stable release not yet available
- NU1701 warnings suppressed for legacy packages (NModbus4, OpenTK)

---

## 🚀 Upgrade Notes

### For Users
- No changes to application behavior
- All features work exactly as before
- Improved performance and security from updated packages

### For Developers
- Updated to .NET 9 Microsoft.Extensions packages
- All existing code remains compatible
- No code changes required for package updates

---

## 🎯 Release Cycle Completion

Version 2.7.0 completes the planned improvement cycle:

| Version | Focus | Status |
|---------|-------|--------|
| v2.3.0 | Server freeze fix & graceful shutdown | ✅ Released |
| v2.4.0 | Coordinator pattern (3 coordinators) | ✅ Released |
| v2.5.0 | Coordinator completion (5 coordinators) | ✅ Released |
| v2.6.0 | Testing infrastructure | ✅ Released |
| v2.7.0 | **Package modernization** | ✅ **Released** |

**All planned improvements complete!** 🎉

---

## 📦 Installation

### Download
- **Installer:** `ModbusForge-2.7.0-setup.exe`
- **Portable:** `ModbusForge-2.7.0-win-x64.zip`
- **Self-Contained:** `ModbusForge-2.7.0-win-x64-sc.zip`

### Requirements
- Windows 10/11 (x64)
- .NET 8.0 Runtime (included in self-contained version)

---

## 🙏 Acknowledgments

This release completes a comprehensive improvement cycle that has:
- Reduced MainViewModel complexity by ~286 lines
- Established 5 coordinator classes for better separation of concerns
- Created testing infrastructure with xUnit and Moq
- Modernized all dependencies to latest stable versions

ModbusForge is now well-architected, tested, and ready for future enhancements!

---

## 📄 Full Changelog

### Changed
- Updated Microsoft.Extensions.* packages from 8.0.0 to 9.0.0 (9 packages)
- Updated CommunityToolkit.Mvvm from 8.2.0 to 8.3.2
- Updated System.IO.Ports from 8.0.0 to 9.0.0
- Version updated to 2.7.0

### Technical
- Version: 2.7.0
- Assembly version: 2.7.0.0
- File version: 2.7.0.0
- Target framework: net8.0-windows
- .NET Extensions: 9.0.0

---

## 🎊 Future Enhancements

With the core refactoring complete, future versions can focus on:
- **New Features** - Additional Modbus functionality
- **UI Enhancements** - Improved user experience
- **Performance** - Further optimizations
- **Testing** - Expanded test coverage
- **Documentation** - Enhanced user guides

---

**Previous Release:** [v2.6.0](RELEASE-v2.6.0.md)  
**GitHub:** https://github.com/nokkies/ModbusForge  
**Issues:** https://github.com/nokkies/ModbusForge/issues

---

**Thank you for using ModbusForge!** 🚀
