# Birko.Communication.gRPC.Server

## Overview

gRPC **server** primitives for the Birko Framework — DI wiring and a server-side authentication
interceptor scaffold over `Grpc.AspNetCore`. Client counterpart: `Birko.Communication.gRPC`
(mirrors the `REST` / `REST.Server` split).

## Project Location

`C:\Source\Birko.Communication.gRPC.Server\`

## Components

- **`GrpcServerSettings`** (`GrpcServerSettings.cs`) — extends `Birko.Configuration.Settings`.
  `EnableDetailedErrors`, `MaxReceiveMessageSizeBytes`, `MaxSendMessageSizeBytes`, `EnableReflection`.
- **`GrpcServiceExtensions`** (`GrpcServiceExtensions.cs`) — namespace `Microsoft.Extensions.DependencyInjection`
  (discoverable convention). `AddBirkoGrpc(this IServiceCollection, GrpcServerSettings?)` calls
  `services.AddGrpc(...)` mapping settings onto `GrpcServiceOptions`; returns `IGrpcServerBuilder`.
- **`GrpcServerAuthenticationInterceptor`** (`GrpcServerAuthenticationInterceptor.cs`) —
  `Grpc.Core.Interceptors.Interceptor` overriding all four server handler kinds. Validator delegate
  `Func<Metadata, ServerCallContext, Task<bool>>`; throws `RpcException(StatusCode.Unauthenticated)` on failure.
  Shared check factored into `EnsureAuthenticatedAsync`.

## Dependencies

- **Birko.Configuration** (`Settings`).
- **Grpc.AspNetCore** (NuGet) — `AddGrpc`, `IGrpcServerBuilder`, `GrpcServiceOptions`.
- **Microsoft.AspNetCore.App** shared framework — required by the consuming host (and by the test project).
- **Grpc.Core.Api** (transitive) — server interceptors, `ServerCallContext`, `RpcException`.

Shared projects carry no `PackageReference`; the importing csproj (tests / host) supplies
`Grpc.AspNetCore` and the ASP.NET Core framework reference.

## Conventions / notes

- **Code generation + service implementations are out of scope** — the host brings generated services
  via `Grpc.Tools` and maps them with `MapGrpcService<T>()`.
- All code compiles without nullable warnings (CS8600–CS8625).
- `GrpcServiceExtensions` deliberately lives in `Microsoft.Extensions.DependencyInjection` so
  `AddBirkoGrpc` is discoverable next to `AddGrpc` without an extra `using`.

## Maintenance

Follow the root [CLAUDE-maintenance.md](../Birko.Framework/CLAUDE-maintenance.md). Every new public
type needs xUnit + FluentAssertions tests in `Birko.Communication.gRPC.Server.Tests`.
