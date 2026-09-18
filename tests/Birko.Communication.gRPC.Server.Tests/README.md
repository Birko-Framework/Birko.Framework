# Birko.Communication.gRPC.Server.Tests

xUnit + FluentAssertions tests for [`Birko.Communication.gRPC.Server`](../Birko.Communication.gRPC.Server).

## Coverage

- **`GrpcServerSettingsTests`** — defaults and property round-trip.
- **`GrpcServiceExtensionsTests`** — `AddBirkoGrpc` returns a builder, registers services, accepts
  settings, and guards null `IServiceCollection`.
- **`GrpcServerAuthenticationInterceptorTests`** — `UnaryServerHandler` runs the continuation when
  authenticated and throws `RpcException(Unauthenticated)` (with the configured detail) when not;
  null-validator guard. Uses `Grpc.Core.Testing.TestServerCallContext` (in-memory, no network).

## Test framework

- xUnit
- FluentAssertions
- Grpc.AspNetCore + `Microsoft.AspNetCore.App` framework reference

## Running tests

```
dotnet test
```

## License

MIT — see [License.md](License.md).
