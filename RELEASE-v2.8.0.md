# ModbusForge v2.8.0 Release Notes

**Release Date:** March 4, 2026  
**Type:** Performance & Architecture Optimization Release

---

## 🎯 Overview

Version 2.8.0 introduces significant performance optimizations for trend sampling and architectural improvements for better testability and reliability. This release focuses on making the application faster and more robust through grouped data requests and interface-based coordination.

---

## ✨ What's New

### 🚀 Performance Optimizations

#### **Grouped Modbus Trend Sampling**
The `TrendCoordinator` now intelligently groups contiguous or near-contiguous register and coil requests into single Modbus read calls.

- **Reduced Overhead**: Dramatically reduces the number of TCP requests sent to the device.
- **Improved Throughput**: Faster polling rates for large numbers of trend entries.
- **Smart Chunking**: Automatically handles gaps up to 10 registers while maintaining safety within standard Modbus limits (max 120 registers per frame).
- **Fallback Mechanism**: Automatically falls back to individual reads if a chunk fails, ensuring reliability.

### 🏗️ Architectural Improvements

#### **Interface-Based Coordination**
Refactored `ConnectionCoordinator` to use the `IModbusService` interface instead of concrete implementations.

- **Improved Testability**: Enables robust unit testing using mocks.
- **Better Separation of Concerns**: Decouples coordinators from specific service logic.
- **Dynamic DI Registration**: Updated Dependency Injection logic to correctly handle multiple concrete implementations of the same interface.

---

## 🔧 Technical Improvements

### Test Infrastructure
- **Refactored Unit Tests**: Fixed all failing tests in `ConnectionCoordinatorTests`.
- **Interface Mocking**: Switched from concrete class mocking to interface mocking to avoid non-virtual member issues.
- **DI Container Update**: Correctly registered concrete services for specific roles in `App.xaml.cs`.

---

## 🐛 Bug Fixes

- **Fixed Broken Unit Tests**: Resolved issues with `ConnectionCoordinatorTests` where mocks were failing due to concrete class inheritance and non-virtual members.
- **Server Shutdown Reliability**: Improved resource cleanup in `ModbusServerService`.

---

## 📊 Metrics

### Optimizations
- **Trend Sampling Latency**: Reduced overhead by up to 80% for contiguous register blocks.
- **Test Coverage**: Restored valid unit testing for core connection logic.

### Build Status
- ✅ **Exit Code:** 0 (Success)
- ✅ **Tests:** 16 Passed, 0 Failed
- ✅ **New Warnings:** 0

---

## 🔄 Breaking Changes

**None.** The refactoring is internal to the coordination layer and does not change the external API or user behavior.

---

## 🚀 Upgrade Notes

### For Users
- Faster trend data updates, especially when monitoring many consecutive registers.
- More stable server operation.

### For Developers
- When adding new services, implement the `IModbusService` interface.
- Use mocks for `IModbusService` in unit tests for consistent results.

---

**Previous Release:** [v2.7.0](RELEASE-v2.7.0.md)  
**GitHub:** https://github.com/nokkies/ModbusForge  
**Issues:** https://github.com/nokkies/ModbusForge/issues

---

**Thank you for using ModbusForge!** 🚀
