# Birko.Communication.Modbus

## Overview
Modbus master protocol implementation over any Birko.Communication IPort transport.

## Project Location
`Birko.Communication.Modbus/`

## Components

### Protocols/ModbusFunction.cs
- **ModbusFunction** enum — Function codes (0x01–0x10): ReadCoils, ReadDiscreteInputs, ReadHoldingRegisters, ReadInputRegisters, WriteSingleCoil, WriteSingleRegister, WriteMultipleCoils, WriteMultipleRegisters
- **ModbusTransport** enum — Tcp (MBAP header) or Rtu (CRC-16)

### Protocols/ModbusFrame.cs
- **ModbusFrame** static class — Frame building and parsing for TCP (MBAP) and RTU (CRC-16)
  - `BuildTcpRequest` / `BuildRtuRequest` — Read request frame builders (function + startAddress + quantity)
  - `BuildTcpWriteRequest` / `BuildRtuWriteRequest` — Generic frame builders accepting raw PDU bytes
  - `ParseTcpResponse` / `ParseRtuResponse` — Response parsers (auto-detect read vs write response format)
  - `CalculateCrc16` — Standard Modbus CRC-16 (polynomial 0xA001)
- **ModbusResponse** sealed class — Parsed response with helpers:
  - `GetRegisters()` — 16-bit register values (big-endian)
  - `GetCoils()` — Bit-unpacked coil/discrete input states
  - `GetFloat32()` / `GetInt32()` — Multi-register value extraction

### Protocols/ModbusClient.cs
- **ModbusClient** — Thread-safe Modbus master over any IPort
  - Read operations: `ReadCoils`, `ReadDiscreteInputs`, `ReadHoldingRegisters`, `ReadInputRegisters`
  - Write operations: `WriteSingleCoil`, `WriteSingleRegister`, `WriteMultipleCoils`, `WriteMultipleRegisters`
  - Configurable: `ResponseTimeoutMs` (3000ms default), `PollIntervalMs` (10ms default)

### Protocols/ModbusException.cs
- **ModbusException** — Exception with Modbus error code mapping (codes 1–6)

## Dependencies
- **Birko.Communication** — IPort interface (AbstractPort, PortSettings)

## Transport Usage
- **Modbus TCP**: Use with `Birko.Communication.Network.Ports.TcpIp`
- **Modbus RTU**: Use with `Birko.Communication.Hardware.Ports.Serial`

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of this project, update the README.md accordingly.

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect new or renamed files, changed architecture, or updated interfaces.

### Test Requirements
Every new public functionality must have corresponding unit tests in Birko.Communication.Modbus.Tests.
