# ModbusForge v5.8.0

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Windows](https://img.shields.io/badge/Windows-10%2F11-0078D4?logo=windows)](https://www.microsoft.com/windows)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![GitHub release](https://img.shields.io/github/v/release/nokkies/ModbusForge)](https://github.com/nokkies/ModbusForge/releases)
[![GitHub issues](https://img.shields.io/github/issues/nokkies/ModbusForge)](https://github.com/nokkies/ModbusForge/issues)

A professional Modbus TCP client/server application built with .NET 8.0 and WPF. ModbusForge provides comprehensive tools for testing, monitoring, and automating Modbus communications.

![ModbusForge](ModbusForge/Resources/ModbusForgeLOGO.png)

## Table of Contents

- [Quick Start](#quick-start)
- [What's New](#whats-new)
- [Key Features](#key-features)
- [Screenshots](#screenshots)
- [Installation](#installation)
- [Feature Details](#feature-details)
- [Modes: Client vs Server](#modes-client-vs-server)
- [FAQ](#faq)
- [Contributing](#contributing)
- [Build and Release](#build-and-release)
- [Versioning](#versioning)
- [Support](#support)

---

## Quick Start

Get up and running with ModbusForge in 5 minutes.

### 1. Launch the Application

```powershell
dotnet run --project ModbusForge
```

### 2. Configure Mode

Choose between **Client** or **Server** mode in `appsettings.json`:

```json
{
  "ServerSettings": {
    "Mode": "Client",
    "DefaultPort": 502,
    "DefaultUnitId": 1
  }
}
```

### 3. Connect (Client Mode)

1. Enter the IP address of your Modbus TCP server
2. Enter the port (default: 502)
3. Enter the Unit ID (slave ID)
4. Click **Connect**

### 4. Read Data

1. Select the **Registers** tab
2. Enter the starting address and count
3. Click **Read**
4. Enable **Continuous Read** for automatic polling

### 5. Explore More

- **Options → Connection Manager**: Save and manage multiple connection profiles
- **Options → Script Editor**: Create automated test sequences
- **Options → Preferences**: Customize application behavior
- **Help → Keyboard Shortcuts**: View all available shortcuts

---

## What's New

### v5.6.0 - Documentation & User Experience

- **Comprehensive Help System**: New searchable help window with F1 support
- **Troubleshooting Tools**: Built-in troubleshooting guide with diagnostic export
- **Improved Keyboard Shortcuts**: Expanded shortcut coverage with quick reference printing
- **Modern Dialog Styling**: About, Keyboard Shortcuts, Script Editor, and Troubleshooting windows now use Fluent UI
- **Tab Stability**: Removed accidental tab close buttons to prevent empty panes
- **Better README**: Restructured documentation with quick start, FAQ, and contributing sections

### v5.3.0 - UX Quick Wins

- **Automatic Continuous Read**: Trend lines now automatically enable continuous read when added
- **Enhanced Error Logging**: Specific exception handling with detailed logging
- **Global Keyboard Shortcuts**: Ctrl+R read, Ctrl+T trends, Ctrl+S save, F5 refresh, F1 help
- **Improved Error Messages**: User-friendly messages with recovery suggestions

### v5.2.0 - Resilience & Error Handling

- **Centralized Resilience**: Retry policy with exponential backoff and jitter
- **Circuit Breaker Pattern**: Prevents cascading connection failures
- **Startup Configuration Validation**: Schema validation for `appsettings.json`
- **Validation Service**: Input validation for IP addresses, ports, unit IDs, and registers

See [FEATURE_ROADMAP.md](FEATURE_ROADMAP.md) for the full development roadmap.

---

## Key Features

### Core Functionality
- 🔌 **Client & Server Modes**: Switch between Modbus TCP client and server
- 📝 **Full Register Support**: Read/write holding registers, input registers, coils, and discrete inputs
- 📊 **Real-time Monitoring**: Continuous polling with configurable intervals
- 🔍 **Connection Diagnostics**: Test TCP and Modbus connectivity with latency measurements

### Multi-Device Support
- Connect to multiple Modbus servers simultaneously
- Save and manage connection profiles
- Quick switching between active connections
- Profiles persist between sessions

### Scripting & Automation
- Visual script editor for creating test sequences
- Support for read/write operations, delays, and logging
- Run scripts with repeat counts and configurable delays
- Save/load scripts as `.mbscript` files

### Data Visualization
- 📈 **Trend Charts**: Real-time graphing with zoom/pan controls
- 📤 **CSV/PNG Export**: Export trend data and charts
- 🖥️ **Console Logging**: Real-time log of all Modbus operations

### Custom Data Tab
- Per-row configuration: Area, Type (uint/int/real/string)
- On-demand and continuous read/write
- Live value updates with trend integration
- Save/Load configurations to JSON

### Visual Simulation
- 🎨 **Visual Node Editor**: Graphical programming for Modbus simulations
- 📶 **Signal Generators**: Ramp, Sine, Triangle, and Square waveforms
- 🔗 **Node Connections**: Wire nodes together to define data flow
- 🔄 **Real-time Simulation**: Execute simulations and monitor values

---

## Screenshots

> *Screenshots will be added here in a future update. The following sections describe the main interfaces.*

### Main Interface
The main window provides a tabbed interface for registers, coils, custom data, simulation, trends, and console logging.

### Connection Manager
Save and manage multiple Modbus connection profiles with quick connect/disconnect.

### Script Editor
Create and run automated test sequences with a visual command editor.

### Visual Node Editor
Build simulations using nodes and connections for signal generation and data transformation.

### Trend Charts
Monitor register values over time with zoom, pan, and export capabilities.

---

## Installation

When you download and run the installer for ModbusForge, Windows Defender SmartScreen will likely show a warning because the application is not digitally signed with a commercial certificate.

To install the application, follow these steps:

1. Run the `ModbusForge-x.x.x-setup.exe` installer.
2. Windows will show a blue window titled "Windows protected your PC".
3. Click on the **More info** link.
4. The publisher will be listed as "Unknown". Click the **Run anyway** button to proceed with the installation.

---

## Feature Details

### Connection Manager

Access via **Options → Connection Manager**

- Create, edit, and delete connection profiles
- Each profile stores: Name, IP Address, Port, Unit ID
- Connect/disconnect individual profiles
- Set active connection for main window operations
- Profiles saved to `%AppData%\ModbusForge\connection-profiles.json`

### Script Editor

Access via **Options → Script Editor** or press **Ctrl+E**

**Supported Commands:**
- Read Holding Registers / Input Registers
- Read Coils / Discrete Inputs
- Write Single Register / Coil
- Delay (configurable milliseconds)
- Log messages

**Script Settings:**
- Repeat count for looping
- Delay between commands
- Stop on error option

**Output Log:** Real-time execution feedback

See [docs/SCRIPTING_GUIDE.md](docs/SCRIPTING_GUIDE.md) for detailed scripting documentation.

### Preferences

Access via **Options → Preferences**

- Auto-reconnect on connection loss
- Show diagnostics on connection error
- Console logging settings
- Confirm before exit
- Settings saved to `%AppData%\ModbusForge\settings.json`

### Custom Data Tab

- **Area Types:** HoldingRegister, Coil, InputRegister, DiscreteInput
- **Data Types:** uint, int, real (32-bit float), string
- On-demand Read/Write buttons per row
- Continuous Write mode per row
- Live reads when Global Continuous Read is enabled
- Save/Load configurations to JSON

### Trend & Logging

- Real-time trend charts with zoom/pan
- Adjustable retention window (1–60 minutes)
- Export to CSV or PNG
- Console tab shows all Modbus operations

### Visual Node Editor

Access via the **Simulation** tab or left navigation panel.

- Drag nodes from the palette onto the canvas
- Connect nodes by dragging from outputs to inputs
- Configure node parameters in the properties panel
- Run simulations and monitor real-time values

---

## Modes: Client vs Server

Configure in `ModbusForge/ModbusForge/appsettings.json` under `ServerSettings`:

- `Mode`: `Client` or `Server`
- `DefaultPort`, `DefaultUnitId`, etc.

Both client and server services are registered; the `MainViewModel` selects the `IModbusService` implementation at runtime based on `Mode`.

### Client Mode
Connect to an existing Modbus TCP server. Use this for testing and monitoring real devices.

### Server Mode
Act as a Modbus TCP server for testing client applications. Configure the listening port and allowed Unit IDs.

---

## FAQ

### Q: What operating systems are supported?
**A:** ModbusForge is built for Windows 10 and Windows 11 using WPF and .NET 8.0.

### Q: Do I need administrator privileges?
**A:** Only if you use the default Modbus port 502. Windows requires admin privileges to bind to ports below 1024. You can use a higher port number (e.g., 1502) to avoid this.

### Q: Can I connect to multiple devices at once?
**A:** Yes, use the Connection Manager to create and manage multiple profiles. You can switch between active connections.

### Q: Where are my settings saved?
**A:** Application settings are saved to `%AppData%\ModbusForge\settings.json`. Connection profiles are saved to `%AppData%\ModbusForge\connection-profiles.json`.

### Q: How do I export trend data?
**A:** Open the Trend tab and use the **Trend** menu to export to CSV or PNG.

### Q: What file format does the Script Editor use?
**A:** Scripts are saved as `.mbscript` files in JSON format.

### Q: The application won't connect to my device. What should I check?
**A:** Verify the IP address, port, and Unit ID. Ensure the device is reachable on the network and that your firewall allows the connection. Use the **Connection Manager** diagnostics or **Help → Troubleshooting** for more guidance.

### Q: Is ModbusForge open source?
**A:** Yes, ModbusForge is open source. See the [LICENSE](LICENSE) file for details.

---

## Contributing

We welcome contributions to ModbusForge! Here are some ways you can help:

### Reporting Issues
- Check existing issues first to avoid duplicates
- Provide detailed steps to reproduce the problem
- Include your ModbusForge version, Windows version, and .NET version
- Attach screenshots or logs if applicable

### Suggesting Features
- Open a GitHub issue with the `enhancement` label
- Describe the feature and its use case
- Include mockups or examples if possible

### Code Contributions
1. Fork the repository
2. Create a feature branch: `git checkout -b feature/your-feature-name`
3. Make your changes following the existing code style
4. Add tests if applicable
5. Commit with clear messages
6. Push to your fork and open a Pull Request

### Code Style
- Use `ILogger` for all logging (no `Debug.WriteLine` or custom file logging)
- Use constants for magic numbers
- Implement proper event handler cleanup to prevent memory leaks
- Add input validation with visual feedback for user inputs

---

## Build and Release

Below are PowerShell commands tested on Windows to produce a Release build and package artifacts.

### Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022 (17.0 or later) with .NET desktop development workload (optional)

### Build (Release)

```powershell
dotnet clean
dotnet restore
dotnet build ModbusForge.sln -c Release
```

### Publish (framework-dependent, single-file)

```powershell
$version = "5.6.0"
dotnet publish .\ModbusForge\ModbusForge.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:PublishTrimmed=false -o .\publish\win-x64
```

### Publish (self-contained, single-file)

```powershell
$version = "5.6.0"
dotnet publish .\ModbusForge\ModbusForge.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -o .\publish\win-x64-sc
```

### Create a ZIP Artifact

```powershell
$version = "5.6.0"
Compress-Archive -Path .\publish\win-x64\* -DestinationPath .\ModbusForge-$version-win-x64.zip -Force
# or for self-contained
Compress-Archive -Path .\publish\win-x64-sc\* -DestinationPath .\ModbusForge-$version-win-x64-sc.zip -Force
```

### Create an Installer

```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" "setup\ModbusForge.iss"
```

---

## Versioning

- The window title displays the application version from the assembly ProductVersion
- Versions follow [Semantic Versioning](https://semver.org/)
- See [FEATURE_ROADMAP.md](FEATURE_ROADMAP.md) for planned releases

---

## Support

- **GitHub Issues**: [https://github.com/nokkies/ModbusForge/issues](https://github.com/nokkies/ModbusForge/issues)
- **Email**: [reinach@softwareForge.cc](mailto:reinach@softwareForge.cc)
- **Documentation**: See the `docs/` folder and in-app Help (F1)

---

*Built with ❤️ by Reinach van Nieuwenhuizen*
