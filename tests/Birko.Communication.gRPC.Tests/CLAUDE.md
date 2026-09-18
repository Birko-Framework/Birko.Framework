# Birko.Communication.gRPC.Tests

## Overview

xUnit + FluentAssertions test project for `Birko.Communication.gRPC` (client primitives).

## Project Location

`C:\Source\Birko.Communication.gRPC.Tests\`

## Scope

Tests the client gRPC primitives without any network: settings, the channel pool, the
authentication interceptor (verified by invoking it with a captured continuation), the client
factory (over an explicit in-memory `CallInvoker`), and the exception wrapper.

## Conventions

- Regular `Microsoft.NET.Sdk` csproj (not shared). Imports `Birko.Contracts`, `Birko.Configuration`,
  and `Birko.Communication.gRPC` `.projitems`; adds the `Grpc.Net.Client` package.
- One test class per source type; test both success and failure/guard paths.
- `GrpcChannelPool` is static, so `GrpcChannelPoolTests` clears it in ctor/Dispose to stay isolated.

## Maintenance

Follow the root [CLAUDE-maintenance.md](../Birko.Framework/CLAUDE-maintenance.md).
