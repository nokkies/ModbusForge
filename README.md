# ModbusForge

Modbus TCP client/server WPF application built with .NET 8.0 (Windows, WPF).

## Current Status

### ✅ Completed
- Project structure and solution setup (WPF + MVVM with CommunityToolkit.Mvvm)
- Dependency Injection and typed configuration via `ServerSettings` (Microsoft.Extensions.Options)
- Modbus TCP client and server services using FluentModbus
- Connection UI, read/write for registers and coils
- Monitoring: periodic reads for Registers, Coils, Discrete Inputs, and per-row monitoring in Custom tab
- Custom tab: per-row area (HoldingRegister/Coil/InputRegister/DiscreteInput), data type (uint/int/real/string), on-demand Read/Write, and per-row continuous write/read with periods
- Persistence for Custom entries to JSON (Save/Load)

### 🚧 In Progress
- Start/Stop Modbus server from the UI
- UI enhancements and refactors
- Simulation feature design (logic blocks and register connectors)
- Comprehensive error handling and polish

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

## Features

- Client and Server modes (configured via `ModbusForge/ModbusForge/appsettings.json`, section `ServerSettings.Mode`)
- Modbus TCP client operations: read/write holding registers, read coils, discrete inputs, and input registers
- Per-tab monitoring with configurable periods
- Custom tab with per-row:
  - Area: `HoldingRegister`, `Coil`, `InputRegister`, `DiscreteInput`
  - Type: `uint`, `int`, `real` (32-bit float across 2 registers), `string` (2 chars per 16-bit register)
  - On-demand Read/Write buttons
  - Continuous Write and Continuous Read with per-row period
  - Save/Load entries to JSON (`custom-entries.json`)

## Modes: Client vs Server

- Configure in `ModbusForge/ModbusForge/appsettings.json` under `ServerSettings`:
  - `Mode`: `Client` or `Server`
  - `DefaultPort`, `DefaultUnitId`, etc.
- The app selects `IModbusService` implementation based on `Mode` (client or server) via DI.
- Server start/stop from UI is planned and under active development.

## Versioning

- The window title displays the application version from the assembly ProductVersion (fallback to `v1.0.2`).

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

This project uses the excellent FluentModbus library for Modbus client and server functionality:

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

## Next Steps

1. Wire server start/stop into UI commands
2. Finalize UI refactors and tab UX polish
3. Implement simulation function blocks and connectors
4. Add comprehensive error handling and user feedback
5. Add unit and integration tests

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.