# ModbusForge User Guide

Comprehensive guide for using ModbusForge.

## Table of Contents

- [Getting Started](#getting-started)
- [Connection Manager](#connection-manager)
- [Working with Registers](#working-with-registers)
- [Custom Data Tab](#custom-data-tab)
- [Trend & Logging](#trend--logging)
- [Script Editor](#script-editor)
- [Visual Node Editor](#visual-node-editor)
- [Preferences](#preferences)
- [Keyboard Shortcuts](#keyboard-shortcuts)
- [Troubleshooting](#troubleshooting)

---

## Getting Started

### Quick Start (5 Minutes)

#### 1. Launch the Application

Double-click the ModbusForge icon or run from the command line:

```powershell
dotnet run --project ModbusForge
```

#### 2. Choose Your Mode

ModbusForge supports two modes:

- **Client Mode**: Connect to existing Modbus TCP servers
- **Server Mode**: Act as a Modbus TCP server for testing

The mode is configured in `appsettings.json` under `ServerSettings`:

```json
{
  "ServerSettings": {
    "Mode": "Client",
    "DefaultPort": 502,
    "DefaultUnitId": 1
  }
}
```

#### 3. Connect (Client Mode)

If in Client mode:

1. Enter the IP address of the Modbus server
2. Enter the port (default: 502)
3. Enter the Unit ID (slave ID)
4. Click **Connect**

#### 4. Start Server (Server Mode)

If in Server mode:

1. Configure the listening port (default: 502)
2. Configure allowed Unit IDs
3. Click **Start Server**

#### 5. Read Data

Once connected:

- Go to the **Registers** tab
- Enter the starting address and count
- Click **Read** to fetch data
- Enable **Continuous Read** for automatic polling

---

## Connection Manager

The Connection Manager allows you to save and manage multiple Modbus connection profiles.

### Access

Go to **Options → Connection Manager**.

### Creating Profiles

1. Click **Add Profile**
2. Enter a name for the connection
3. Configure:
   - IP Address
   - Port (default: 502)
   - Unit ID (slave ID)
4. Click **Save**

### Managing Profiles

- **Connect**: Click to connect using this profile
- **Disconnect**: Disconnect the current connection
- **Edit**: Modify profile settings
- **Delete**: Remove a profile
- **Set Active**: Make this the default connection

### Profile Storage

Profiles are saved to:

```
%AppData%\ModbusForge\connection-profiles.json
```

This means your profiles persist between sessions.

### Tips

- Give your profiles descriptive names (e.g., "PLC Line 1", "Test Server")
- Use different Unit IDs for different devices
- Test connections before saving profiles

---

## Working with Registers

### Reading Registers

1. Select the appropriate tab:
   - **Registers**: Holding Registers
   - **Input Registers**: Input Registers
   - **Coils**: Coils
   - **Discrete Inputs**: Discrete Inputs
2. Enter the starting address
3. Enter the count (number of items to read)
4. Click **Read**

### Continuous Read

Enable **Continuous Read** to automatically poll data at the configured interval. This is useful for monitoring live values.

### Writing Values

- **Double-click** a holding register or coil to open the Quick Write dialog
- Use the **Write** button in the toolbar for specific addresses

### Filtering

Use the **Filter** text box to search for addresses, values, or types in the DataGrid.

### Context Menu Actions

Right-click on a row to:

- **Quick Write**: Open the write dialog
- **Add to Custom Watch**: Add to the Custom Data tab
- **Add to Trend View**: Add to the trend chart
- **Copy Address**: Copy the address to clipboard
- **Copy Value**: Copy the value to clipboard

---

## Custom Data Tab

The Custom Data tab allows you to define custom register/coil configurations for monitoring and control.

### Adding Custom Entries

1. Click **Add Entry**
2. Configure:
   - **Area**: HoldingRegister, Coil, InputRegister, or DiscreteInput
   - **Address**: Register or coil address
   - **Type**: uint, int, real (float), or string
   - **Description**: Optional description

### Data Types

- **uint**: Unsigned integer (16-bit)
- **int**: Signed integer (16-bit)
- **real**: 32-bit floating point
- **string**: String data (multiple registers)

### Operations

- **Read Now**: Read the entry once
- **Write Now**: Write a value to the entry
- **Continuous Write**: Continuously write a value
- **Add to Trend**: Add entry to trend chart

### Continuous Read

When **Global Continuous Read** is enabled, all custom entries are read automatically at the configured interval.

### Save and Load

- **Save**: Save your custom entries to JSON
- **Load**: Load previously saved entries

---

## Trend & Logging

### Access

Click the **Trend** tab or use **Ctrl+T**.

### Adding Trend Lines

#### From Registers

1. Read registers to populate the data grid
2. Right-click a register row
3. Select **Add to Trend**

#### From Custom Entries

1. Go to the Custom Data tab
2. Right-click a custom entry
3. Select **Add to Trend**

### Trend Features

- **Real-Time Visualization**: Data updates automatically when continuous read is enabled
- **Multiple Trend Lines**: Multiple trend lines can be displayed simultaneously
- **Unique Colors**: Each line has a unique color

### Zoom and Pan

- **Scroll Wheel**: Zoom in/out
- **Click and Drag**: Pan the chart
- **Double-Click**: Reset zoom to fit all data

### Data Retention

Configure how long data is kept:

- Range: 1 to 60 minutes
- Older data is automatically discarded

### Export

- **Export to CSV**: Export trend data to CSV file
- **Export to PNG**: Save the current chart as an image

---

## Script Editor

The Script Editor allows you to create automated test sequences for Modbus operations.

### Access

Go to **Options → Script Editor** or press **Ctrl+E**.

### Supported Commands

#### Read Operations

- **Read Holding Registers**: Read from holding registers
- **Read Input Registers**: Read from input registers
- **Read Coils**: Read coil states
- **Read Discrete Inputs**: Read discrete input states

#### Write Operations

- **Write Single Register**: Write to a holding register
- **Write Coil**: Set coil state

#### Control Commands

- **Delay**: Wait for specified milliseconds
- **Log**: Add a message to the output log

### Script Settings

#### Repeat Count

Number of times to repeat the entire script.

#### Delay Between Commands

Milliseconds to wait between each command execution.

#### Stop on Error

If enabled, the script stops when an error occurs.

### Example Script

```
1. Read Holding Registers (Address: 0, Count: 10)
2. Delay (100ms)
3. Write Single Register (Address: 0, Value: 100)
4. Delay (50ms)
5. Read Holding Registers (Address: 0, Count: 10)
6. Log ("Test complete")
```

### Saving and Loading

- **Save**: Save your script to a `.mbscript` file
- **Load**: Load a previously saved script

### Keyboard Shortcuts

- **Ctrl+N**: New script
- **Ctrl+O**: Open script
- **Ctrl+S**: Save script
- **Ctrl+E**: Execute script

---

## Visual Node Editor

The Visual Node Editor provides a graphical interface for creating Modbus simulations.

### Access

Go to the **Simulation** tab.

### Overview

The Visual Node Editor allows you to:

- Create visual simulation programs
- Connect nodes to define data flow
- Generate waveforms and patterns
- Simulate PLC behavior

### Interface

#### Palette (Left Panel)

Contains available nodes organized by category:

- **I/O**: Input/output nodes
- **Sources**: Signal generators, constants
- **Math**: Mathematical operations
- **Logic**: Boolean operations
- **Transform**: Data conversions

#### Canvas (Center)

Drag nodes from the palette to the canvas. Connect nodes by dragging from output dots to input dots.

#### Properties Panel (Right)

Configure selected node parameters.

### Keyboard Shortcuts

#### Editor Operations

- **Ctrl+Z**: Undo last action
- **Ctrl+Y**: Redo
- **Delete**: Delete selected node
- **Esc**: Cancel operation / Clear search

#### Canvas Navigation

- **Scroll Wheel**: Scroll vertically
- **Shift+Scroll**: Scroll horizontally
- **Ctrl+Scroll**: Zoom in/out
- **Middle Mouse Drag**: Pan canvas
- **Left Click (on empty space)**: Pan canvas

#### Connections

- **Right-click wire**: Delete connection

### Node Types

#### Signal Generator

Generate standard waveforms:

- Ramp
- Sine
- Triangle
- Square

Configure amplitude, frequency, and offset.

#### Constants

Fixed values for testing.

#### Math Nodes

Perform mathematical operations on signals.

### Tips

- Use the search box in the palette to quickly find nodes
- Double-click a node to select it
- Hover over connectors to see valid connections
- Green connector = valid connection
- Red connector = invalid connection

---

## Preferences

Configure ModbusForge behavior to suit your needs.

### Access

Go to **Options → Preferences**.

### Settings

#### Connection

- **Auto-reconnect**: Automatically reconnect on connection loss
- **Show diagnostics on error**: Display diagnostic dialog on connection errors

#### Console Logging

- **Enable console logging**: Log Modbus operations to console tab
- **Log level**: Detail level of logging (Info, Warning, Error)

#### Behavior

- **Confirm before exit**: Show confirmation dialog when closing the application

### Storage

Settings are saved to:

```
%AppData%\ModbusForge\settings.json
```

### Tips

- Enable auto-reconnect for unstable networks
- Use detailed logging for troubleshooting
- Disable confirm exit for faster workflow

---

## Keyboard Shortcuts

### Global Shortcuts

| Shortcut | Action |
|----------|--------|
| Ctrl+R | Read registers |
| Ctrl+T | Open trends |
| Ctrl+S | Save project |
| Ctrl+O | Open project |
| Ctrl+N | New project |
| Ctrl+E | Open script editor |
| F5 | Refresh data |
| F1 | Open help |

### Script Editor

| Shortcut | Action |
|----------|--------|
| Ctrl+N | New script |
| Ctrl+O | Open script |
| Ctrl+S | Save script |
| Ctrl+E | Execute script |

### Visual Node Editor

| Shortcut | Action |
|----------|--------|
| Ctrl+Z | Undo last action |
| Ctrl+Y | Redo |
| Ctrl+Shift+Z | Redo (alternate) |
| Delete | Delete selected node |
| Esc | Cancel operation / Clear search |
| Right-click wire | Delete connection |
| Scroll Wheel | Scroll vertically |
| Shift+Scroll | Scroll horizontally |
| Ctrl+Scroll | Zoom in/out |
| Middle Drag | Pan canvas |

---

## Troubleshooting

See [TROUBLESHOOTING.md](TROUBLESHOOTING.md) for detailed troubleshooting guidance.

For additional help, press **F1** anywhere in the application for context-sensitive help.
