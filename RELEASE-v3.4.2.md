# ModbusForge v3.4.2 Release Notes

## 🎯 Major Features

### Unit ID Dropdown for Server Mode
- **Unit ID Selection**: Added dropdown in server mode to select active Unit ID
- **Data Filtering**: Custom entries and register operations now filter by selected Unit ID
- **Auto-Refresh**: Custom entries automatically refresh when Unit ID selection changes
- **Smart Mode Detection**: Uses dropdown selection in server mode, global Unit ID in client mode

## 🔧 Improvements
- **Default Simulation**: Classic mode now defaults on startup (was Visual mode)
- **Clean Visual Editor**: Visual Node Editor starts empty without sample nodes
- **Publishing Port Support**: Enhanced server binding to all interfaces (0.0.0.0)

## 🧪 Testing
- **Unit Tests**: Added comprehensive tests for publishing port functionality
- **Multi-Client Support**: Verified multiple client connections to publishing port
- **Unit ID Parsing**: Tested range notation (e.g., "1,5,10-15,20")

## 🐛 Bug Fixes
- Fixed Unit ID dropdown not updating table data
- Resolved server binding to show actual interface IPs instead of 0.0.0.0

## 📦 Installation
1. Download `ModbusForge-v3.4.2.zip` from GitHub releases
2. Extract to desired location
3. Run `ModbusForge.exe`

## 🚀 Usage
1. Start ModbusForge in **Server mode**
2. Configure Unit IDs (e.g., "1,2,3")
3. Connect server
4. Use **Active ID** dropdown to switch between Unit IDs
5. All Custom entries and register operations now respect the selected Unit ID

## 🔄 Technical Details

### EffectiveUnitId Property
```csharp
public byte EffectiveUnitId => IsServerMode ? SelectedUnitId : UnitId;
```

### OnSelectedUnitIdChanged Event
- Automatically refreshes Custom entries when dropdown selection changes
- Only triggers in server mode when connected

### Unit Tests Added
- `ModbusServerPublishingPortTests.cs` with 8 comprehensive tests
- Tests for 0.0.0.0 binding, multiple clients, Unit ID parsing, and error handling

## 📋 Files Changed
- `MainViewModel.cs` - Added Unit ID dropdown logic and EffectiveUnitId
- `MainWindow.xaml` - Added dropdown UI for server mode
- `ModbusServerService.cs` - Enhanced BoundEndpoint property
- `ModbusForge.Tests/` - Added publishing port tests
- `ModbusForge.csproj` - Version bumped to 3.4.2

## 🎉 Breaking Changes
None - fully backward compatible.

## 🐳 Docker Support
No changes to Docker configuration.

## 🔗 Links
- GitHub Repository: https://github.com/nokkies/ModbusForge
- Issues: https://github.com/nokkies/ModbusForge/issues
- Documentation: https://github.com/nokkies/ModbusForge/blob/master/README.md
