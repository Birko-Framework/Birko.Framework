# Birko.Communication.Hardware

Hardware device communication library providing serial port, LPT parallel port, and infrared port implementations for the Birko Framework.

## Features

- Serial port communication with configurable baud rate, parity, data bits, and stop bits
- LPT parallel port I/O via native `inpout32.dll` interop
- Infrared (IrCOMM) communication through virtual COM port
- Event-driven data reception via `AbstractPort` base class

## Installation

This is a shared project (.projitems). Reference it from your main project:

```xml
<Import Project="..\Birko.Communication.Hardware\Birko.Communication.Hardware.projitems"
        Label="Shared" />
```

## Dependencies

- **Birko.Communication** - Base communication interfaces (`AbstractPort`, `PortSettings`)
- **System.IO.Ports** - .NET serial port APIs

## Usage

### Serial Port

```csharp
using Birko.Communication.Hardware.Ports;

var settings = new SerialSettings
{
    Name = "COM1",
    BaudRate = 9600,
    Parity = Parity.None,
    DataBits = 8,
    StopBits = StopBits.One
};

var serial = new Serial(settings);
serial.OnDataReceived += (sender, data) =>
{
    Console.WriteLine($"Received: {Encoding.UTF8.GetString(data)}");
};

serial.Open();
serial.Write(Encoding.UTF8.GetBytes("Hello Device"));
serial.Close();
```

### Infrared Port (IrCOMM)

```csharp
using Birko.Communication.Hardware.Ports;

var settings = new InfraportSettings
{
    Name = "COM3",
    BaudRate = 9600,
    Parity = Parity.None,
    DataBits = 8,
    StopBits = StopBits.One
};

var infraport = new Infraport(settings);
infraport.Open();
```

### LPT Parallel Port

```csharp
using Birko.Communication.Hardware.Ports;

var settings = new LPTSettings
{
    Name = "LPT1",
    Number = 1
};

var lpt = new LPT(settings);
lpt.Open();
```

## API Reference

### Classes

| Class | Description |
|-------|-------------|
| `Serial` | Serial port implementation extending `AbstractPort` |
| `SerialSettings` | Settings for serial connections (BaudRate, Parity, DataBits, StopBits) |
| `LPT` | LPT parallel port with native I/O via `inpout32.dll` |
| `LPTSettings` | Settings for LPT connections (Number) |
| `Infraport` | Infrared port extending `Serial` (IrCOMM virtual COM port) |
| `InfraportSettings` | Settings for infrared connections (extends `SerialSettings`) |

### Namespace

- `Birko.Communication.Hardware.Ports`

## Related Projects

- [Birko.Communication](../Birko.Communication/) - Base communication abstractions
- [Birko.Communication.Network](../Birko.Communication.Network/) - TCP/UDP network ports
- [Birko.Communication.Bluetooth](../Birko.Communication.Bluetooth/) - Bluetooth communication (extends `Serial`)

## License

Part of the Birko Framework.
