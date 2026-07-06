# Birko.Communication.Hardware.Tests

## Overview
Unit tests for Birko.Communication.Hardware — the low-level port helpers (LPT parallel port, serial).

## Project Location
`C:\Source\Birko\Framework.Tests\Birko.Communication.Hardware.Tests\`

## Test Framework
xUnit + FluentAssertions

## Scope & conventions
- **No real hardware.** The inpout32 (`Out32`/`Inp32`) and `System.IO.Ports.SerialPort` paths need
  physical devices and are not exercised. Only pure logic is tested.
- `LptTests` — regression for CR-C03: `LPT.ResolvePortAddress` maps a logical LPT number (1/2/3) to
  the parallel-port base I/O address (0x378 / 0x278 / 0x3BC) that inpout32 expects, passes through a
  raw address, and rejects ambiguous small values.
- References `System.IO.Ports` because the shared Hardware project's Serial port depends on it.
