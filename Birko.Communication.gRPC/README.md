# Birko.Communication.gRPC

gRPC **client** primitives for the Birko Framework — channel pooling, a typed client factory, an
authentication interceptor scaffold, and typed settings over [`Grpc.Net.Client`](https://www.nuget.org/packages/Grpc.Net.Client).

This is the client half of the gRPC support; the server half lives in
[`Birko.Communication.gRPC.Server`](../Birko.Communication.gRPC.Server) (mirrors the
`Birko.Communication.REST` / `.REST.Server` split).

## Features

- **`GrpcChannelPool`** — endpoint-keyed cache of reusable `GrpcChannel` instances (channels are
  thread-safe and expensive to create, so they should be shared, not created per call).
- **`GrpcClientFactory`** — `CreateClient<TClient>()` constructs any generated client (a
  `ClientBase` subclass) over a pooled channel or an explicit `CallInvoker`, applying interceptors in order.
- **`GrpcAuthenticationInterceptor`** — client interceptor that injects auth metadata into every call
  (unary, server/client/duplex streaming). Construct with a token provider for a bearer-style header,
  or with a custom `Action<Metadata>` for arbitrary headers.
- **`GrpcSettings`** — `RemoteSettings` descendant: `Endpoint` (alias for `Location`), TLS via the
  endpoint scheme or explicit `Credentials`, `MaxReceiveMessageSizeBytes`, `MaxSendMessageSizeBytes`,
  `DeadlineSeconds`, `ExtraMetadata`.
- **`GrpcException`** — wraps `RpcException` exposing `StatusCode`, `Detail`, and `Trailers`.

> Code generation (`.proto` → C#) is **out of scope** for this project — bring your own generated
> client via `Grpc.Tools`. These primitives wrap and configure whatever client you generate.

## Usage

```csharp
using Birko.Communication.gRPC;

var settings = new GrpcSettings
{
    Endpoint = "https://api.example.com:443",
    MaxReceiveMessageSizeBytes = 16 * 1024 * 1024,
};

// Inject a bearer token on every call.
var auth = new GrpcAuthenticationInterceptor(() => tokenStore.CurrentAccessToken);

// Greeter.GreeterClient is generated from your .proto by Grpc.Tools.
var client = GrpcClientFactory.CreateClient<Greeter.GreeterClient>(settings, auth);

try
{
    var reply = await client.SayHelloAsync(new HelloRequest { Name = "Birko" });
}
catch (RpcException ex)
{
    throw GrpcException.FromRpcException(ex);
}
```

## Dependencies

- **Birko.Configuration** — `RemoteSettings` base.
- **Grpc.Net.Client** (NuGet) — `GrpcChannel` / `GrpcChannelOptions`.
- **Grpc.Core.Api** (transitive) — `Interceptor`, `CallInvoker`, `Metadata`, `RpcException`.

Consumers that import this shared project must add the `Grpc.Net.Client` package reference.

## Running tests

```
dotnet test ..\Birko.Communication.gRPC.Tests\Birko.Communication.gRPC.Tests.csproj
```

## License

MIT — see [License.md](License.md).
