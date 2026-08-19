# ModbusForge Testing Infrastructure

## Overview

This test project provides unit and integration tests for ModbusForge using xUnit, Moq, and .NET 8.

## Test Structure

```
ModbusForge.Tests/
├── Fakes/                 # Fake implementations of IModbusService and other dependencies
├── Helpers/               # Test helpers (FlaUI app automation, etc.)
├── Integration/           # End-to-end integration tests
├── Performance/           # Polling throughput benchmarks
├── Services/              # Unit tests for Core services
├── SmokeTests/            # Avalonia smoke tests (excluded from default CI run)
└── README.md              # This file
```

## Running Tests

### Run all unit/integration/performance tests
```powershell
dotnet test ModbusForge.Tests/ModbusForge.Tests.csproj --filter "FullyQualifiedName!~UITests & FullyQualifiedName!~SmokeTests"
```

### Run with detailed output
```powershell
dotnet test --verbosity normal
```

### Run a specific test class
```powershell
dotnet test --filter "FullyQualifiedName~ConnectionManagerTests"
```

### Generate code coverage
```powershell
dotnet test --collect:"XPlat Code Coverage"
```

## Testing Framework

- **xUnit 2.9.3** - Test framework
- **Moq 4.20.72** - Mocking framework
- **Microsoft.NET.Test.Sdk 17.12.0** - Test SDK
- **coverlet.collector 6.0.2** - Code coverage

## Mocking Strategy

### Services
- Mock `IModbusService` for Modbus operations
- Mock `ILogger<T>` for logging
- Mock `IConsoleLoggerService` for console output
- Mock `ITrendLogger` for trend logging

### Example Mock Setup
```csharp
var mockService = new Mock<IModbusService>();
mockService.Setup(s => s.ConnectAsync(It.IsAny<string>(), It.IsAny<int>()))
    .ReturnsAsync(true);
```

## Best Practices

1. **AAA Pattern** - Arrange, Act, Assert
2. **One assertion per test** - Keep tests focused
3. **Descriptive names** - Test names describe what they test
4. **Mock only what you need** - Don't over-mock
5. **Test behavior, not implementation** - Focus on outcomes

## CI/CD Integration

Tests run automatically on:
- Pull requests
- Commits to `master`
- Release tags

## Troubleshooting

### Tests fail with "Could not find constructor"
- Ensure you're mocking interfaces, not concrete classes
- Check that all required dependencies are provided

### Tests timeout
- Increase timeout: `[Fact(Timeout = 5000)]`
- Check for async/await issues

### Coverage not generating
- Ensure coverlet.collector is installed
- Use `--collect:"XPlat Code Coverage"` flag

## Contributing

When adding new features:
1. Write tests first (TDD)
2. Ensure existing tests pass
3. Maintain coverage above thresholds
4. Update this README if needed
