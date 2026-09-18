# Birko.Communication.IR

Consumer infrared (IR) communication library for 38 kHz modulated remote control protocols (NEC, Samsung, RC5). **Not** IrDA/IrCOMM — see `Birko.Communication.Hardware.Ports.Infraport` for serial IrCOMM.

## Features

- **InfraredPort** — extends AbstractPort for consumer IR, with async send/receive and learning mode
- **Pluggable transports** — IIrTransport with 4 backends:
  - **Serial** — USB-UART + IR LED on microcontroller (Arduino/ESP32)
  - **HTTP** — ESPHome REST API (remote_transmitter)
  - **MQTT** — ESPHome/Tasmota MQTT (stub)
  - **GPIO** — Linux /dev/lirc0, Raspberry Pi direct (stub)
- **Protocol layer** — IIrProtocol encodes/decodes between commands and raw timings
  - **NEC** — Standard (8-bit address) and extended (16-bit address) with repeat codes
  - **Samsung** — 32-bit TV remote variant (address repeated, command complemented)
  - **RC5** — Philips Manchester-encoded 14-bit protocol (36 kHz)
  - **Raw** — Capture & replay for unknown protocols (learning mode)
- **Device profiles** — IDeviceProfile codebook per device model
  - **SamsungAcProfile** — Samsung AC with modes, temp 16–30 °C, fan speeds, swing, Wind-Free
- **IrTiming** — Raw mark/space durations with Pronto hex export

## Installation

This is a shared project. Import the `.projitems` file in your `.csproj`:

```xml
<Import Project="..\Birko.Communication.IR\Birko.Communication.IR.projitems" Label="Shared" />
```

Also import `Birko.Communication` (for AbstractPort/IPort) and `Birko.Communication.Hardware` (for Serial port, if using SerialIrTransport).

## Dependencies

- **Birko.Communication** — IPort, AbstractPort, PortSettings
- **Birko.Communication.Hardware** — Serial port (for SerialIrTransport)
- **System.Net.Http** — HttpClient (for HttpIrTransport)
- **System.Text.Json** — JSON serialization (for HttpIrTransport)

## Usage

### Send NEC command via Serial transport

```csharp
using Birko.Communication.IR.Ports;
using Birko.Communication.IR.Transports;
using Birko.Communication.IR.Protocols;

var settings = new InfraredSettings
{
    Name = "LivingRoom IR",
    TransportType = "serial",
    ConnectionString = "COM3"
};

var transport = new SerialIrTransport("COM3", 115200);
var port = new InfraredPort(settings, transport);
port.Open();

var nec = new NecProtocol();
var command = new IrCommand
{
    Address = 0x04,  // Samsung TV
    Command = 0x02   // Power
};

await port.SendCommandAsync(nec, command);
port.Close();
```

### Send via ESPHome HTTP

```csharp
using Birko.Communication.IR.Transports;
using Birko.Communication.IR.Protocols;

var transport = new HttpIrTransport("http://192.168.1.100", apiPassword: "my_api_key");
await transport.ConnectAsync();

var nec = new NecProtocol();
var timing = nec.Encode(new IrCommand { Address = 0x04, Command = 0x02 });
await transport.TransmitAsync(timing);
```

### Samsung AC control

```csharp
using Birko.Communication.IR.Devices;

var ac = new SamsungAcProfile();
ac.SetTemperature(22);
ac.SetMode(SamsungAcMode.Cool);
ac.SetFanSpeed(SamsungAcFan.Auto);

var timing = ac.GetTiming("PowerOn");
await transport.TransmitAsync(timing!);
```

### Learning mode (capture unknown remote)

```csharp
var port = new InfraredPort(settings, transport);
port.RegisterProtocol(new NecProtocol());
port.RegisterProtocol(new SamsungProtocol());
port.RegisterProtocol(new RawProtocol()); // catch-all

port.OnCommandReceived += (sender, cmd) =>
{
    Console.WriteLine($"Received: {cmd}");
};

port.Open();
await port.StartLearningAsync();
// Point remote at receiver and press buttons...
await port.StopLearningAsync();
```

## API Reference

### Ports

| Class | Description |
|-------|-------------|
| `InfraredSettings` | Port settings (carrier freq, timeout, transport type, connection string) |
| `InfraredPort` | Consumer IR port with async send/receive and protocol decoding |

### Transports

| Class | Description |
|-------|-------------|
| `IIrTransport` | Transport interface (connect, transmit, receive) |
| `SerialIrTransport` | USB-UART with microcontroller IR LED |
| `HttpIrTransport` | ESPHome REST API |
| `MqttIrTransport` | MQTT stub (ESPHome/Tasmota) |
| `GpioIrTransport` | Linux LIRC stub (Raspberry Pi) |

### Protocols

| Class | Description |
|-------|-------------|
| `IIrProtocol` | Protocol interface (encode/decode) |
| `NecProtocol` | NEC standard/extended (38 kHz, 562.5 μs unit) |
| `SamsungProtocol` | Samsung 32-bit TV remote (38 kHz, 550 μs unit) |
| `Rc5Protocol` | Philips RC5 Manchester (36 kHz, 889 μs half-bit) |
| `RawProtocol` | Raw capture/replay (learning mode catch-all) |
| `IrTiming` | Raw mark/space durations with Pronto hex export |
| `IrCommand` | Decoded command (protocol, address, command, raw code) |

### Devices

| Class | Description |
|-------|-------------|
| `IDeviceProfile` | Device codebook interface |
| `DeviceCommand` | Named command with address/code/extended data |
| `SamsungAcProfile` | Samsung AC (modes, temp 16–30, fan, swing, Wind-Free) |

## Related Projects

- [Birko.Communication](../Birko.Communication/) — Base communication abstractions
- [Birko.Communication.Hardware](../Birko.Communication.Hardware/) — Serial/LPT/Infraport (IrCOMM)
- [Birko.Communication.Network](../Birko.Communication.Network/) — TCP/IP, UDP
- [Birko.Communication.Modbus](../Birko.Communication.Modbus/) — Modbus RTU/TCP protocol

## License

Part of the Birko Framework. See [License.md](License.md) for MIT license details.
