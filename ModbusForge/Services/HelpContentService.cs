using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
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

        public FlowDocument GetHelpContent(string topicId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(topicId))
                {
                    return CreateSimpleDocument("Invalid topic ID");
                }

                var content = _helpContent.GetValueOrDefault(topicId);
                if (string.IsNullOrWhiteSpace(content))
                {
                    return CreateSimpleDocument(GetNotFoundContent(topicId));
                }

                return ParseMarkdownToFlowDocument(content);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load help content for topic: {TopicId}", topicId);
                return CreateSimpleDocument(GetErrorContent());
            }
        }

        private FlowDocument CreateSimpleDocument(string text)
        {
            try
            {
                var document = new FlowDocument();
                document.FontFamily = new FontFamily("Segoe UI");
                document.FontSize = 14;
                document.PagePadding = new Thickness(0);

                var paragraph = new Paragraph();
                paragraph.Inlines.Add(new Run(text));
                document.Blocks.Add(paragraph);

                return document;
            }
            catch
            {
                // Ultimate fallback
                var document = new FlowDocument();
                var paragraph = new Paragraph(new Run("Help content unavailable"));
                document.Blocks.Add(paragraph);
                return document;
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
                ["script-editor"] = GetScriptEditorContent(),
                ["custom-data"] = GetCustomDataContent(),
                ["trends"] = GetTrendsContent(),
                ["visual-editor"] = GetVisualEditorContent(),
                ["preferences"] = GetPreferencesContent(),
                ["keyboard-shortcuts"] = GetKeyboardShortcutsContent(),
                ["troubleshooting"] = GetTroubleshootingContent()
            };
        }

        private FlowDocument ParseMarkdownToFlowDocument(string markdown)
        {
            try
            {
                var document = new FlowDocument();
                document.FontFamily = new FontFamily("Segoe UI");
                document.FontSize = 14;
                document.PagePadding = new Thickness(0);

                if (string.IsNullOrWhiteSpace(markdown))
                {
                    return document;
                }

                var lines = markdown.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                var currentParagraph = new Paragraph();

                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();

                    if (string.IsNullOrWhiteSpace(trimmedLine))
                    {
                        AddCurrentParagraph(document, ref currentParagraph);
                        continue;
                    }

                    // Headers
                    if (trimmedLine.StartsWith("# "))
                    {
                        AddCurrentParagraph(document, ref currentParagraph);
                        var header = new Paragraph(new Run(trimmedLine.Substring(2).Trim()))
                        {
                            FontSize = 22,
                            FontWeight = FontWeights.SemiBold,
                            Margin = new Thickness(0, 16, 0, 8)
                        };
                        document.Blocks.Add(header);
                    }
                    else if (trimmedLine.StartsWith("## "))
                    {
                        AddCurrentParagraph(document, ref currentParagraph);
                        var header = new Paragraph(new Run(trimmedLine.Substring(3).Trim()))
                        {
                            FontSize = 18,
                            FontWeight = FontWeights.SemiBold,
                            Margin = new Thickness(0, 12, 0, 6)
                        };
                        document.Blocks.Add(header);
                    }
                    else if (trimmedLine.StartsWith("### "))
                    {
                        AddCurrentParagraph(document, ref currentParagraph);
                        var header = new Paragraph(new Run(trimmedLine.Substring(4).Trim()))
                        {
                            FontSize = 16,
                            FontWeight = FontWeights.SemiBold,
                            Margin = new Thickness(0, 8, 0, 4)
                        };
                        document.Blocks.Add(header);
                    }
                    // List items
                    else if (trimmedLine.StartsWith("- ") || trimmedLine.StartsWith("* "))
                    {
                        AddCurrentParagraph(document, ref currentParagraph);
                        var list = new List { Margin = new Thickness(24, 8, 0, 8), MarkerStyle = TextMarkerStyle.Disc };
                        var listItem = new ListItem();
                        var paragraph = new Paragraph();
                        AddInlineText(trimmedLine.Substring(2).Trim(), paragraph);
                        listItem.Blocks.Add(paragraph);
                        list.ListItems.Add(listItem);
                        document.Blocks.Add(list);
                    }
                    // Regular text
                    else
                    {
                        if (currentParagraph.Inlines.Count > 0)
                        {
                            currentParagraph.Inlines.Add(new Run(" "));
                        }
                        AddInlineText(trimmedLine, currentParagraph);
                    }
                }

                AddCurrentParagraph(document, ref currentParagraph);
                return document;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing markdown");
                return CreateSimpleDocument("Error displaying help content");
            }
        }

        private void AddInlineText(string text, Paragraph paragraph)
        {
            // Simple inline formatting for bold and code
            var parts = System.Text.RegularExpressions.Regex.Split(text, @"(\*\*[^*]+\*\*|`[^`]+`)");
            
            foreach (var part in parts)
            {
                if (string.IsNullOrEmpty(part)) continue;

                if (part.StartsWith("**") && part.EndsWith("**") && part.Length > 4)
                {
                    paragraph.Inlines.Add(new Run(part.Substring(2, part.Length - 4))
                    {
                        FontWeight = FontWeights.Bold
                    });
                }
                else if (part.StartsWith("`") && part.EndsWith("`") && part.Length > 2)
                {
                    paragraph.Inlines.Add(new Run(part.Substring(1, part.Length - 2))
                    {
                        FontFamily = new FontFamily("Consolas"),
                        Background = new SolidColorBrush(Color.FromRgb(240, 240, 240))
                    });
                }
                else
                {
                    paragraph.Inlines.Add(new Run(part));
                }
            }
        }

        private void AddCurrentParagraph(FlowDocument document, ref Paragraph currentParagraph)
        {
            if (currentParagraph.Inlines.Count > 0)
            {
                currentParagraph.Margin = new Thickness(0, 8, 0, 8);
                document.Blocks.Add(currentParagraph);
                currentParagraph = new Paragraph();
            }
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

## Script Settings

### Repeat Count
Number of times to repeat the entire script.

### Delay Between Commands
Milliseconds to wait between each command execution.

### Stop on Error
If enabled, the script stops when an error occurs.

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
- **Add to Trend**: Add entry to trend chart

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

The Trend feature provides real-time data visualization for Modbus registers and custom entries.

## Access
Click the ""Trends"" button in the toolbar or go to the Trend tab.

## Adding Trend Lines

### From Registers
1. Read registers to populate the data grid
2. Right-click a register row
3. Select ""Add to Trend""

### From Custom Entries
1. Go to Custom Data tab
2. Right-click a custom entry
3. Select ""Add to Trend""

## Trend Features

### Real-Time Visualization
- Data updates automatically when continuous read is enabled
- Multiple trend lines can be displayed simultaneously
- Each line has a unique color

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

## Tips
- Use descriptive names for trend lines
- Limit the number of trend lines for better performance
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
- **Transform**: Data conversions

### Canvas (Center)
Drag nodes from the palette to the canvas. Connect nodes by dragging from output dots to input dots.

### Properties Panel (Right)
Configure selected node parameters.

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
