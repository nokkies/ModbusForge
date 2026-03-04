# ModbusForge v2.8.1 Release Notes

**Release Date:** March 4, 2026  
**Type:** Security Patch Release

---

## 🎯 Overview

Version 2.8.1 is a critical security patch that addresses a potential Denial of Service (DoS) vulnerability in the configuration loading mechanism. This release also ensures all unit tests are fully compatible with the architectural improvements introduced in v2.8.0.

---

## 🛡️ Security Fixes

### **Denial of Service (DoS) Prevention**
Fixed a vulnerability in `ConfigurationCoordinator` where reading excessively large or malformed configuration files could lead to application hangs or memory exhaustion.

- **File Size Limits**: Implemented strict validation of configuration file sizes before processing.
- **Improved Parsing**: Enhanced the robustness of the JSON parsing logic to better handle unexpected or malicious input structures.

---

## 🔧 Technical Improvements

### Test Compatibility
- **Unit Test Alignment**: Integrated the `IModbusService` refactoring into the `ConfigurationCoordinator` tests, ensuring the test suite remains valid and passing for all components.
- **Service Integration**: Verified that the security fix does not impact the performance of Modbus server/client operations.

---

## 🐛 Bug Fixes

- **Test Suite Restoration**: Fixed errors in `ConfigurationCoordinatorTests` that were causing build failures in the security branch.

---

## 📊 Metrics

### Build Status
- ✅ **Exit Code:** 0 (Success)
- ✅ **Tests:** All tests passed
- ✅ **Security Impact**: Critical vulnerability resolved

---

## 🔄 Breaking Changes

**None.** This is a security patch and maintains full backward compatibility with v2.8.0 configuration formats.

---

**Previous Release:** [v2.8.0](RELEASE-v2.8.0.md)  
**GitHub:** https://github.com/nokkies/ModbusForge  
**Issues:** https://github.com/nokkies/ModbusForge/issues

---

**Thank you for using ModbusForge!** 🚀
