# Birko.Communication.Modbus.Tests

## Overview
Unit tests for Birko.Communication.Modbus protocol implementation.

## Project Location
`C:\Source\Birko.Communication.Modbus.Tests\`

## Components

### MockPort.cs
- **MockPort** — In-memory IPort implementation for testing without real hardware
  - Captures all written data, provides pre-configured response data
  - Tracks Open/Close/Clear call counts

### ModbusFrameTests.cs
- CRC-16 calculation (known vectors, empty, consistency)
- TCP request building (all function codes, MBAP header structure)
- TCP write request building (raw PDU wrapping)
- TCP response parsing (read registers, write echo, errors, too-short)
- RTU request building (frame structure, CRC validity)
- RTU write request building (raw PDU + CRC)
- RTU response parsing (read registers, write echo, CRC mismatch, errors)
- PDU builders (WriteSingle, WriteMultiple)

### ModbusResponseTests.cs
- GetRegisters (values, big-endian, empty)
- GetCoils (full byte, partial, multi-byte, zero quantity)
- GetFloat32 (valid, offset, insufficient data)
- GetInt32 (positive, negative, offset, insufficient data)

### ModbusExceptionTests.cs
- Known error codes 1–6 with expected messages
- Unknown error codes
- Exception inheritance

### ModbusClientTests.cs
- Constructor validation (null port)
- Connect/Disconnect lifecycle
- All TCP read operations (coils, discrete inputs, holding registers, input registers)
- All TCP write operations (single coil, single register, multiple coils, multiple registers)
- RTU read operation
- Timeout handling
- Auto-connect behavior
- Port buffer clearing
- Transaction ID incrementing

## Dependencies
- Birko.Communication (IPort interface)
- Birko.Communication.Modbus (code under test)
- xUnit 2.9.3, FluentAssertions 7.0.0

## Maintenance
When adding new Modbus functionality, add corresponding tests here.
