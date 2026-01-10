# v2.4.0 Coordinator Integration Plan

## MainViewModel Changes Required

### 1. Add Coordinator Fields
```csharp
private readonly ConnectionCoordinator _connectionCoordinator;
private readonly RegisterCoordinator _registerCoordinator;
private readonly CustomEntryCoordinator _customEntryCoordinator;
```

### 2. Update Constructor
Add coordinator parameters and store them:
```csharp
public MainViewModel(
    ModbusTcpService clientService,
    ModbusServerService serverService,
    ILogger<MainViewModel> logger,
    IOptions<ServerSettings> options,
    ITrendLogger trendLogger,
    ISimulationService simulationService,
    ICustomEntryService customEntryService,
    IConsoleLoggerService consoleLoggerService,
    ConnectionCoordinator connectionCoordinator,  // ADD
    RegisterCoordinator registerCoordinator,      // ADD
    CustomEntryCoordinator customEntryCoordinator // ADD
)
```

### 3. Update Parameterless Constructor
Add coordinator resolution:
```csharp
App.ServiceProvider.GetRequiredService<ConnectionCoordinator>(),
App.ServiceProvider.GetRequiredService<RegisterCoordinator>(),
App.ServiceProvider.GetRequiredService<CustomEntryCoordinator>()
```

### 4. Replace Connection Methods
```csharp
// OLD:
private bool CanConnect() => !IsConnected;
private async Task ConnectAsync() { ... }
private async Task DisconnectAsync() { ... }
private async Task RunDiagnosticsAsync() { ... }

// NEW:
private bool CanConnect() => _connectionCoordinator.CanConnect(IsConnected);
private async Task ConnectAsync()
{
    await _connectionCoordinator.ConnectAsync(ServerAddress, Port, IsServerMode,
        msg => StatusMessage = msg, connected => IsConnected = connected);
}
private async Task DisconnectAsync()
{
    await _connectionCoordinator.DisconnectAsync(IsServerMode,
        msg => StatusMessage = msg, connected => IsConnected = connected);
}
private async Task RunDiagnosticsAsync()
{
    await _connectionCoordinator.RunDiagnosticsAsync(ServerAddress, Port, UnitId,
        msg => StatusMessage = msg);
}
```

### 5. Replace Register Methods
All register methods need `IsServerMode` parameter:
```csharp
private async Task ReadRegistersAsync()
{
    await _registerCoordinator.ReadRegistersAsync(UnitId, RegisterStart, RegisterCount, 
        RegistersGlobalType, HoldingRegisters, msg => StatusMessage = msg, 
        hasError => _hasConnectionError = hasError, HoldingMonitorEnabled, IsServerMode);
}

private async Task WriteRegisterAsync()
{
    await _registerCoordinator.WriteRegisterAsync(UnitId, WriteRegisterAddress, 
        WriteRegisterValue, msg => StatusMessage = msg, 
        async () => await ReadRegistersAsync(), IsServerMode);
}

// Similar for: ReadInputRegistersAsync, ReadCoilsAsync, ReadDiscreteInputsAsync, WriteCoilAsync
```

### 6. Replace Helper Methods
```csharp
public async Task WriteRegisterAtAsync(int address, ushort value)
{
    await _registerCoordinator.WriteRegisterAtAsync(UnitId, address, value, IsServerMode);
}

public async Task WriteFloatAtAsync(int address, float value)
{
    await _registerCoordinator.WriteFloatAtAsync(UnitId, address, value, IsServerMode);
}

public async Task WriteStringAtAsync(int address, string text)
{
    await _registerCoordinator.WriteStringAtAsync(UnitId, address, text, IsServerMode);
}

public async Task WriteCoilAtAsync(int address, bool state)
{
    await _registerCoordinator.WriteCoilAtAsync(UnitId, address, state, IsServerMode);
}
```

### 7. Replace Custom Entry Methods
```csharp
private void AddCustomEntry()
{
    _customEntryCoordinator.AddCustomEntry(CustomEntries);
}

private async Task WriteCustomNowAsync(CustomEntry entry)
{
    await _customEntryCoordinator.WriteCustomNowAsync(entry, UnitId, 
        msg => StatusMessage = msg, IsServerMode);
}

private async Task SaveCustomAsync()
{
    await _customEntryCoordinator.SaveCustomAsync(CustomEntries, 
        msg => StatusMessage = msg);
}

private async Task LoadCustomAsync()
{
    await _customEntryCoordinator.LoadCustomAsync(CustomEntries, 
        msg => StatusMessage = msg);
}
```

### 8. Update Dispose Method
Keep the existing timeout-based disconnect - it already works correctly.

## Testing Checklist
- [ ] Server mode: Start server on port 502
- [ ] Server mode: Read holding registers
- [ ] Server mode: Write holding registers
- [ ] Server mode: Enable simulation
- [ ] Client mode: Connect to server
- [ ] Client mode: Read/write operations
- [ ] Mode switching at runtime
- [ ] Custom entries read/write
- [ ] Graceful shutdown with server running
