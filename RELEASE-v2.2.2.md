# ModbusForge v2.2.2 Release Summary

## 🎯 Release Information
- **Version**: 2.2.2
- **Release Date**: January 10, 2026
- **Git Tag**: v2.2.2
- **Commit**: 4435fac

## ✨ Major New Features

### 🔧 Script Rules Automation System
- **Complete rule-based automation** for Modbus operations
- **Visual rule editor** with intuitive interface
- **Real-time rule evaluation** (250ms intervals)
- **Support for all Modbus areas**: HoldingRegister, InputRegister, Coil, DiscreteInput
- **Multiple comparison operators**: Equals, NotEquals, GreaterThan, LessThan, GreaterThanOrEqual, LessThanOrEqual
- **Action types**: SetRegister, SetCoil, LogMessage
- **Delay functionality** for timed actions
- **One-time trigger support** for initialization rules

### 📚 Enhanced User Experience
- **Added "Script Rules Help..."** menu item under Help menu
- **Comprehensive built-in documentation**
- **Easy access** to script rules guide without opening editor
- **Improved menu organization** with separators

## 🔧 Technical Improvements

### 🏗️ Architecture
- **Resolved circular dependency issues** in DI container
- **Clean dependency injection** with proper service separation
- **Self-contained ScriptRuleService** with internal timer
- **Proper resource management** with IDisposable implementation
- **Configuration-driven UnitId selection**

### 🐛 Bug Fixes
- **Fixed circular dependency** between MainViewModel and ScriptRuleService
- **Improved error handling** and logging
- **Enhanced null reference safety**

## 📁 File Changes

### New Files (7)
- `Models/ScriptRule.cs` - Rule model with properties and methods
- `Services/IScriptRuleService.cs` - Service interface definition
- `Services/ScriptRuleService.cs` - Implementation with timer and evaluation logic
- `ViewModels/ScriptEditorViewModel.cs` - Editor view model with commands
- `Views/ScriptEditorWindow.xaml/.xaml.cs` - Visual editor UI
- `Resources/FluentTheme.xaml` - Theme resources

### Modified Files (6)
- `MainWindow.xaml/.xaml.cs` - Help menu integration
- `App.xaml.cs` - DI registration fixes
- `MainViewModel.cs` - Version updates and dependency cleanup
- `ModbusForge.csproj` - Version updated to 2.2.2
- `setup/ModbusForge.iss` - Installer version 2.2.2

## 🚀 Release Artifacts

### Installer
- **File**: `ModbusForge-2.2.2-setup.exe`
- **Size**: 7.77 MB
- **Type**: Inno Setup installer with uninstaller
- **Location**: `installers/ModbusForge-2.2.2-setup.exe`

### Portable Version
- **File**: `ModbusForge-v2.2.2-portable.zip`
- **Size**: 74.8 MB
- **Type**: Self-contained single-file executable
- **Location**: `installers/ModbusForge-v2.2.2-portable.zip`

## 📋 System Requirements
- **.NET 8.0 Windows**
- **Windows 10/11 (x64)**
- **4GB RAM minimum**
- **100MB disk space**

## 🚀 How to Use

### Access Script Rules Help
1. Open ModbusForge
2. Go to `Help → Script Rules Help...`
3. Read comprehensive documentation

### Create Script Rules
1. Go to `Options → Script Editor...`
2. Click "Add Rule" to create a new rule
3. Configure conditions and actions
4. Enable rules for automatic evaluation

### Rule Examples
- **If register 40001 > 100, set register 40002 to 200**
- **If coil 1 turns ON, wait 5 seconds then turn ON coil 2**
- **If input register 30001 equals 42, log "Sensor triggered"**

## 🔗 Links
- **GitHub Repository**: https://github.com/nokkies/ModbusForge
- **Git Tag**: https://github.com/nokkies/ModbusForge/releases/tag/v2.2.2
- **Issues**: https://github.com/nokkies/ModbusForge/issues

## 📝 Release Notes
This release adds powerful automation capabilities to ModbusForge, enabling sophisticated rule-based control systems with an intuitive visual interface. The script rules system allows users to create complex automation logic without writing code, making ModbusForge suitable for both simple monitoring and advanced industrial control applications.

## 🎉 Next Steps
1. Download the installer or portable version
2. Install and launch ModbusForge v2.2.2
3. Access Help → Script Rules Help for detailed documentation
4. Create your first automation rule using the Script Editor

---

**This release represents a major milestone in ModbusForge's evolution, adding enterprise-grade automation capabilities while maintaining the application's ease of use and reliability.**
