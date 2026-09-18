# Birko.Communication.gRPC.Server

gRPC **server** primitives for the Birko Framework — DI wiring and an authentication interceptor
scaffold over [`Grpc.AspNetCore`](https://www.nuget.org/packages/Grpc.AspNetCore). The client half
lives in [`Birko.Communication.gRPC`](../Birko.Communication.gRPC) (mirrors the
`Birko.Communication.REST` / `.REST.Server` split).

## Features

- **`AddBirkoGrpc(this IServiceCollection, GrpcServerSettings?)`** — registers gRPC services with
  Birko defaults (detailed errors, message-size caps) applied from settings; returns the
  `IGrpcServerBuilder` so service registration can be chained.
- **`GrpcServerAuthenticationInterceptor`** — server interceptor that validates request metadata
  (e.g. an authorization header) before any handler runs, across all four call kinds (unary,
  client/server/duplex streaming). Throws `RpcException(Unauthenticated)` on failure.
- **`GrpcServerSettings`** — `Settings` descendant: `EnableDetailedErrors`,
  `MaxReceiveMessageSizeBytes`, `MaxSendMessageSizeBytes`, `EnableReflection`.

> Code generation (`.proto` → C#) and service implementations are **out of scope** — the host brings
> its own generated services via `Grpc.Tools` and maps them with `MapGrpcService<T>()`.

## Usage

```csharp
using Birko.Communication.gRPC.Server;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

var settings = new GrpcServerSettings { EnableDetailedErrors = true };

builder.Services
    .AddBirkoGrpc(settings)
    .AddServiceOptions<GreeterService>(o => { /* per-service tuning */ });

// Register the auth interceptor + validator.
builder.Services.AddSingleton(new GrpcServerAuthenticationInterceptor(
    (headers, ctx) => Task.FromResult(headers.GetValue("authorization") is { } h && tokenValidator.IsValid(h))));

var app = builder.Build();
app.MapGrpcService<GreeterService>();   // GreeterService generated from your .proto
app.Run();
```

To apply the interceptor globally, add it in the `AddBirkoGrpc` options callback's
`options.Interceptors.Add<GrpcServerAuthenticationInterceptor>()` (it must be resolvable from DI).

## Dependencies

- **Birko.Configuration** — `Settings` base.
- **Grpc.AspNetCore** (NuGet) — `AddGrpc`, `IGrpcServerBuilder`, server interceptors.
- Requires the **`Microsoft.AspNetCore.App`** shared framework in the consuming host.

Consumers that import this shared project must add the `Grpc.AspNetCore` package reference and target
the ASP.NET Core shared framework.

## Running tests

```
dotnet test ..\Birko.Communication.gRPC.Server.Tests\Birko.Communication.gRPC.Server.Tests.csproj
```

## License

MIT — see [License.md](License.md).
