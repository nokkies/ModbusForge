# ModbusForge Troubleshooting Guide

This guide covers common issues and their solutions.

## Table of Contents

- [Connection Issues](#connection-issues)
- [Data Issues](#data-issues)
- [Performance Issues](#performance-issues)
- [Simulation Issues](#simulation-issues)
- [Application Issues](#application-issues)
- [Diagnostic Export](#diagnostic-export)
- [Getting More Help](#getting-more-help)

---

## Connection Issues

### "Unable to connect to server"

**Possible causes:**
- The IP address is incorrect
- The port is blocked by a firewall
- The Modbus server is not running
- The Unit ID is wrong

**Solutions:**
1. Verify the IP address with `ping <ip>`
2. Check that the port is open with `telnet <ip> <port>` or PowerShell `Test-NetConnection`
3. Confirm the Modbus server is running
4. Verify the Unit ID with the device documentation
5. Use the Connection Manager diagnostics
6. Export diagnostics from **Help → Troubleshooting**

### "Connection timeout"

**Possible causes:**
- Network latency is too high
- The device is not responding
- The port is wrong
- The Unit ID is wrong

**Solutions:**
1. Check network connectivity
2. Verify the server is responsive
3. Confirm the correct port
4. Try a different Unit ID
5. Check firewall rules

### "Access denied" or port 502 errors

On Windows, binding to port 502 or connecting to certain ports may require administrator privileges.

**Solutions:**
1. Run ModbusForge as administrator
2. Use a higher port number (e.g., 1502) that does not require elevation
3. Configure the device to use the same port

---

## Data Issues

### "All values are 0"

**Possible causes:**
- The starting address is wrong
- The Unit ID is wrong
- The device has no data at those addresses

**Solutions:**
1. Verify the starting address
2. Try reading a different address range
3. Confirm the Unit ID
4. Check the device documentation for valid address ranges

### "Write failed"

**Possible causes:**
- The device does not allow writes
- The address is read-only
- The data type is wrong

**Solutions:**
1. Verify the device allows writes
2. Confirm the address is writable
3. Check the data type (uint, int, real, string)
4. Ensure the value is within valid range

### "Value is not updating"

**Possible causes:**
- Continuous read is not enabled
- The polling interval is too long
- The device is not changing the value

**Solutions:**
1. Enable **Continuous Read**
2. Reduce the polling interval
3. Verify the device is actually updating the value

---

## Performance Issues

### "Application is slow"

**Possible causes:**
- Too many continuous reads running
- Large address counts
- Too many trend lines
- Short polling intervals

**Solutions:**
1. Reduce the number of continuous reads
2. Decrease the number of addresses being polled
3. Increase polling intervals
4. Close unused trend charts
5. Disable unnecessary logging

### "Trend chart is lagging"

**Possible causes:**
- Data retention window is too long
- Too many data points
- High-frequency polling

**Solutions:**
1. Reduce the retention window to 5-15 minutes
2. Remove unnecessary trend lines
3. Increase the polling interval

### "High memory usage"

**Possible causes:**
- Large data tables open
- Long trend history
- Memory leaks from previous versions

**Solutions:**
1. Reduce data retention
2. Close unused tabs
3. Restart the application periodically
4. Update to the latest version

---

## Simulation Issues

### "Simulation nodes are not updating"

**Possible causes:**
- The simulation is not running
- Nodes are not connected properly
- Signal generator is not configured

**Solutions:**
1. Click the **Run** button in the Simulation tab
2. Verify connections between nodes
3. Check signal generator parameters (amplitude, frequency, offset)

### "Cannot connect two nodes"

**Possible causes:**
- Incompatible data types
- Output is already connected
- The connection direction is wrong

**Solutions:**
1. Check that the data types are compatible
2. Remove existing connections first
3. Drag from output dot to input dot (not reverse)

### "Canvas is hard to navigate"

**Solutions:**
1. Use middle-mouse drag to pan
2. Use Ctrl+Scroll to zoom
3. Use the fit-to-view button if available
4. Use the search box to find nodes quickly

---

## Application Issues

### "Application crashes on startup"

**Possible causes:**
- Corrupted configuration files
- Missing .NET runtime
- Invalid appsettings.json

**Solutions:**
1. Check that .NET 8.0 runtime is installed
2. Verify `appsettings.json` is valid JSON
3. Delete or rename `%AppData%\ModbusForge\settings.json` to reset preferences
4. Export diagnostics and check the crash log

### "Settings are not saving"

**Possible causes:**
- The application does not have write access to AppData
- The settings file is corrupted

**Solutions:**
1. Check write permissions to `%AppData%\ModbusForge`
2. Delete the corrupted settings file
3. Restart the application

### "Window appears empty after closing a tab"

**Solutions:**
1. Click the corresponding item in the left navigation panel
2. In newer versions, the main tabs no longer have close buttons to prevent this issue

---

## Diagnostic Export

You can export diagnostic information to help with troubleshooting.

### How to Export

1. Go to **Help → Troubleshooting**
2. Click **Export Diagnostics**
3. Choose a save location
4. The exported file contains:
   - Application version
   - Operating system info
   - .NET framework version
   - Application paths
   - Configuration file info
   - Recent crash log entries

### What to Include in a Bug Report

When reporting an issue, please include:

1. ModbusForge version
2. Windows version
3. .NET version
4. Steps to reproduce
5. Expected behavior
6. Actual behavior
7. Diagnostic export file (if possible)
8. Screenshots (if applicable)

---

## Getting More Help

### In-App Help

- Press **F1** anywhere in the application for context-sensitive help
- Go to **Help → Help** for the full help index
- Go to **Help → Keyboard Shortcuts** for shortcut reference
- Go to **Help → Troubleshooting** for this guide

### Online Resources

- **GitHub Issues**: [https://github.com/nokkies/ModbusForge/issues](https://github.com/nokkies/ModbusForge/issues)
- **Email**: [skordonkels@gmail.com](mailto:skordonkels@gmail.com)

### Reporting Bugs

1. Check existing issues to avoid duplicates
2. Provide a clear title and description
3. Include steps to reproduce
4. Attach the diagnostic export file
5. Include screenshots if they help explain the issue
