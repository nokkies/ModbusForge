# ModbusForge v2.6.0 Release Notes

**Release Date:** January 11, 2026  
**Type:** Testing Infrastructure Release

---

## 🎯 Overview

Version 2.6.0 introduces comprehensive testing infrastructure for ModbusForge, establishing a foundation for quality assurance and preventing regressions in future releases. This release adds a complete test project with xUnit, Moq, and code coverage support.

---

## ✨ What's New

### 🧪 Testing Infrastructure

#### **ModbusForge.Tests Project**
A new test project has been created with full support for unit testing, integration testing, and code coverage.

**Framework & Tools:**
- **xUnit 2.9.2** - Modern, extensible test framework
- **Moq 4.20.72** - Powerful mocking framework for isolating dependencies
- **Microsoft.NET.Test.Sdk 17.12.0** - Test SDK for running tests
- **coverlet.collector 6.0.2** - Code coverage collection

**Project Structure:**
```
ModbusForge.Tests/
├── Coordinators/          # Unit tests for coordinator classes
│   ├── CoordinatorTestsBasic.cs
│   └── ConnectionCoordinatorTests.cs
├── Services/              # Unit tests for service classes (future)
├── Integration/           # Integration tests (future)
└── README.md             # Testing documentation
```

#### **Initial Test Suite**
Basic coordinator tests have been implemented to demonstrate the testing infrastructure:

**CoordinatorTestsBasic.cs** - 5 passing tests:
- `TrendCoordinator_GetTrendKey_ReturnsCorrectFormat` ✅
- `TrendCoordinator_GetTrendKey_HandlesNullArea` ✅
- `TrendCoordinator_GetTrendDisplayName_UsesNameWhenProvided` ✅
- `TrendCoordinator_GetTrendDisplayName_GeneratesNameWhenEmpty` ✅
- `TrendCoordinator_GetTrendDisplayName_HandlesNullName` ✅

**Test Coverage:**
- TrendCoordinator static methods: 100%
- Demonstrates AAA pattern (Arrange, Act, Assert)
- Shows proper null handling and edge cases

---

## 🔧 Technical Improvements

### Testing Best Practices

1. **AAA Pattern** - All tests follow Arrange-Act-Assert structure
2. **Descriptive Names** - Test names clearly describe what they test
3. **Focused Tests** - One assertion per test for clarity
4. **Edge Case Coverage** - Tests handle null values and empty strings

### Documentation

**README.md** in test project provides:
- Complete testing guide
- Running tests instructions
- Mocking strategies
- Best practices
- Coverage goals
- CI/CD integration notes
- Troubleshooting guide

### Running Tests

```bash
# Run all tests
dotnet test

# Run with detailed output
dotnet test --verbosity normal

# Run specific test class
dotnet test --filter "FullyQualifiedName~CoordinatorTestsBasic"

# Generate code coverage
dotnet test --collect:"XPlat Code Coverage"
```

---

## 📊 Metrics

### Test Results
- **Total Tests:** 5
- **Passed:** 5 ✅
- **Failed:** 0
- **Skipped:** 0
- **Duration:** ~1.3s

### Coverage Goals (Documented)
- **Coordinators:** 80%+ target
- **Services:** 70%+ target
- **ViewModels:** 60%+ target (UI-heavy code)

---

## 🐛 Bug Fixes

**None.** This release focuses on adding testing infrastructure without changing application functionality.

---

## 🔄 Breaking Changes

**None.** This is a pure testing infrastructure addition. All existing functionality is preserved.

---

## 📝 Known Issues

- Mocking concrete service classes (ModbusTcpService, ModbusServerService) requires interface-based approach
- Full coordinator test coverage pending (foundation established)
- UI automation tests not yet implemented

---

## 🚀 Upgrade Notes

### For Users
- No changes to application behavior
- All features work exactly as before

### For Developers
- New test project available for TDD
- Run `dotnet test` to execute all tests
- Add tests for new features before implementation
- Maintain coverage above documented thresholds

---

## 🎯 What's Next

### v2.7.0 (Planned)
- **Package Modernization**
  - Update LiveCharts to stable 2.0.0+
  - Remove PrivateAssets where possible
  - Address NU1701 warnings
  - Dependency cleanup and optimization

### Future Testing Enhancements
- [ ] Complete unit tests for all coordinators
- [ ] Service layer unit tests
- [ ] Integration tests for coordinator interactions
- [ ] UI automation tests (WPF UI Automation)
- [ ] Performance tests
- [ ] Load tests for server mode
- [ ] Mutation testing

---

## 📦 Installation

### Download
- **Installer:** `ModbusForge-2.6.0-setup.exe`
- **Portable:** `ModbusForge-2.6.0-win-x64.zip`
- **Self-Contained:** `ModbusForge-2.6.0-win-x64-sc.zip`

### Requirements
- Windows 10/11 (x64)
- .NET 8.0 Runtime (included in self-contained version)

---

## 🙏 Acknowledgments

This release establishes a solid testing foundation that will ensure code quality and prevent regressions as ModbusForge continues to evolve.

---

## 📄 Full Changelog

### Added
- ModbusForge.Tests project with xUnit and Moq
- CoordinatorTestsBasic test class with 5 passing tests
- ConnectionCoordinatorTests skeleton for future expansion
- Comprehensive testing README with best practices
- Code coverage support with coverlet.collector
- Test project targeting net8.0-windows

### Changed
- Version updated to 2.6.0
- Solution now includes test project

### Technical
- Version: 2.6.0
- Assembly version: 2.6.0.0
- File version: 2.6.0.0
- Test framework: xUnit 2.9.2
- Mocking framework: Moq 4.20.72

---

## 📊 Release Progress

| Version | Focus | Status |
|---------|-------|--------|
| v2.3.0 | Server freeze fix | ✅ Released |
| v2.4.0 | Coordinator pattern (3 coordinators) | ✅ Released |
| v2.5.0 | Coordinator pattern completion (5 coordinators) | ✅ Released |
| v2.6.0 | Testing infrastructure | ✅ Released |
| v2.7.0 | Package modernization | 🔄 Planned |

**Testing Foundation Established!** 🎉

---

**Previous Release:** [v2.5.0](RELEASE-v2.5.0.md)  
**GitHub:** https://github.com/nokkies/ModbusForge  
**Issues:** https://github.com/nokkies/ModbusForge/issues
