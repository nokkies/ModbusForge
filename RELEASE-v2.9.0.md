# ModbusForge v2.9.0 Release Notes

**Release Date:** March 4, 2026  
**Type:** Convergence & Optimization Release

---

## 🎯 Overview

Version 2.9.0 marks a major milestone for ModbusForge, representing the convergence of all outstanding feature, performance, and security branches. This release significantly expands the testing infrastructure, optimizes asynchronous operations, and hardens the security of the application.

---

## ✨ What's New

### 🚀 Performance & Reliability

#### **Non-Blocking Mode Switching**
Refactored the mode switching logic (Client vs. Server) to be fully non-blocking, ensuring a smooth UI experience even under heavy load.

#### **Async Disconnect Optimizations**
Improved the reliability of asynchronous disconnections, ensuring all TCP resources are released gracefully and preventing "zombie" connections.

### 🛡️ Security Hardening

#### **Logging Sanitization**
Removed sensitive connection details and internal state values from the `ModbusService` logs to prevent accidental exposure of network configuration in log files.

#### **Insecure Network Binding Fix**
Updated `ModbusServerService` to use more secure default network binding options, reducing the attack surface for unauthorized connections.

### 🧪 MASSIVE Test Expansion

The unit test suite has been expanded from 20 to **56 passing tests**, covering:
- **Trend Logging Service**: Comprehensive verification of trend data publishing and processing.
- **Generic Helpers**: Refactored I/O operations to use shared generic helpers, all of which are now strictly verified.
- **Edge Case Coverage**: Added tests for empty display names, boundary register addresses, and rapid connection-switching scenarios.

---

## 🔧 Technical Improvements

### Code Refactoring
- **Generic I/O Helpers**: Introduced standardized patterns for Modbus read/write operations to reduce code duplication and improve maintainability.
- **Service Decoupling**: Completed the migration of all coordinators to the `IModbusService` interface.

---

## 📊 Metrics

### Build Status
- ✅ **Exit Code:** 0 (Success)
- ✅ **Tests:** 56 Passed, 0 Failed
- ✅ **Infrastructure**: Full convergence of all remote branches.

---

## 🔄 Breaking Changes

**None.** This release is fully backward compatible with the v2.8.x series.

---

**Previous Release:** [v2.8.2](RELEASE-v2.8.2.md)  
**GitHub:** https://github.com/nokkies/ModbusForge  
**Issues:** https://github.com/nokkies/ModbusForge/issues

---

**Thank you for using ModbusForge!** 🚀
