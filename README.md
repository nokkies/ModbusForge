# ModbusForge v2.1.0

Modbus TCP client/server WPF application built with .NET 8.0 (Windows, WPF).

## Current Status

### ✅ Completed
- Project structure and solution setup (WPF + MVVM with CommunityToolkit.Mvvm)
- Dependency Injection and typed configuration via `ServerSettings` (Microsoft.Extensions.Options)
- Modbus TCP client and server services using NModbus4 v2.0.0
- Connection UI, read/write for registers and coils
- Monitoring: periodic reads for Registers, Coils, Discrete Inputs, gated by a single global Continuous Read toggle
- Custom tab: per-row Area (HoldingRegister/Coil/InputRegister/DiscreteInput) and Type (uint/int/real/string), on-demand Read/Write, per-row Continuous Write, and live read updates driven by Trend-enabled rows
- Logging/Trend tab: select rows to trend, zoom/pan, CSV/PNG export, retention window 1–60 minutes
- Persistence for Custom entries to JSON (Save/Load)
- Start/Stop Modbus server from the UI

## Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022 (17.0 or later) with .NET desktop development workload

## Getting Started

1. **Clone the repository**
   ```
   git clone https://github.com/yourusername/ModbusForge.git
   cd ModbusForge
   ```

2. **Restore NuGet packages**
   ```
   dotnet restore
   ```

3. **Build the solution**
   ```
   dotnet build
   ```

4. **Run the application**
   ```
   dotnet run --project ModbusForge
   ```

## Installation

When you download and run the installer for ModbusForge, Windows Defender SmartScreen will likely show a warning because the application is not digitally signed with a commercial certificate.

To install the application, follow these steps:
1.  Run the `ModbusForge-x.x.x-setup.exe` installer.
2.  Windows will show a blue window titled "Windows protected your PC".
3.  Click on the **More info** link.
4.  The publisher will be listed as "Unknown". Click the **Run anyway** button to proceed with the installation.

## Features

- Client and Server modes (configured via `ModbusForge/ModbusForge/appsettings.json`, section `ServerSettings.Mode`)
- Modbus TCP client operations: read/write holding registers, read coils, discrete inputs, and input registers
- Global Continuous Read toggle that gates all periodic reads
- Custom tab with per-row:
  - Area: `HoldingRegister`, `Coil`, `InputRegister`, `DiscreteInput`
  - Type: `uint`, `int`, `real` (32-bit float across 2 registers), `string` (2 chars per 16-bit register)
  - On-demand Read/Write buttons
  - Continuous Write (per-row)
  - Live reads: when Global Continuous Read is ON, rows with `Trend` enabled are read at the trend sample rate and their `Value` updates in the grid
  - Save/Load entries to JSON (`custom-entries.json`)

- Logging/Trend
  - Add/remove trend series per Custom row (`Trend` column)
  - Adjustable retention window (1–60 minutes)
  - Zoom and pan controls, play/pause live window, reset axes
  - Export/Import CSV, export PNG

## Modes: Client vs Server

- Configure in `ModbusForge/ModbusForge/appsettings.json` under `ServerSettings`:
  - `Mode`: `Client` or `Server`
  - `DefaultPort`, `DefaultUnitId`, etc.
- Both client and server services are registered; the `MainViewModel` selects the `IModbusService` implementation at runtime based on `Mode`.
- Server start/stop from UI is planned and under active development.

## Versioning

- The window title displays the application version from the assembly ProductVersion (fallback to `v2.1.0`).

## Build and Release

Below are PowerShell commands tested on Windows to produce a Release build and package artifacts.

1. Build (Release):

```powershell
dotnet clean
dotnet restore
dotnet build ModbusForge.sln -c Release
```

2. Publish (framework-dependent, single-file):

```powershell
dotnet publish .\ModbusForge\ModbusForge.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:PublishTrimmed=false -o .\publish\win-x64
```

3. Publish (self-contained, single-file):

```powershell
dotnet publish .\ModbusForge\ModbusForge.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -o .\publish\win-x64-sc
```

4. Create a ZIP artifact:

```powershell
$version = "2.1.0"
Compress-Archive -Path .\publish\win-x64\* -DestinationPath .\ModbusForge-$version-win-x64.zip -Force
# or for self-contained
Compress-Archive -Path .\publish\win-x64-sc\* -DestinationPath .\ModbusForge-$version-win-x64-sc.zip -Force
```

5. Tag and create a GitHub Release (optional):

```powershell
$version = "2.1.0"
git tag v$version
git push origin v$version

# If GitHub CLI is installed
gh release create v$version .\ModbusForge-$version-win-x64.zip -t "ModbusForge v$version" -n "See changelog in README"
# Optionally upload self-contained ZIP as well:
gh release upload v$version .\ModbusForge-$version-win-x64-sc.zip
```

If you don’t use the GitHub CLI, you can create a release manually on GitHub and upload the ZIP file(s).

6. Create an Installer (optional):

This project uses [Inno Setup](https://jrsoftware.org/isinfo.php) to create a simple installer.

1. **Install Inno Setup:** Download and install the latest version of Inno Setup from the [official website](https://jrsoftware.org/isdl.php).
2. **Compile the Script:** Open the `setup/ModbusForge.iss` script in the Inno Setup Compiler, or run it from the command line from the project root:
   ```powershell
   & "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" "setup\ModbusForge.iss"
   ```
   The installer will be created in the `installers` directory.

## Changelog

- 2.1.0 (2025-10-23)
  - Added console logging tab for real-time monitoring of all Modbus operations
  - Console displays connection attempts, successes, failures, and data operations
  - Improved debugging and troubleshooting capabilities

- 2.0.2 (2025-10-23)
  - Fixed client address offset issues
  - Updated installer to version 2.0.2

- 2.0.1 (2025-10-23)
  - Fixed server address offset issues
  - Improved connection stability

- 2.0.0 (2025-10-22)
  - Migrated from FluentModbus to NModbus4 for improved server stability
  - Resolved server stopping issues
  - Updated dependencies and improved error handling

- 1.3.0 (2025-08-27)
  - Integrated MahApps.Metro theming and converted `MainWindow` to `MetroWindow`.
  - Kept Light theme (`Styles/Themes/Light.Blue.xaml`).
  - Restored tabs to system look for clarity; improved DataGrid readability with gridlines and alternating rows.
  - Minor UI polish and resources cleanup.
  - Updated installer and README to 1.3.0.

- 1.2.2 (2025-08-27)
  - Fixed startup crash by registering `DecodeViewModel` in DI (`App.xaml.cs`).
  - Restored missing `MenuItem_Donate_Click` handler in `MainWindow.xaml.cs`.
  - Updated installer script `setup/ModbusForge.iss` to 1.2.2 (AppVersion, OutputBaseFilename).
  - Refreshed README version references and example commands to 1.2.2.

- 1.1.1 (2025-08-23)
  - Trend: added retention window control (1–60 minutes) with Apply action.
  - Trend: Export CSV (selected/all series), Import CSV, and Export PNG buttons added to toolbar.
  - Minor UI polish on Trend tab; wiring with `TrendViewModel` and safe file dialogs.

- 1.1.0 (2025-08-23)
  - Version bump and Simulation scaffolding: added Simulation tab UI bindings in `MainWindow.xaml`.
  - `MainViewModel`: simulation timer that ramps holding registers and toggles coils when in Server mode.
  - `ModbusServerService`: helper methods for input registers and discrete inputs for simulation.

- 1.0.9 (2025-08-23)
  - Custom tab continuous read/trend fix: when Global Continuous Read is ON, rows with `Trend` enabled are read asynchronously by the trend timer and their `Value` updates live in the grid.
  - Removed per-row continuous read period in Custom; live reads are driven by the Trend sampler to avoid duplicate polling.
  - Value formatting during trend reads now matches single-read behavior for `uint`/`int`/`real`/`coil`/`discreteinput`.
  - README updated with Global Continuous Read behavior and Build/Release commands.

- 1.0.8 (2025-08-22)
  - Fixed XAML errors (XDG0008) by removing designer-only static types and relying on XAML arrays/resources.
  - Decoupled LiveCharts types from ViewModel; Zoom locking now handled via `LockToZoomModeConverter` using `ZoomAndPanMode` in the view.
  - Trend tab improvements: Play/Pause live window, Reset axes, CSV export/import; PNG export draws a white background to avoid transparent/black backgrounds.
  - Package alignment: SkiaSharp 3.116.1 + SkiaSharp.Views.WPF 3.116.1; LiveChartsCore.SkiaSharpView.WPF remains at 2.0.0-rc5.4.
  - Minor cleanups and nullability adjustments.

## Simulation Roadmap

- Logic function blocks: AND, OR, NOT, SET/RESET, timers (TON/TOF/TP)
- Connectors to Modbus registers/coils for inputs/outputs
- Visual block editor with wiring, polling, and write-back to registers
- Persistable simulation graphs and runtime execution with scan-cycle

## Project Structure

- `ModbusForge/` - Main WPF application project
  - `Configuration/` - Application configuration files
  - `Converters/` - Value converters for XAML bindings
  - `Models/` - Data models
  - `Services/` - Business logic and services
  - `ViewModels/` - ViewModels for MVVM pattern
  - `Views/` - XAML views
  - `App.xaml` - Application entry point
  - `MainWindow.xaml` - Main application window

## Attribution

This project uses the NModbus4 library for Modbus client and server functionality:

- NModbus4: https://github.com/NModbus4/NModbus4 (MIT License)

## Troubleshooting

### .NET SDK Issues
If you encounter issues with the .NET SDK:

1. Verify .NET 8.0 SDK is installed:
   ```
   dotnet --version
   ```
   Should return a version starting with `8.0`

2. If not installed, download and install from [.NET 8.0 Downloads](https://dotnet.microsoft.com/download/dotnet/8.0)

3. Restart your IDE/terminal after installation

### Build Issues
If you encounter build issues:

1. Clean the solution:
   ```
   dotnet clean
   ```

2. Restore packages:
   ```
   dotnet restore
   ```

3. Rebuild the solution:
   ```
   dotnet build
   ```

### Port already in use (10048)

- When starting the Modbus server, if the configured port (default `502`) is already in use, the app will not crash. Instead, it shows a friendly message and suggests trying an alternative port (e.g., `1502`).
- To find which process is using the port on Windows:
  ```
  netstat -ano | findstr :502
  ```
  Then locate the PID in Task Manager or with:
  ```
  tasklist | findstr <PID>
  ```
  You can either stop that process or change the server port in the UI and try again.

## Next Steps

1. Wire server start/stop into UI commands
2. Finalize UI refactors and tab UX polish
3. Implement simulation function blocks and connectors
4. Add comprehensive error handling and user feedback
5. Add unit and integration tests

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.