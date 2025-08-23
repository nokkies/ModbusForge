# ModbusForge

![Release Workflow](https://github.com/nokkies/ModbusForge/actions/workflows/release.yml/badge.svg)
![Latest Release](https://img.shields.io/github/v/release/nokkies/ModbusForge)
![Downloads](https://img.shields.io/github/downloads/nokkies/ModbusForge/total)

Modbus TCP client/server WPF application built with .NET 8.0 (Windows, WPF).

## Current Status

### ✅ Completed
- Project structure and solution setup (WPF + MVVM with CommunityToolkit.Mvvm)
- Dependency Injection and typed configuration via `ServerSettings` (Microsoft.Extensions.Options)
- Modbus TCP client and server services using FluentModbus
- Connection UI, read/write for registers and coils
- Monitoring: periodic reads for Registers, Coils, Discrete Inputs, gated by a single global Continuous Read toggle
- Custom tab: per-row Area (HoldingRegister/Coil/InputRegister/DiscreteInput) and Type (uint/int/real/string), on-demand Read/Write, per-row Continuous Write, and live read updates driven by Trend-enabled rows
- Logging/Trend tab: select rows to trend, zoom/pan, CSV/PNG export, retention window 1–60 minutes
- Persistence for Custom entries to JSON (Save/Load)

## Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022 (17.0 or later) with .NET desktop development workload

## Getting Started

1. **Clone the repository**
   ```
   git clone https://github.com/nokkies/ModbusForge.git
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

## Support / Donate

[![Donate with PayPal](https://img.shields.io/badge/Donate-PayPal-blue.svg)](https://www.paypal.com/donate/?hosted_button_id=ELTVNJEYLZE3W)

If you find ModbusForge useful, please consider supporting development:

- PayPal: https://www.paypal.com/donate/?hosted_button_id=ELTVNJEYLZE3W
- Donate Page (PayPal button): [docs/index.html](docs/index.html)

## Features

- Client and Server modes (configured via `ModbusForge/ModbusForge/appsettings.json`, section `ServerSettings.Mode`)
- Modbus TCP client operations: read/write holding registers, read coils, discrete inputs, and input registers
- Global Continuous Read toggle that gates all periodic reads
- Custom tab with per-row:
  - Area: `HoldingRegister`, `Coil`, `InputRegister`, `DiscreteInput`
  - Type: `uint`, `int`, `real` (32-bit float across 2 registers), `string` (2 chars per 16-bit register)
  - On-demand Read/Write buttons
  - Continuous Write (per-row)
  - Live reads: when Global Continuous Read is ON, rows with `Trend` enabled are read asynchronously by the trend timer and their `Value` updates live in the grid
  - Save/Load entries to JSON (`custom-entries.json`)
- Logging/Trend
  - Add/remove trend series per Custom row (`Trend` column)
  - Adjustable retention window (1–60 minutes)
  - Zoom and pan controls, play/pause live window, reset axes
  - Export/Import CSV, export PNG
- Decode tab
  - Always reads 2 registers and displays all decoding variants side-by-side:
    - None, Swap Bytes, Swap Words, Swap Bytes+Words
  - Shows both 16-bit (first word) and 32-bit (two words) interpretations:
    - Raw hex, Unsigned, Signed, Float (32-bit), ASCII
  - Read button is enabled only when connected; Enter key triggers Read
  - Address accepts decimal (e.g., 100) or hex (e.g., 0x64)
  - Shows "Reading..." while a read is in progress

## Modes: Client vs Server

- Configure in `ModbusForge/ModbusForge/appsettings.json` under `ServerSettings`:
  - `Mode`: `Client` or `Server`
  - `DefaultPort`, `DefaultUnitId`, etc.
- Both client and server services are registered; the `MainViewModel` selects the `IModbusService` implementation at runtime based on `Mode`.

### Server Controls

- Start server:
  - Set `Mode` to `Server`.
  - Set `Port` (default `502`, configurable via `ServerSettings.DefaultPort`).
  - Click `Start Server` (Connect button). Status shows "Server started" on success.
- Stop server:
  - Click `Disconnect`. Status shows "Server stopped".
- Defaults:
  - `DefaultPort` and `DefaultUnitId` come from `appsettings.json` (`ServerSettings`).
  - Note: `UnitId` applies to client operations. In Server mode, it is not used by the listener.
- Common error (port in use):
  - You’ll see a friendly message and an option to retry on port `1502`. See Troubleshooting → "Port already in use (10048)".

## Versioning

- The window title displays the application version from the assembly ProductVersion.
- CI builds stamp `Version`, `AssemblyVersion`, and `FileVersion` from the Git tag (e.g., `v1.1.2`). Local dev builds use the version in `ModbusForge/ModbusForge.csproj`.

## Download

- Latest Release: https://github.com/nokkies/ModbusForge/releases/latest
- Assets provided on each release:
  - `ModbusForge-<version>-win-x64.zip` (framework-dependent; requires .NET 8 Desktop Runtime)
  - `ModbusForge-<version>-win-x64-sc.zip` (self-contained; no .NET install required)
  - `ModbusForge-<version>-win-x64.msix` (Windows installer; double-click to install; signed if code-signing secrets are configured)
- Checksums: SHA256 file is attached to the Release for verification.
- MSIX requires Windows 10 1809 (build 17763) or later.

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
$version = "1.1.2"
Compress-Archive -Path .\publish\win-x64\* -DestinationPath .\ModbusForge-$version-win-x64.zip -Force
# or for self-contained
Compress-Archive -Path .\publish\win-x64-sc\* -DestinationPath .\ModbusForge-$version-win-x64-sc.zip -Force
```

5. Tag and create a GitHub Release (optional):

```powershell
$version = "1.1.2"
git tag v$version
git push origin v$version

# If GitHub CLI is installed
gh release create v$version .\ModbusForge-$version-win-x64.zip -t "ModbusForge v$version" -n "See changelog in README"
# Optionally upload self-contained ZIP as well:
gh release upload v$version .\ModbusForge-$version-win-x64-sc.zip
```

If you don’t use the GitHub CLI, you can create a release manually on GitHub and upload the ZIP file(s).

## Changelog

- 1.1.2 (2025-08-23)
  - Decode tab redesign: removed toggle checkboxes; always reads 2 registers and shows all swap variants side-by-side (None / Swap Bytes / Swap Words / Swap B+W).
  - ViewModel: computes all variants internally; Read is enabled only when connected; added IsBusy to prevent double-reads and show "Reading...".
  - UX polish: Enter key triggers Read; Address accepts decimal or 0x-prefixed hex; improved status messages.
  - Docs: README updated with Decode section and changelog entry.

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

This project uses the FluentModbus library for Modbus client and server functionality:

- FluentModbus: https://github.com/Apollo3zehn/FluentModbus (MIT License)

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

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.