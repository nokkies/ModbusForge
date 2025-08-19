# ModbusForge

A WPF application for Modbus TCP communication, built with .NET 9.0.

## Current Status

### ✅ Completed
- Project structure and solution setup
- MVVM architecture implementation
- Dependency injection configuration
- Basic Modbus TCP service implementation
- Main window UI with connection management

### 🚧 In Progress
- Resolving .NET SDK installation/configuration issues
- Testing Modbus communication
- Implementing register and coil monitoring UI
- Adding comprehensive error handling

## Prerequisites

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
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

## Troubleshooting

### .NET SDK Issues
If you encounter issues with the .NET SDK:

1. Verify .NET 8.0 SDK is installed:
   ```
   dotnet --version
   ```
   Should return a version starting with `9.0`

2. If not installed, download and install from [.NET 9.0 Downloads](https://dotnet.microsoft.com/download/dotnet/9.0)

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

1. Resolve any remaining build issues
2. Test Modbus TCP communication with a test server
3. Implement register and coil monitoring UI
4. Add comprehensive error handling and user feedback
5. Add unit and integration tests

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.