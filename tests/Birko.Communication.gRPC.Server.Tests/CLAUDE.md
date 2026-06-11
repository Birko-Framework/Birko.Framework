# Birko.Communication.gRPC.Server.Tests

## Overview

xUnit + FluentAssertions test project for `Birko.Communication.gRPC.Server` (server primitives).

## Project Location

`C:\Source\Birko.Communication.gRPC.Server.Tests\`

## Scope

Tests the server gRPC primitives without a running host: settings, the `AddBirkoGrpc` DI extension
(against a bare `ServiceCollection`), and the server authentication interceptor (via
`Grpc.Core.Testing.TestServerCallContext`).

## Conventions

- Regular `Microsoft.NET.Sdk` csproj with `<FrameworkReference Include="Microsoft.AspNetCore.App" />`
  (required to compile `Grpc.AspNetCore` server types). Imports `Birko.Contracts`,
  `Birko.Configuration`, and `Birko.Communication.gRPC.Server` `.projitems`; adds the `Grpc.AspNetCore` package.
- One test class per source type; test both success and failure/guard paths.

## Maintenance

Follow the root [CLAUDE-maintenance.md](../Birko.Framework/CLAUDE-maintenance.md).
