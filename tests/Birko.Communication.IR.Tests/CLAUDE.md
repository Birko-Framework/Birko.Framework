# Birko.Communication.IR.Tests

## Overview
Unit tests for Birko.Communication.IR infrared communication protocols and models.

## Project Location
`tests/Birko.Communication.IR.Tests/`

## Components
- **NecProtocolTests.cs** — NEC encode/decode round-trip (standard + extended), leader timing, repeat codes, complement validation, null/short input
- **SamsungProtocolTests.cs** — Samsung encode/decode round-trip, address repeat validation, all address/command sweep, leader timing
- **Rc5ProtocolTests.cs** — RC5 Manchester encode/decode, toggle bit, 5-bit address/6-bit command masking, multiple combinations
- **RawProtocolTests.cs** — Raw decode (catch-all), FNV-1a hash consistency, encode throws NotSupportedException
- **IrTimingTests.cs** — Constructor validation, TotalDurationUs, ToProntoHex format, empty/padding edge cases
- **IrCommandTests.cs** — ToString (standard + repeat), default property values

## Dependencies
- Birko.Communication (IPort)
- Birko.Communication.IR (code under test)
- xUnit 2.9.3, FluentAssertions 7.0.0

## Maintenance
When adding new IR protocols or features, add corresponding tests here.
