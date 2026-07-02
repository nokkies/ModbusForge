# ModbusForge API Reference

This document describes the public service interfaces and data models used in ModbusForge.

## Table of Contents

- [Service Interfaces](#service-interfaces)
- [Data Models](#data-models)
- [Scripting Models](#scripting-models)
- [Configuration](#configuration)

---

## Service Interfaces

### IModbusService

The main interface for Modbus operations. Implemented separately for client and server modes.

```csharp
public interface IModbusService
{
    bool IsConnected { get; }
    string ServerAddress { get; }
    int Port { get; }
    byte UnitId { get; }

    Task ConnectAsync(string serverAddress, int port, byte unitId);
    Task DisconnectAsync();

    Task<ushort[]?> ReadHoldingRegistersAsync(byte unitId, int address, int count);
    Task<ushort[]?> ReadInputRegistersAsync(byte unitId, int address, int count);
    Task<bool[]?> ReadCoilsAsync(byte unitId, int address, int count);
    Task<bool[]?> ReadDiscreteInputsAsync(byte unitId, int address, int count);

    Task WriteSingleRegisterAsync(byte unitId, int address, ushort value);
    Task WriteSingleCoilAsync(byte unitId, int address, bool value);
    Task WriteMultipleRegistersAsync(byte unitId, int address, ushort[] values);
    Task WriteMultipleCoilsAsync(byte unitId, int address, bool[] values);
}
```

### IScriptRunner

Interface for executing Modbus scripts.

```csharp
public interface IScriptRunner
{
    bool IsRunning { get; }

    event EventHandler<ScriptExecutionEventArgs>? CommandExecuted;
    event EventHandler<string>? LogMessage;
    event EventHandler? ScriptStarted;
    event EventHandler<bool>? ScriptCompleted;

    Task RunScriptAsync(Script script, IModbusService modbusService, byte unitId, CancellationToken cancellationToken = default);
    void Stop();
}
```

### IConnectionManager

Manages connection profiles and active connections.

```csharp
public interface IConnectionManager
{
    ObservableCollection<ConnectionProfile> Profiles { get; }
    ConnectionProfile? ActiveProfile { get; }

    void AddProfile(string name, string address, int port, byte unitId);
    void UpdateProfile(ConnectionProfile profile);
    void DeleteProfile(ConnectionProfile profile);
    void SetActiveProfile(ConnectionProfile profile);
    Task ConnectAsync(ConnectionProfile profile);
    Task DisconnectAsync(ConnectionProfile profile);
    void SaveProfiles();
    void LoadProfiles();
}
```

---

## Data Models

### ConnectionProfile

Represents a saved Modbus connection.

```csharp
public class ConnectionProfile
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public int Port { get; set; } = 502;
    public byte UnitId { get; set; } = 1;
    public bool IsConnected { get; set; }
}
```

### CustomEntry

Represents a custom data monitoring entry.

```csharp
public class CustomEntry
{
    public string Area { get; set; } = "HoldingRegister";
    public int Address { get; set; } = 1;
    public string Type { get; set; } = "uint";
    public string Description { get; set; } = string.Empty;
    public bool Trend { get; set; }
    public bool Continuous { get; set; }
    public int PeriodMs { get; set; } = 1000;
    public string ValueText { get; set; } = string.Empty;
}
```

### TrendDataPoint

Represents a single trend data sample.

```csharp
public class TrendDataPoint
{
    public DateTime Timestamp { get; set; }
    public double Value { get; set; }
}
```

---

## Scripting Models

### Script

Container for a script sequence.

```csharp
public class Script
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "New Script";
    public string Description { get; set; } = string.Empty;
    public bool StopOnError { get; set; } = true;
    public int RepeatCount { get; set; } = 1;
    public int DelayBetweenCommandsMs { get; set; } = 100;
    public ObservableCollection<ScriptCommand> Commands { get; } = new();
}
```

### ScriptCommandType

Enumeration of supported script commands.

```csharp
public enum ScriptCommandType
{
    ReadHoldingRegisters,
    ReadInputRegisters,
    ReadCoils,
    ReadDiscreteInputs,
    WriteSingleRegister,
    WriteSingleCoil,
    Delay,
    Log,
    Loop
}
```

### ScriptCommand

Individual command in a script.

```csharp
public class ScriptCommand
{
    public ScriptCommandType CommandType { get; set; }
    public int Address { get; set; } = 1;
    public int Count { get; set; } = 1;
    public ushort Value { get; set; }
    public bool BoolValue { get; set; }
    public int DelayMs { get; set; } = 1000;
    public string Message { get; set; } = string.Empty;
    public int LoopCount { get; set; } = 1;
    public bool IsEnabled { get; set; } = true;
    public string LastResult { get; set; } = string.Empty;
    public bool LastSuccess { get; set; }
}
```

---

## Configuration

### appsettings.json

Main application configuration file.

```json
{
  "ServerSettings": {
    "Mode": "Client",
    "DefaultPort": 502,
    "DefaultUnitId": 1,
    "MaxConnections": 10
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning"
    }
  }
}
```

### Configuration Options

#### ServerSettings.Mode

- `Client`: Connect to existing Modbus TCP servers
- `Server`: Act as a Modbus TCP server

#### ServerSettings.DefaultPort

Default port for client connections or server listening.

#### ServerSettings.DefaultUnitId

Default Unit ID (slave ID) for client connections.

---

## Notes

- All service interfaces are registered in the DI container via `App.xaml.cs`
- Implementations are selected at runtime based on the configured mode
- The API is primarily used internally by the WPF UI; direct API usage is for advanced scenarios and plugin development
