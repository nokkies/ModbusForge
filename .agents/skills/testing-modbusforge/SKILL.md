---
name: testing-modbusforge
description: Build, run, and end-to-end test the ModbusForge WPF app locally. Use when verifying UI/ViewModel changes (registers grid, connect/disconnect, Unit ID switching, custom watch, trends).
---

# Testing ModbusForge (WPF, net8.0-windows)

## Build & unit tests
```powershell
dotnet build ModbusForge.sln
dotnet test ModbusForge.Tests/ModbusForge.Tests.csproj --filter "FullyQualifiedName!~UITests & FullyQualifiedName!~SmokeTests" --no-build
```
The `UITests`/`SmokeTests` filters exclude FlaUI automation that needs a running app; the remaining ~355 unit tests run headless. One test (`DisposeAsync_DoesNotBlockCallingThread_WhenLockIsHeld`) can be timing-flaky — rerun once before treating a failure as real.

## Running the app for GUI testing
Built exe: `ModbusForge\bin\Debug\net8.0-windows\ModbusForge.exe`.

If the .NET SDK is installed to a non-default location (e.g. `C:\dotnet` via dotnet-install.ps1), the apphost won't find the WindowsDesktop runtime and shows a "To run this application, you must install .NET Desktop Runtime" dialog. Fix by pointing the apphost at the SDK's bundled runtime:
```powershell
$env:DOTNET_ROOT="C:\dotnet"; $env:PATH="C:\dotnet;"+$env:PATH
Start-Process ".\ModbusForge\bin\Debug\net8.0-windows\ModbusForge.exe" -WorkingDirectory "$HOME\repos\ModbusForge"
```
(If the standard `C:\Program Files\dotnet` runtime is present, no DOTNET_ROOT is needed.) Maximize the window before recording via the Win32 `ShowWindow(handle, 3)` call on the process `MainWindowHandle`.

## No external hardware needed: use Server mode
ModbusForge can self-host a Modbus server, so you can test client-style read/write against its own datastore:
1. Set the top **Mode** dropdown to `Server`.
2. (Optional) set **Unit ID(s)** to a comma list like `1,2` to test multiple slave IDs. This field is disabled while connected — set it before starting.
3. Click **Start Server** (the Connect button relabels to this in Server mode). Status bar shows "Server started ..." / "Connected".
4. Go to the **Registers** tab, click **Read** — Holding Registers populate (default seed values 10,20,...,100).

## Key flows & how to verify them
- **Holding register write via grid edit**: double-click a Value cell, type a new number, press Enter. The cell-edit handler writes to the datastore and auto-triggers a re-read. To prove the write really happened, click **Read** again — the new value must persist (if it reverts, the write path is broken).
- **Connect/Disconnect state**: the Connect/ServerAddress/UnitId fields are disabled (`IsEnabled` bound to `IsConnected`) while connected; Disconnect re-enables them.
- **Unit ID switching**: the **Active ID** dropdown (Server mode) switches the current slave config; switching triggers a fresh read of that unit's registers. Verify no crash/hang and the grid refreshes.

## Known quirks (not necessarily bugs)
- Opening the **Active ID** dropdown can briefly flash a red `Value '' could not be converted.` validation border on the combo box — a transient binding quirk, the switch still works.

## Devin Secrets Needed
None — fully local; no credentials or network services required.
