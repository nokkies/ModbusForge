# ModbusForge v3.4.3 - Enhanced Save/Load with Auto-Filename Generation

## 🎉 Major Release Features

### ✨ Auto-Filename Generation
- **Smart Naming**: Automatic filename generation with IP address and Unit ID placeholders
- **Format**: `MBIP{IP_ADDRESS}_ID{UNIT_ID}_{TIMESTAMP}`
- **Examples**:
  - Client: `MBIP127000000001_ID1_20260310_125500.mfp`
  - Server: `MBIP192000000168_ID3_20260310_125500.mfp`
  - Export Unit ID: `MBIP192000000168_ID1_ID1_20260310_125500.mui`

### 🔧 Enhanced Save/Load Functionality
- **Mode-Aware Operations**: Different behavior for Client vs Server modes
- **Client Mode**: Save/load single client configuration only
- **Server Mode**: Save/load complete multi-Unit ID project structure
- **Unit ID Export/Import**: Export specific Unit ID and import as different Unit ID

### 🏢 Complete Unit ID Isolation
- **Per-Unit ID State**: Each Unit ID gets isolated custom entries, simulation, and monitoring
- **Independent Settings**: No cross-contamination between Unit ID configurations
- **Flexible Management**: Import/export Unit IDs between different projects

## 🛠️ Technical Improvements

### 🐛 Critical Fixes
- **TwoWay Binding Crash**: Fixed fatal errors caused by read-only property bindings
- **Property Delegation**: Converted read-only delegated properties to writable with proper setters
- **Application Stability**: Resolved crashes on startup and during configuration changes

### 🎨 UI/UX Enhancements
- **InputDialog Control**: New dialog for Unit ID import/export workflows
- **Menu Structure**: Enhanced File menu with new export/import options
- **Mode-Aware UI**: Server-only features hidden in Client mode

### 📁 File Format Updates
- **.mfp**: ModbusForge Project (complete project with all Unit IDs)
- **.mui**: ModbusForge Unit ID (single Unit ID configuration)
- **Enhanced JSON Structure**: Improved project configuration serialization

## 🚀 New Commands & Features

### File Operations
- `Save Project...` (Ctrl+S): Mode-aware project saving
- `Open Project...` (Ctrl+O): Project loading with automatic mode detection
- `Export Unit IDs...`: Bulk export of all Unit ID configurations
- `Export Unit ID...`: Export specific Unit ID (Server only)
- `Import Unit IDs...`: Bulk import with conflict detection
- `Import Unit ID As...`: Import Unit ID as different Unit ID (Server only)

### Engineer Benefits
- **No Manual Typing**: Engineers don't need to type IP addresses or filenames
- **Consistent Naming**: Standardized format across all save operations
- **Easy Identification**: Quick identification of IP and Unit ID from filename
- **Timestamp Tracking**: Built-in timestamp for version control
- **Searchable Files**: Easy to find files for specific IP/Unit ID combinations

## 🔧 Migration Notes

### Breaking Changes
- **Project Structure**: Projects now save/load differently based on mode
- **Unit ID Isolation**: Complete separation of Unit ID configurations
- **File Extensions**: New `.mui` extension for Unit ID exports

### Compatibility
- **Backward Compatible**: Can load projects from previous versions
- **Automatic Migration**: Older projects automatically converted to new structure
- **Legacy Commands**: Hidden but retained for compatibility

## 📋 Installation & Setup

### Requirements
- .NET 8.0 or higher
- Windows 10/11 or compatible Windows Server
- Network access for Modbus TCP communication

### Installation
1. Download the latest release from GitHub
2. Extract to desired location
3. Run `ModbusForge.exe`
4. Configure Client/Server mode as needed

## 🐛 Bug Fixes

### Critical Issues Fixed
- **Fatal Startup Crash**: TwoWay binding to read-only properties resolved
- **Memory Leaks**: Proper disposal of resources and event subscriptions
- **UI Freezes**: Improved async handling in file operations
- **Configuration Loss**: Enhanced error handling during save/load operations

### Stability Improvements
- **Exception Handling**: Comprehensive error handling throughout the application
- **Logging**: Enhanced logging for better debugging
- **Resource Management**: Improved memory and resource cleanup

## 🎯 Performance Optimizations

### Memory Usage
- **Reduced Memory Footprint**: Optimized data structures and collections
- **Lazy Loading**: On-demand loading of Unit ID configurations
- **Efficient Cloning**: Improved deep copy implementations

### Response Time
- **Faster File Operations**: Optimized JSON serialization/deserialization
- **Improved UI Responsiveness**: Better async/await patterns
- **Reduced Startup Time**: Optimized initialization sequence

## 🔮 Future Roadmap

### Upcoming Features
- **Multi-Protocol Support**: Modbus RTU and ASCII support
- **Advanced Monitoring**: Real-time data visualization
- **Plugin System**: Extensible architecture for custom protocols
- **Cloud Integration**: Remote configuration storage and synchronization

### Planned Improvements
- **Mobile Companion App**: Remote monitoring and control
- **Advanced Analytics**: Data logging and analysis features
- **Integration APIs**: REST API for third-party integration
- **Security Enhancements**: Authentication and authorization features

## 📞 Support & Community

### Getting Help
- **Documentation**: Comprehensive user manual and API documentation
- **Community Forum**: Join discussions and get help from other users
- **Bug Reports**: Report issues via GitHub Issues
- **Feature Requests**: Suggest improvements via GitHub Discussions

### Contributing
- **Source Code**: Available on GitHub under MIT license
- **Pull Requests**: Welcome contributions from the community
- **Development Guide**: Instructions for setting up development environment
- **Code of Conduct**: Community guidelines for contributors

---

**Thank you for using ModbusForge! 🎉**

This release represents a significant step forward in Modbus device management and testing tools. Your feedback and contributions help make ModbusForge better for everyone.

*ModbusForge v3.4.3 - Released March 10, 2026*
