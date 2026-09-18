# Birko.Communication.Tests

## Overview
Unit tests for the Birko.Communication base port surface (`PortSettings`, `AbstractPort`, `IPort`).

## Project Location
`C:\Source\Birko\Framework.Tests\Birko.Communication.Tests\`

## Test Framework
xUnit + FluentAssertions

## Scope & conventions
- **No real hardware.** Behavior is exercised through a trivial in-memory `AbstractPort` subclass; no
  OS ports are opened.
- `AbstractPortTests` — CR-L044 coverage: `PortSettings.GetID` (CR-L041 typo regression: prefix is
  `AbstractPort|`), `Clear`/`IsEmpty`/`GetData` over `ReadData`, the `SubscribeProcessData` /
  `InvokeProcessData` (protected, CR-L042) / `UnSubscribeProcessData` wiring, and `IPort : IDisposable`
  → `Dispose()` calls `Close()` once (CR-L043).
