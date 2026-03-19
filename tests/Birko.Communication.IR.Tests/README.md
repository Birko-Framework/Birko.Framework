# Birko.Communication.IR.Tests

Unit tests for the Birko.Communication.IR infrared communication library.

## Test Coverage

- **NecProtocolTests** — NEC encode/decode round-trip, leader timing, repeat codes, extended NEC, complement validation
- **SamsungProtocolTests** — Samsung encode/decode round-trip, address repeat validation, all address/command combinations
- **Rc5ProtocolTests** — RC5 Manchester encode/decode, toggle bit, address/command masking, multiple combinations
- **RawProtocolTests** — Raw protocol decode (catch-all), hash consistency, encode rejection
- **IrTimingTests** — Constructor, TotalDurationUs, ToProntoHex format, duration padding
- **IrCommandTests** — ToString formatting, default values, repeat display

## Test Framework

- xUnit 2.9.3
- FluentAssertions 7.0.0
- .NET 10.0

## Running Tests

```bash
dotnet test Birko.Communication.IR.Tests
```

## License

See [License.md](License.md) for details.
