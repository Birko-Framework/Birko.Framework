# Birko.Communication.IR

## Overview
Consumer infrared (38 kHz modulated) communication for remote control protocols. NOT IrDA/IrCOMM — that is Birko.Communication.Hardware.Ports.Infraport.

## Project Location
`Birko.Communication.IR/`

## Components

### Ports/InfraredSettings.cs
- **InfraredSettings** extends PortSettings — CarrierFrequencyHz (38000), ReceiveTimeoutMs, TransportType, ConnectionString

### Ports/InfraredPort.cs
- **InfraredPort** extends AbstractPort — wraps IIrTransport, registers IIrProtocol decoders
  - `SendCommandAsync(protocol, command)` — encode and transmit
  - `SendRawAsync(timing)` — transmit raw timings
  - `StartLearningAsync()` / `StopLearningAsync()` — learning mode
  - `RegisterProtocol(protocol)` — add decoder for received signals
  - `OnCommandReceived` event — decoded command from learning mode
  - `Write(byte[])` — IPort compat: expects packed int32 durations

### Transports/IIrTransport.cs
- **IIrTransport** interface — ConnectAsync, DisconnectAsync, TransmitAsync, StartReceiveAsync, StopReceiveAsync, OnReceived event

### Transports/SerialIrTransport.cs
- **SerialIrTransport** — USB-UART with microcontroller (Arduino/ESP32)
  - Text protocol: `SEND {freq} {d0},{d1},...\n`, `RECV {freq} {d0},{d1},...\n`, `LEARN\n`, `STOP\n`
  - Background receive loop with line buffering

### Transports/HttpIrTransport.cs
- **HttpIrTransport** — ESPHome REST API (remote_transmitter)
  - POST to `/api/services/remote_transmitter/transmit_raw`
  - JSON payload: `{ command: [signed durations], carrier_frequency, repeat }`
  - Receive not supported (use MQTT for ESPHome remote_receiver)

### Transports/MqttIrTransport.cs
- **MqttIrTransport** — Stub for ESPHome/Tasmota MQTT integration

### Transports/GpioIrTransport.cs
- **GpioIrTransport** — Stub for Linux LIRC /dev/lirc0

### Protocols/IrTiming.cs
- **IrTiming** — Raw mark/space durations array, carrier frequency, repeat count/gap, ToProntoHex()

### Protocols/IrCommand.cs
- **IrCommand** — Decoded: Protocol, Address, Command, RawCode, IsRepeat, BitCount, Toggle

### Protocols/IIrProtocol.cs
- **IIrProtocol** interface — Name, Encode(command)->timing, Decode(timing)->command?

### Protocols/NecProtocol.cs
- **NecProtocol** — 38 kHz, 562.5 μs unit, 32-bit (address+~address+command+~command)
  - Standard: 8-bit address, Extended: 16-bit address (ExtendedMode property)
  - Repeat code: 9000+2250+562 μs
  - Tolerance: ±200 μs

### Protocols/SamsungProtocol.cs
- **SamsungProtocol** — 38 kHz, 550 μs unit, 32-bit (address+address+command+~command)
  - Samsung repeats address instead of complementing it

### Protocols/Rc5Protocol.cs
- **Rc5Protocol** — 36 kHz, Manchester encoding, 889 μs half-bit, 14-bit frame
  - S1(1)+S2(1)+Toggle(1)+Address(5)+Command(6)

### Protocols/RawProtocol.cs
- **RawProtocol** — Learning mode catch-all, Decode always succeeds, Encode throws NotSupportedException

### Devices/IDeviceProfile.cs
- **IDeviceProfile** interface — Manufacturer, Model, Protocol, GetCommandNames, GetCommand, GetTiming

### Devices/DeviceCommand.cs
- **DeviceCommand** — Named command: Name, Description, Address, CommandCode, ExtendedData

### Devices/SamsungAcProfile.cs
- **SamsungAcProfile** — Samsung AC codebook, stateful (tracks power/mode/temp/fan/swing/windFree)
  - 14-byte extended frame with 2-section encoding
  - Enums: SamsungAcMode (Auto/Cool/Dry/Fan/Heat), SamsungAcFan (Auto/Low/Medium/High/Turbo), SamsungAcSwing (Off/Vertical/Horizontal/Both)
  - Commands: PowerOn/Off/Toggle, Mode*, Temp16-30/Up/Down, Fan*, Swing*, WindFreeOn/Off
  - Temperature range: 16–30 °C

## Dependencies
- **Birko.Communication** — IPort, AbstractPort, PortSettings, ProcessDataDelegate
- **Birko.Communication.Hardware** — Serial port (for SerialIrTransport)

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of this project, update the README.md accordingly.

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect new or renamed files, changed architecture, or updated interfaces.

### Test Requirements
Every new public functionality must have corresponding unit tests in Birko.Communication.IR.Tests.
