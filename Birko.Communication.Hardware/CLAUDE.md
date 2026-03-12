# Birko.Communication.Hardware

## Overview
Hardware device communication implementation for Birko.Communication.

## Project Location
`C:\Source\Birko.Communication.Hardware\`

## Purpose
- Serial port communication
- USB device communication
- Hardware protocol implementation
- Device discovery

## Components

### Serial Port
- `SerialPortCommunicator` - Serial port communication
- `AsyncSerialPortCommunicator` - Async serial port

### USB
- `UsbCommunicator` - USB device communication
- `UsbDeviceFinder` - Device discovery

### Models
- `SerialPortSettings` - Port configuration
- `HardwareDeviceInfo` - Device information

## Serial Port Communication

```csharp
using Birko.Communication.Hardware;

var settings = new SerialPortSettings
{
    PortName = "COM1",
    BaudRate = 9600,
    Parity = Parity.None,
    DataBits = 8,
    StopBits = StopBits.One
};

var communicator = new SerialPortCommunicator(settings);
communicator.DataReceived += (sender, data) =>
{
    Console.WriteLine($"Received: {Encoding.UTF8.GetString(data)}");
};

communicator.Connect();
communicator.Send(Encoding.UTF8.GetBytes("Hello Device"));
```

## USB Communication

```csharp
var finder = new UsbDeviceFinder(vendorId: 0x1234, productId: 0x5678);
var device = finder.FindDevice();

if (device != null)
{
    var communicator = new UsbCommunicator(device);
    communicator.Connect();
    communicator.Send(data);
}
```

## Dependencies
- Birko.Communication
- System.IO.Ports
- (Optional) device-specific USB libraries

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
