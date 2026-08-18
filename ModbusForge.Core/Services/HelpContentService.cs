using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace ModbusForge.Services
{
    public class HelpContentService : IHelpContentService
    {
        private readonly ILogger<HelpContentService> _logger;
        private readonly Dictionary<string, string> _helpContent;

        public HelpContentService(ILogger<HelpContentService> logger)
        {
            _logger = logger;
            _helpContent = InitializeHelpContent();
        }

        public string? GetHelpContent(string topicId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(topicId))
                {
                    return GetErrorContent();
                }

                if (!_helpContent.TryGetValue(topicId, out var content) || string.IsNullOrWhiteSpace(content))
                {
                    return GetNotFoundContent(topicId);
                }

                return content;
            }
            catch (System.Exception ex) when (ex is not (System.OutOfMemoryException or System.OperationCanceledException))
            {
                _logger.LogError(ex, "Failed to load help content for topic: {TopicId}", topicId);
                return GetErrorContent();
            }
        }

        public bool HasTopic(string topicId)
        {
            return _helpContent.ContainsKey(topicId);
        }

        private Dictionary<string, string> InitializeHelpContent()
        {
            return new Dictionary<string, string>
            {
                ["getting-started"] = GetGettingStartedContent(),
                ["connection-manager"] = GetConnectionManagerContent(),
                ["device-scanner"] = GetDeviceScannerContent(),
                ["script-editor"] = GetScriptEditorContent(),
                ["custom-data"] = GetCustomDataContent(),
                ["trends"] = GetTrendsContent(),
                ["visual-editor"] = GetVisualEditorContent(),
                ["preferences"] = GetPreferencesContent(),
                ["mcp-server"] = GetMcpServerContent(),
                ["keyboard-shortcuts"] = GetKeyboardShortcutsContent(),
                ["partial-reads"] = GetPartialReadsContent(),
                ["troubleshooting"] = GetTroubleshootingContent()
            };
        }

        private string GetGettingStartedContent()
        {
            return @"# Getting Started with ModbusForge

Welcome to ModbusForge! This guide will help you get up and running quickly.

## Quick Start (5 Minutes)

### 1. Launch the Application
Double-click the ModbusForge icon to launch the application.

### 2. Choose Your Mode
ModbusForge supports two modes:
- **Client Mode**: Connect to existing Modbus TCP servers
- **Server Mode**: Act as a Modbus TCP server for testing

The mode is configured in `appsettings.json` under `ServerSettings`.

### 3. Connect (Client Mode)
If in Client mode:
1. Enter the IP address of the Modbus server
2. Enter the port (default: 502)
3. Enter the Unit ID (slave ID)
4. Click ""Connect""

### 4. Start Server (Server Mode)
If in Server mode:
1. Configure the listening port (default: 502)
2. Configure allowed Unit IDs
3. Click ""Start Server""

### 5. Read Data
Once connected:
- Go to the ""Holding Registers"" tab
- Enter the starting address and count
- Click ""Read"" to fetch data
- Enable ""Continuous Read"" for automatic polling

## Next Steps
- Explore the Connection Manager to save connection profiles
- Try the Script Editor for automated testing
- Use the Visual Node Editor for simulation
- Check the Keyboard Shortcuts (F1 or Help menu)

## Need More Help?
- Press F1 anywhere for context-sensitive help
- See the Troubleshooting section for common issues
- Visit https://github.com/nokkies/ModbusForge for more resources";
        }

        private string GetConnectionManagerContent()
        {
            return @"# Connection Manager

The Connection Manager allows you to save and manage multiple Modbus connection profiles.

## Access
Go to **Options → Connection Manager** or press the connection button in the toolbar.

## Features

### Creating Profiles
1. Click ""Add Profile""
2. Enter a name for the connection
3. Configure:
   - IP Address
   - Port (default: 502)
   - Unit ID (slave ID)
4. Click ""Save""

### Managing Profiles
- **Connect**: Click to connect using this profile
- **Disconnect**: Disconnect the current connection
- **Edit**: Modify profile settings
- **Delete**: Remove a profile
- **Set Active**: Make this the default connection

### Profile Storage
Profiles are saved to:
`%AppData%\ModbusForge\connection-profiles.json`

This means your profiles persist between sessions.

## Tips
- Give your profiles descriptive names (e.g., ""PLC Line 1"", ""Test Server"")
- Use different Unit IDs for different devices
- Test connections before saving profiles";
        }

        private string GetDeviceScannerContent()
        {
            return @"# Device Scanner

Discover Modbus TCP devices on your network without touching your live connections.
Open it from **Options > Device Scanner...**.

## Defining the scan

- **Start IP / End IP**: inclusive IPv4 range (maximum 4096 addresses).
- **Port from / Port to**: single port (502) or a range, up to 64 ports.
- **Unit ID from / to**: any subset of 1-247.
- **Register type / Probe address**: the read used to detect a unit; a unit is reported
  as found when it answers, including when it answers with a Modbus exception.
- **Connect / Response (ms)**: per-endpoint timeouts. Lower values scan faster but can
  miss slow gateways.
- **Parallel hosts**: how many endpoints are probed at the same time.
- **Read device identification (FC43)**: asks each discovered unit for vendor name,
  product code and revision.
- **Detect function codes (FC01-FC04)**: reads one item from each register space to work
  out which read functions the unit implements. A unit that replies *illegal data address*
  still implements the function; only *illegal function* excludes it.
- **Scan register range**: additionally reads a block of addresses from each discovered
  unit and lists which are readable.

## Working with results

- The **Function codes** column lists the detected read functions, for example `FC03, FC04`.
- **Add to Profiles** saves the selected device as a connection profile so it appears in
  the Connection Manager.
- **Export CSV** writes one row per device plus one row per scanned register.
- **Stop** cancels a running scan; results already found are kept.

## Tips

- Serial gateways often expose several unit IDs behind one IP; scan the full 1-247 range
  when you are unsure.
- A `NoTcpConnection` result means nothing answered on the port; `NoModbusResponse` means
  the port was open but the unit stayed silent.
- Scanning is deliberately capped to avoid flooding a production network. Narrow the IP or
  port range if the scanner reports too many endpoints.";
        }

        private string GetScriptEditorContent()
        {
            return @"# Script Editor

The Script Editor allows you to create automated test sequences for Modbus operations.

## Access
Go to **Options → Script Editor**

## Supported Commands

### Read Operations
- **Read Holding Registers**: Read from holding registers
- **Read Input Registers**: Read from input registers
- **Read Coils**: Read coil states
- **Read Discrete Inputs**: Read discrete input states

### Write Operations
- **Write Single Register**: Write to a holding register
- **Write Coil**: Set coil state

### Control Commands
- **Delay**: Wait for specified milliseconds
- **Log**: Add a message to the output log
- **Loop**: Repeat the rest of the script (every command after the Loop row) the number of times in the **Loops** column. The Loop consumes the rest of the script, so those commands only run inside the loop. Nested loops are not supported.

## Script Settings

### Repeat Count
Number of times to repeat the entire script.

### Delay Between Commands
Milliseconds to wait between each command execution.

### Stop on Error
If enabled, the script stops when an error occurs.

## Command Grid

Each command row has one column per property; most commands only use a few of them. **Hover a column header** to see what that column controls, and **drag the edge of a header** to resize the column so the labels fit.

## Example Script
```
1. Read Holding Registers (Address: 0, Count: 10)
2. Delay (100ms)
3. Write Single Register (Address: 0, Value: 100)
4. Delay (50ms)
5. Read Holding Registers (Address: 0, Count: 10)
6. Log (""Test complete"")
```

## Saving and Loading
- **Save**: Save your script to a `.mbscript` file
- **Load**: Load a previously saved script

## Keyboard Shortcuts
- **Ctrl+N**: New script
- **Ctrl+O**: Open script
- **Ctrl+S**: Save script
- **Ctrl+E**: Execute script";
        }

        private string GetCustomDataContent()
        {
            return @"# Custom Data Tab

The Custom Data tab allows you to define custom register/coil configurations for monitoring and control.

## Features

### Adding Custom Entries
1. Click ""Add Entry""
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
- **Add to Trend**: Add a trend pen that polls the entry's address

### Continuous Read
When ""Global Continuous Read"" is enabled, all custom entries are read automatically at the configured interval.

### Save and Load
- **Save**: Save your custom entries to JSON
- **Load**: Load previously saved entries

## Tips
- Use descriptive names for easy identification
- Group related entries together
- Use the trend feature to monitor values over time";
        }

        private string GetTrendsContent()
        {
            return @"# Trend & Logging

The Trends tab plots live data as named **pens**. Each pen polls one register (or coil) address - or mirrors an existing tag - at its own read period while a connection is active. Pens are stored per unit and persist with the project; they are independent of Custom Watch entries.

## Access
Go to the **Trends** tab (or press Ctrl+T).

## Adding Pens

### From the Add dialog
1. Click **Add** in the pen list
2. Choose the source: **Register** (area + address) or **Tag**
3. Optionally set a name and read period, then click OK

### From a register or custom entry
1. Read registers to populate the data grid, or go to the Custom Data tab
2. Right-click a register row or a custom entry
3. Select ""Add to Trend""

A pen appears in the pen list immediately; its line draws as soon as the first samples are read.

## Pen List

The pen list on the right shows every pen:

- **Rename** inline - updates the chart legend and is saved with the unit configuration. The pen keeps its series history: only the label changes, the data line is untouched
- **Click the swatch** to cycle the pen's color
- **Eye** toggles the pen's line on the chart
- **Red dot** - the pen's reads are failing; hover the dot for the last error. Failing pens keep retrying every cycle and recover on their own when reads succeed again
- **✕** removes the pen (the trend data is dropped; Custom Watch entries are untouched)

**Clear** in the Data group removes all pens at once.

## Trend Features

### Real-Time Visualization
- Pens are polled automatically while a connection is active - no continuous read setting required
- Multiple pens can be displayed simultaneously
- Each pen has a unique color

### Zoom and Pan
- **Scroll Wheel**: Zoom in/out
- **Click and Drag**: Pan the chart
- **Reset** button: Reset zoom to fit all data

### Data Retention
Configure how long data is kept:
- Range: 1 to 60 minutes
- Older data is automatically discarded

### Import and Export
- **Export CSV**: Export the selected pen, or all pens if none is selected
- **Import CSV**: Plot a previously exported capture (e.g. historical data)
- **Export PNG**: Save the current chart as an image

## Tips
- Use descriptive pen names - they are the series keys in exports
- Limit the number of pens for better performance
- Adjust retention based on your monitoring needs";
        }

        private string GetVisualEditorContent()
        {
            return @"# Visual Node Editor

The Visual Node Editor provides a graphical interface for creating Modbus simulations.

## Access
Go to the **Simulation** tab

## Overview
The Visual Node Editor allows you to:
- Create visual simulation programs
- Connect nodes to define data flow
- Generate waveforms and patterns
- Simulate PLC behavior

## Interface

### Palette (Left Panel)
Contains available nodes organized by category:
- **I/O**: Input/output nodes
- **Sources**: Signal generators, constants
- **Math**: Mathematical operations
- **Logic**: Boolean operations
- **Signal Conditioning**: scale, edge detect, moving average
- **Transform**: Data conversions

### Canvas (Center)
Drag nodes from the palette to the canvas. Connect nodes by dragging from output dots to input dots.

### Properties Panel (Right)
Configure selected node parameters.

## Block Problems
While the simulation runs, a block that cannot produce fresh output is marked with a
red border and a red dot in its top-right corner; hover the dot for the reason:
- **Evaluation failed** — the block threw while computing (for example, a Boolean
  input bound to a register that holds a non-boolean value). The block keeps its
  last outputs and retries every cycle; the marker clears as soon as it runs cleanly.
- **Locked in a loop** — blocks that form a cycle are excluded from the execution
  order entirely and are marked this way. Break the loop to make them run again.
When the simulation stops, all markers are cleared.

## Keyboard Shortcuts

### Editor Operations
- **Ctrl+Z**: Undo last action
- **Ctrl+Y**: Redo
- **Delete**: Delete selected node
- **Esc**: Cancel operation / Clear search

### Canvas Navigation
- **Scroll Wheel**: Scroll vertically
- **Shift+Scroll**: Scroll horizontally
- **Ctrl+Scroll**: Zoom in/out
- **Middle Mouse Drag**: Pan canvas
- **Left Click (on empty space)**: Pan canvas

### Connections
- **Right-click wire**: Delete connection

## Node Types

### Signal Generator
Generate standard waveforms:
- Ramp
- Sine
- Triangle
- Square

Configure amplitude, frequency, and offset.

### Constants
Fixed values for testing.

### Math Nodes
Perform mathematical operations on signals.

### Signal Conditioning
- **Scale (LIN)**: linearly maps an analog value from one range to another (e.g. a 0..100 raw register to 0..120 °C). Configure the From/To ranges; when Clamp is on, results stay inside the To range.
- **Edge Detect**: emits a single-cycle pulse on the selected transition (Rising or Falling) of a Boolean input — useful for triggering timers or counters from noisy or held signals.
- **Moving Average (MAVG)**: smooths an analog signal by averaging the last N samples (window 1..1024). The window fills gradually after startup.

## Tips
- Use the search box in the palette to quickly find nodes
- Double-click a node to select it
- Hover over connectors to see valid connections
- Green connector = valid connection
- Red connector = invalid connection";
        }

        private string GetPreferencesContent()
        {
            return @"# Preferences

Configure ModbusForge behavior to suit your needs.

## Access
Go to **Options → Preferences**

## Settings

### Connection
- **Auto-reconnect**: Automatically reconnect on connection loss
- **Show diagnostics on error**: Display diagnostic dialog on connection errors

### Console Logging
- **Enable console logging**: Log Modbus operations to console tab
- **Log level**: Detail level of logging (Info, Warning, Error)

### Behavior
- **Confirm before exit**: Show confirmation dialog when closing the application

### Storage
Settings are saved to:
`%AppData%\ModbusForge\settings.json`

## Tips
- Enable auto-reconnect for unstable networks
- Use detailed logging for troubleshooting
- Disable confirm exit for faster workflow";
        }

        private string GetKeyboardShortcutsContent()
        {
            return @"# Keyboard Shortcuts

Master these shortcuts to work more efficiently.

## Global Shortcuts

### Main Application
- **Ctrl+R**: Read registers
- **Ctrl+T**: Open trends
- **Ctrl+S**: Save project
- **F5**: Refresh data
- **F1**: Open help

### File Operations
- **Ctrl+O**: Open project
- **Ctrl+N**: New project
- **Ctrl+W**: Close current tab

## Visual Node Editor

### Editor Operations
- **Ctrl+Z**: Undo last action
- **Ctrl+Y**: Redo
- **Ctrl+Shift+Z**: Redo (alternate)
- **Delete**: Delete selected node
- **Esc**: Cancel operation / Clear search

### Canvas Navigation
- **Scroll Wheel**: Scroll vertically
- **Shift+Scroll**: Scroll horizontally
- **Ctrl+Scroll**: Zoom in/out
- **Middle Mouse Drag**: Pan canvas
- **Left Click (on empty space)**: Pan canvas

### Connections
- **Right-click wire**: Delete connection

## Script Editor
- **Ctrl+N**: New script
- **Ctrl+O**: Open script
- **Ctrl+S**: Save script
- **Ctrl+E**: Execute script

## Tips
- Press F1 anywhere for context-sensitive help
- Use the Keyboard Shortcuts window (Help menu) for a printable reference";
        }

        private string GetTroubleshootingContent()
        {
            return @"# Troubleshooting

Common issues and their solutions.

## Connection Issues

### ""Unable to connect to server""
**Possible Causes:**
- Wrong IP address or port
- Server not running
- Firewall blocking connection
- Network unreachable

**Solutions:**
1. Verify the IP address and port are correct
2. Ensure the Modbus server is running
3. Check Windows Firewall settings
4. Try pinging the server IP
5. Use the Diagnostics feature to test connectivity

### ""Connection timeout""
**Possible Causes:**
- Network latency
- Server not responding
- Incorrect Unit ID

**Solutions:**
1. Check network connectivity
2. Verify the server is running and responsive
3. Try a different Unit ID
4. Increase timeout in settings (if available)

## Data Issues

### ""All values are 0""
**Possible Causes:**
- Wrong address range
- Unit ID mismatch
- Server has no data

**Solutions:**
1. Verify the starting address is correct
2. Check the Unit ID matches the server configuration
3. Try reading a different address range
4. Use the Diagnostics feature to test read operations

### ""Write failed""
**Possible Causes:**
- Read-only device
- Wrong address
- Invalid data type

**Solutions:**
1. Verify the device allows writes
2. Check the address is writable
3. Ensure the data type is correct
4. Try writing to a different address

## Performance Issues

### ""Application is slow""
**Possible Causes:**
- Too many continuous reads
- Large address ranges
- High polling frequency

**Solutions:**
1. Reduce the number of continuous reads
2. Decrease the address count
3. Increase the polling interval
4. Close unused trend charts

## Simulation Issues

### ""Simulation not updating""
**Possible Causes:**
- Simulation not enabled
- Timer not running
- Nodes not connected

**Solutions:**
1. Enable simulation in the Simulation tab
2. Check the simulation timer is running
3. Verify nodes are properly connected
4. Check node parameters are configured

## Getting More Help

If you continue to experience issues:
1. Check the diagnostic information (Help → Diagnostics)
2. Export diagnostic logs and share them
3. Visit https://github.com/nokkies/ModbusForge/issues
4. Contact support at reinach@softwareForge.cc

## Diagnostic Information

To export diagnostic information:
1. Go to Help → Diagnostics
2. Click ""Export Diagnostics""
3. Save the file and share with support";
        }

        private string GetMcpServerContent()
        {
            return @"# API & Model Context Protocol (MCP) Server

ModbusForge includes an embedded REST API server that can be integrated with Model Context Protocol (MCP) clients to allow AI assistants (like Claude, ChatGPT, or Cursor) to view and control Modbus operations.

## How to Enable the Server

1. Open the application.
2. Navigate to **Options → Preferences**.
3. Toggle the **Enable REST API / MCP** setting to **ON**.
4. Set the **API Port** (default is `5000`).
5. Choose whether to enable API documentation (Swagger) or require authentication.

## API Documentation (Swagger)

If **Enable API documentation (Swagger)** is turned on:
- You can navigate to `http://localhost:5000/swagger` in your web browser.
- This hosts an interactive developer console listing all API endpoints, Function Codes, and request/response models.
- You can test read and write commands directly from your browser.

## Connecting an AI Agent via MCP

To connect an LLM or AI coding assistant using the Model Context Protocol (MCP):
1. Configure an MCP bridge (e.g., node-based bridge) pointing to the ModbusForge REST API.
2. In your MCP client configuration (such as `claude_desktop_config.json`), add a server entry:
```json
{
  ""mcpServers"": {
    ""modbusforge"": {
      ""command"": ""node"",
      ""args"": [""path/to/modbusforge-mcp-bridge.js""],
      ""env"": {
        ""MODBUSFORGE_API_URL"": ""http://localhost:5000"",
        ""MODBUSFORGE_API_KEY"": ""YOUR_API_KEY""
      }
    }
  }
}
```

## Verifying the Server is Running

- Check the application log in the **Console** tab; it will print `API Server started on http://localhost:5000`.
- Send a query to the status endpoint:
  `curl http://localhost:5000/api/status`
  It should return `{""status"":""Running""}`.";
        }

        private string GetPartialReadsContent()
        {
            return @"# Partial or Chunked Reads

The Modbus protocol limits how many registers can be read in a single network packet. ModbusForge automatically splits large requests into multiple packets and reassembles the result, so you can read or write more registers than a single Modbus request normally allows.

## When does this happen?

- **Holding and input register reads** larger than **125** registers are sent as several consecutive requests.
- **Holding register writes** larger than **123** registers are split into several writes.
- **Coil writes** larger than **1968** coils are split into several writes.

You can enter any count that fits in the Modbus address space (**1 to 65536**) for holding and input registers. Coil and discrete input counts are still limited to the protocol maximum of **2000**.

## What does a red value mean?

If one of the chunks fails, ModbusForge keeps all the values that were successfully read and shows the last successful value in **red**.

- Hover over the red value to see a tooltip explaining that the read was partial.
- The red value is the last register that was read successfully before the failure.
- Any registers after the red value were not read.

## How to clear a red value

- Run the read again. If it succeeds, the red marker disappears and all values are updated.
- Write to a red register (if it is a holding register) and re-read to verify the device is responding.

## Common causes of a partial read

- Network timeout or disconnection after some chunks completed.
- The slave device stopped responding.
- An address in the requested range is not mapped on the device, causing a Modbus exception.
- Reading or writing beyond the device's actual register map.

## Tips

- For very large ranges over slow or serial links, the operation can take a noticeable amount of time.
- If partial reads happen frequently, reduce the count or increase the timeout/period.
- Use the connection log to see how many packets were sent and where the failure occurred.";
        }

        private string GetNotFoundContent(string topicId)
        {
            return $"# Help Topic Not Found\n\nThe help topic '{topicId}' could not be found.\n\nPlease select a topic from the navigation panel or check the Troubleshooting section.";
        }

        private string GetErrorContent()
        {
            return "# Error Loading Help\n\nAn error occurred while loading the help content. Please try again or contact support.";
        }
    }
}
