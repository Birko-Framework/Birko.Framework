# Birko.Communication.Modbus

Modbus master protocol implementation for the Birko Framework, supporting both Modbus TCP (MBAP) and Modbus RTU (CRC-16) transports over any `IPort`.

## Features

- Modbus TCP with MBAP header framing
- Modbus RTU with CRC-16 verification
- Read operations: Coils, Discrete Inputs, Holding Registers, Input Registers
- Write operations: Single/Multiple Coils, Single/Multiple Registers
- Response helpers: register extraction, Float32/Int32 decoding, coil unpacking
- Thread-safe client with configurable timeouts
- Works with any `IPort` transport (TCP, Serial, etc.)

## Installation

This is a shared project (.projitems). Reference it from your main project:

```xml
<Import Project="..\Birko.Communication.Modbus\Birko.Communication.Modbus.projitems"
        Label="Shared" />
```

## Dependencies

- **Birko.Communication** — Base `IPort` interface

## Usage

### Modbus TCP (Holding Registers)

```csharp
using Birko.Communication.Modbus.Protocols;
using Birko.Communication.Network.Ports;

var port = new TcpIp(new TcpIpSettings { Address = "192.168.1.100", Port = 502 });

using var client = new ModbusClient(port, ModbusTransport.Tcp);
client.Connect();

// Read 10 holding registers starting at address 0
var response = client.ReadHoldingRegisters(unitId: 1, startAddress: 0, quantity: 10);
ushort[] values = response.GetRegisters();

// Read a 32-bit float from register pair
float temperature = response.GetFloat32(registerOffset: 0);
```

### Modbus RTU (Serial)

```csharp
using Birko.Communication.Modbus.Protocols;
using Birko.Communication.Hardware.Ports;

var port = new Serial(new SerialSettings
{
    Name = "COM1",
    BaudRate = 9600,
    Parity = Parity.None,
    DataBits = 8,
    StopBits = StopBits.One
});

using var client = new ModbusClient(port, ModbusTransport.Rtu);
client.Connect();

var response = client.ReadInputRegisters(unitId: 1, startAddress: 100, quantity: 2);
int powerValue = response.GetInt32(registerOffset: 0);
```

### Write Operations

```csharp
// Write single coil (ON)
client.WriteSingleCoil(unitId: 1, address: 0, value: true);

// Write single register
client.WriteSingleRegister(unitId: 1, address: 100, value: 0x1234);

// Write multiple registers
client.WriteMultipleRegisters(unitId: 1, startAddress: 100, values: new ushort[] { 0x0001, 0x0002, 0x0003 });

// Write multiple coils
client.WriteMultipleCoils(unitId: 1, startAddress: 0, values: new bool[] { true, false, true, true });
```

### Reading Coils

```csharp
var response = client.ReadCoils(unitId: 1, startAddress: 0, quantity: 16);
bool[] coilStates = response.GetCoils(16);
```

## API Reference

### Classes

| Class | Description |
|-------|-------------|
| `ModbusClient` | Thread-safe Modbus master client over any `IPort` |
| `ModbusFrame` | Static frame builder/parser for TCP (MBAP) and RTU (CRC-16) |
| `ModbusResponse` | Parsed response with register/coil/float/int helpers |
| `ModbusException` | Exception with standard Modbus error code mapping |

### Enums

| Enum | Description |
|------|-------------|
| `ModbusFunction` | Function codes: ReadCoils (0x01) through WriteMultipleRegisters (0x10) |
| `ModbusTransport` | Transport mode: Tcp or Rtu |

### Namespace

- `Birko.Communication.Modbus.Protocols`

## Related Projects

- [Birko.Communication](../Birko.Communication/) — Base communication abstractions (`IPort`)
- [Birko.Communication.Network](../Birko.Communication.Network/) — TCP/UDP ports (Modbus TCP transport)
- [Birko.Communication.Hardware](../Birko.Communication.Hardware/) — Serial ports (Modbus RTU transport)

## License

Part of the Birko Framework.
