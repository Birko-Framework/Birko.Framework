# Birko.Communication.Modbus.Tests

Unit tests for Birko.Communication.Modbus — Modbus master protocol implementation.

## Test Framework

- **xUnit** 2.9.3 — Test runner
- **FluentAssertions** 7.0.0 — Assertion library

## Test Coverage

- **ModbusFrameTests** — TCP/RTU frame building and parsing, CRC-16, PDU builders, error responses
- **ModbusResponseTests** — Register extraction, coil unpacking, Float32/Int32 decoding, edge cases
- **ModbusExceptionTests** — Error code mapping, unknown codes, inheritance
- **ModbusClientTests** — Read/write operations over TCP/RTU, auto-connect, timeout, transaction IDs

## Running Tests

```bash
dotnet test Birko.Communication.Modbus.Tests/
```

## License

Part of the Birko Framework.
