# ModbusForge Testing Infrastructure

## Overview

This test project provides comprehensive testing for ModbusForge using xUnit, Moq, and .NET 8.0.

## Test Structure

```
ModbusForge.Tests/
├── Coordinators/          # Unit tests for coordinator classes
│   ├── ConnectionCoordinatorTests.cs
│   ├── RegisterCoordinatorTests.cs
│   ├── CustomEntryCoordinatorTests.cs
│   ├── TrendCoordinatorTests.cs
│   └── ConfigurationCoordinatorTests.cs
├── Services/              # Unit tests for service classes
├── Integration/           # Integration tests
└── README.md             # This file
```

## Running Tests

### Run all tests
```bash
dotnet test
```

### Run with detailed output
```bash
dotnet test --verbosity normal
```

### Run specific test class
```bash
dotnet test --filter "FullyQualifiedName~ConnectionCoordinatorTests"
```

### Generate code coverage
```bash
dotnet test --collect:"XPlat Code Coverage"
```

## Testing Framework

- **xUnit 2.9.2** - Test framework
- **Moq 4.20.72** - Mocking framework
- **Microsoft.NET.Test.Sdk 17.12.0** - Test SDK
- **coverlet.collector 6.0.2** - Code coverage

## Test Categories

### Unit Tests
Test individual components in isolation using mocks for dependencies.

**Example:**
```csharp
[Fact]
public void CanConnect_WhenNotConnected_ReturnsTrue()
{
    // Arrange
    bool isConnected = false;

    // Act
    var result = _coordinator.CanConnect(isConnected);

    // Assert
    Assert.True(result);
}
```

### Integration Tests
Test coordinator interactions and end-to-end workflows.

**Example:**
```csharp
[Fact]
public async Task CompleteReadWriteCycle_Success()
{
    // Test full read/write cycle with real services
}
```

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

## Coverage Goals

- **Coordinators**: 80%+ coverage
- **Services**: 70%+ coverage
- **ViewModels**: 60%+ coverage (UI-heavy code)

## CI/CD Integration

Tests run automatically on:
- Pull requests
- Commits to main branch
- Release tags

## Future Enhancements

- [ ] UI automation tests (WPF UI Automation)
- [ ] Performance tests
- [ ] Load tests for server mode
- [ ] End-to-end scenario tests
- [ ] Mutation testing

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
