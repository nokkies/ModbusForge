# ModbusForge v2.8.2 Release Notes

**Release Date:** March 4, 2026  
**Type:** Testing & Stability Release

---

## 🎯 Overview

Version 2.8.2 focuses on improving the robustness of the application by expanding the unit test suite. This release specifically addresses the core data conversion logic, ensuring that all Modbus data type transformations are accurate and verified.

---

## ✨ What's New

### 🧪 Expanded Test Coverage

#### **DataTypeConverter Tests**
Introduced a new comprehensive test suite for the `DataTypeConverter` class. 

- **Endianness Verification**: Confirmed that 16-bit to 32-bit (Single/Float) conversions handle register order correctly.
- **Signed/Unsigned Validation**: Added tests for `ushort` to `short` (Int16) conversions, including boundary cases and overflow protection.
- **Robustness**: Increased total test count to **20**, providing a stronger safety net for future performance optimizations.

---

## 🔧 Technical Improvements

### Infrastructure
- **Merged v2.8.1 Security Baseline**: All new tests are built upon the latest security-hardened foundation.
- **Consistent Test Environment**: Verified all 20 tests pass in a clean build environment.

---

## 📊 Metrics

### Build Status
- ✅ **Exit Code:** 0 (Success)
- ✅ **Tests:** 20 Passed, 0 Failed
- ✅ **New Coverage**: Significant increases in `ModbusForge.Helpers` coverage.

---

## 🔄 Breaking Changes

**None.** This release only adds unit tests and does not modify the application's runtime behavior.

---

**Previous Release:** [v2.8.1](RELEASE-v2.8.1.md)  
**GitHub:** https://github.com/nokkies/ModbusForge  
**Issues:** https://github.com/nokkies/ModbusForge/issues

---

**Thank you for using ModbusForge!** 🚀
