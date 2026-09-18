# Birko.Communication.Hardware

## Overview
Low-level hardware ports for Birko.Communication. Each port derives from `AbstractPort` (implements
`IPort`) and is configured by a `PortSettings`-derived settings object.

## Project Location
`C:\Source\Birko\Framework\Birko.Communication.Hardware\`

## Components

### Ports (all `: AbstractPort`)
- `Serial` (`Ports/Serial.cs`) — RS-232 serial port over `System.IO.Ports.SerialPort`. Settings:
  `SerialSettings : PortSettings` (Name = port name e.g. "COM1", BaudRate, Parity, DataBits, StopBits, …).
- `Infraport` (`Ports/Infraport.cs`) — serial-attached infrared transceiver. Settings:
  `InfraportSettings : SerialSettings`.
- `LPT` (`Ports/LPT.cs`) — parallel port via inpout32 P/Invoke. Settings: `LPTSettings : PortSettings`.

### Base surface (from `Birko.Communication.Ports.AbstractPort`)
`Open()` / `Close()` / `Write(byte[])` / `Read(int size)` / `HasReadData(int)` / `RemoveReadData(int)` /
`Clear()` / `IsEmpty()` / `GetData()` / `IsOpen()`, the `SubscribeProcessData` / `UnSubscribeProcessData`
event pair, and `PortSettings.GetID()`. `Read`/`HasReadData`/`RemoveReadData` treat a negative `size` as
"all available". `IPort : IDisposable` — `Dispose()` closes the port (Serial also disposes the handle).

## Usage

```csharp
using Birko.Communication.Hardware.Ports;

var port = new Serial(new SerialSettings { Name = "COM1", BaudRate = 9600 });
port.SubscribeProcessData(() =>
{
    var data = port.RemoveReadData(-1); // -1 = drain the whole buffer
    Console.WriteLine($"Received: {System.Text.Encoding.UTF8.GetString(data)}");
});

port.Open();
port.Write(System.Text.Encoding.UTF8.GetBytes("Hello Device"));
// ...
port.Close(); // or `using` — IPort : IDisposable
```

## Dependencies
- Birko.Communication
- System.IO.Ports (Serial / Infraport)

## Use Cases
- Industrial automation
- POS systems
- Sensor data collection
- Device control
- Legacy system integration

## Best Practices

1. **Port cleanup** - Always close ports properly
2. **Error handling** - Handle device disconnections
3. **Timeouts** - Set appropriate read/write timeouts
4. **Buffer management** - Manage buffers carefully
5. **Device detection** - Handle device presence/absence

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of this project, update the README.md accordingly. This includes:
- New classes, interfaces, or methods
- Changed dependencies
- New or modified usage examples
- Breaking changes

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect:
- New or renamed files and components
- Changed architecture or patterns
- New dependencies or removed dependencies
- Updated interfaces or abstract class signatures
- New conventions or important notes

### Test Requirements
Every new public functionality must have corresponding unit tests. When adding new features:
- Create test classes in the corresponding test project
- Follow existing test patterns (xUnit + FluentAssertions)
- Test both success and failure cases
- Include edge cases and boundary conditions
