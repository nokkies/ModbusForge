# ModbusForge Scripting Guide

A complete guide to creating and executing Modbus test scripts with the ModbusForge Script Editor.

## Table of Contents

- [Introduction](#introduction)
- [Opening the Script Editor](#opening-the-script-editor)
- [Script Structure](#script-structure)
- [Command Reference](#command-reference)
- [Creating Your First Script](#creating-your-first-script)
- [Advanced Scripting](#advanced-scripting)
- [File Format](#file-format)
- [Tips and Best Practices](#tips-and-best-practices)

---

## Introduction

The ModbusForge Script Editor allows you to create automated test sequences that can:

- Read from any Modbus area
- Write to registers and coils
- Add delays between operations
- Log messages
- Repeat sequences multiple times
- Stop on errors

This is useful for:

- Automated testing of Modbus devices
- Regression testing
- Performance benchmarking
- Simulating device usage patterns
- Logging device behavior over time

---

## Opening the Script Editor

Access the Script Editor via:

- **Menu**: Options → Script Editor
- **Keyboard**: Ctrl+E

The Script Editor window contains:

- **Script Settings**: Name, repeat count, delay, and error handling
- **Commands List**: The sequence of commands to execute
- **Output Log**: Real-time execution feedback
- **Run Controls**: Start, stop, save, and load scripts

---

## Script Structure

A script consists of:

### Metadata

- **Name**: Descriptive name for the script
- **Description**: Optional description of what the script does
- **Repeat Count**: How many times to run the entire command sequence
- **Delay Between Commands**: Milliseconds to wait between commands
- **Stop on Error**: Whether to stop execution when a command fails

### Commands

An ordered list of operations. Each command has:

- **Type**: The operation to perform
- **Parameters**: Address, count, value, etc. depending on type
- **Enabled**: Whether the command should be executed
- **Last Result**: Result from the last execution

---

## Command Reference

### Read Commands

#### Read Holding Registers

Reads values from holding registers.

- **Address**: Starting register address
- **Count**: Number of registers to read
- **Example**: Read 10 registers starting at address 0

#### Read Input Registers

Reads values from input registers.

- **Address**: Starting register address
- **Count**: Number of registers to read

#### Read Coils

Reads states from coils.

- **Address**: Starting coil address
- **Count**: Number of coils to read

#### Read Discrete Inputs

Reads states from discrete inputs.

- **Address**: Starting input address
- **Count**: Number of inputs to read

### Write Commands

#### Write Single Register

Writes a value to a single holding register.

- **Address**: Register address
- **Value**: 16-bit unsigned value (0-65535)
- **Example**: Write 1234 to register 0

#### Write Single Coil

Writes a boolean value to a single coil.

- **Address**: Coil address
- **Bool Value**: True (ON) or False (OFF)
- **Example**: Set coil 0 to ON

### Control Commands

#### Delay

Pauses execution for a specified number of milliseconds.

- **Delay (ms)**: Number of milliseconds to wait
- **Example**: Delay 1000ms (1 second)

#### Log

Adds a custom message to the output log.

- **Message**: Text to log
- **Example**: Log "Starting pump test"

#### Loop

> Note: The Loop command type is defined in the model but currently executes as a single iteration.

---

## Creating Your First Script

### Step 1: Open the Script Editor

Press **Ctrl+E** or go to **Options → Script Editor**.

### Step 2: Configure the Script

1. Enter a name: "First Test"
2. Set Repeat Count to 1
3. Set Delay Between Commands to 100ms
4. Enable Stop on Error

### Step 3: Add Commands

1. Click **Add**
2. Set Type to "ReadHoldingRegisters"
3. Set Address to 1
4. Set Count to 5
5. Click **Add** again
6. Set Type to "WriteSingleRegister"
7. Set Address to 1
8. Set Value to 100
9. Click **Add** again
10. Set Type to "Delay"
11. Set Delay (ms) to 500
12. Click **Add** again
13. Set Type to "ReadHoldingRegisters"
14. Set Address to 1
15. Set Count to 5

### Step 4: Run the Script

1. Connect to a Modbus device
2. Click **Run Script**
3. Watch the Output Log for results

### Step 5: Save the Script

1. Click **Save Script**
2. Choose a location and name
3. The script is saved as a `.mbscript` file

---

## Advanced Scripting

### Using Repeat Counts

Set Repeat Count to a value greater than 1 to loop the entire script. This is useful for:

- Endurance testing
- Periodic monitoring
- Data logging

### Combining Delays

Use delays between commands to:

- Allow devices to settle after writes
- Simulate realistic timing
- Avoid overwhelming the network

### Disabling Commands

Uncheck the **Enabled** checkbox on a command to skip it during execution. This is useful for:

- Temporarily disabling steps during debugging
- Creating multiple test variants
- A/B testing

### Cloning Commands

Use the **Clone** button to duplicate a selected command. This saves time when creating similar operations.

### Stop on Error

When enabled, the script stops immediately if a command fails. When disabled, failures are logged but execution continues.

---

## File Format

Scripts are saved as JSON files with the `.mbscript` extension.

### Example `.mbscript` File

```json
{
  "Name": "First Test",
  "Description": "A simple test script",
  "StopOnError": true,
  "RepeatCount": 1,
  "DelayBetweenCommandsMs": 100,
  "Commands": [
    {
      "CommandType": "ReadHoldingRegisters",
      "Address": 1,
      "Count": 5,
      "Value": 0,
      "BoolValue": false,
      "DelayMs": 1000,
      "Message": "",
      "LoopCount": 1,
      "IsEnabled": true
    },
    {
      "CommandType": "WriteSingleRegister",
      "Address": 1,
      "Count": 1,
      "Value": 100,
      "BoolValue": false,
      "DelayMs": 1000,
      "Message": "",
      "LoopCount": 1,
      "IsEnabled": true
    }
  ]
}
```

---

## Tips and Best Practices

### Before Running

- Verify the connection to the Modbus device
- Start with simple read commands
- Test writes with non-critical addresses first
- Save your script before running

### During Execution

- Watch the Output Log for errors
- Use the Stop button if something goes wrong
- Enable Stop on Error for critical devices

### Script Design

- Keep scripts focused on a single test scenario
- Use descriptive names and log messages
- Add delays between write and subsequent read operations
- Use repeat counts for endurance testing

### Common Patterns

#### Read-Modify-Read Pattern

```
1. Read Holding Registers
2. Write Single Register
3. Delay
4. Read Holding Registers
5. Log ("Verify write successful")
```

#### Polling Pattern

```
1. Set Repeat Count to 100
2. Read Holding Registers
3. Delay 1000ms
```

#### Coil Toggle Pattern

```
1. Write Single Coil (ON)
2. Delay 500ms
3. Read Coils
4. Write Single Coil (OFF)
5. Delay 500ms
6. Read Coils
```

### Safety

- Always verify addresses before writing
- Use delays to prevent rapid successive writes
- Test scripts on non-production devices first
- Enable Stop on Error when working with critical systems
