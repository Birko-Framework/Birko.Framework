# Birko.Communication.gRPC

## Overview

gRPC **client** primitives for the Birko Framework. Provides channel pooling, a typed client factory,
an authentication interceptor scaffold, typed settings, and an exception wrapper over `Grpc.Net.Client`.
The server counterpart is `Birko.Communication.gRPC.Server` (mirrors the `REST` / `REST.Server` split).

## Project Location

`C:\Source\Birko.Communication.gRPC\`

## Components

- **`GrpcSettings`** (`GrpcSettings.cs`) — extends `Birko.Configuration.RemoteSettings`. `Endpoint`
  aliases `Location`; adds `MaxReceiveMessageSizeBytes`, `MaxSendMessageSizeBytes`, `DeadlineSeconds`,
  `Credentials` (`ChannelCredentials`), `ExtraMetadata`.
- **`GrpcChannelPool`** (`GrpcChannelPool.cs`) — static `ConcurrentDictionary`-backed pool keyed by
  `Endpoint`. `GetChannel`, `Remove`, `Clear`. Mirrors `RestClient.GetClient` caching.
- **`GrpcClientFactory`** (`GrpcClientFactory.cs`) — `CreateClient<TClient>(GrpcSettings, params Interceptor[])`
  and `CreateClient<TClient>(CallInvoker, params Interceptor[])`. Uses `Activator.CreateInstance` against
  the generated client's `CallInvoker` constructor; applies interceptors via `CallInvoker.Intercept`.
- **`GrpcAuthenticationInterceptor`** (`GrpcAuthenticationInterceptor.cs`) — `Grpc.Core.Interceptors.Interceptor`
  overriding all five client call kinds. Two constructors: token-provider (bearer header + static
  `ExtraMetadata`) and raw `Action<Metadata>`. Header injection factored into `WithAuth`.
- **`GrpcException`** (`GrpcException.cs`) — wraps `RpcException`; `StatusCode` / `Detail` / `Trailers`;
  `FromRpcException` factory. Mirrors `GraphQLException`.

## Dependencies

- **Birko.Configuration** (`RemoteSettings`).
- **Grpc.Net.Client** (NuGet) — channel creation.
- **Grpc.Core.Api** (transitive of `Grpc.Net.Client`) — interceptors, call invokers, metadata, `RpcException`.

Shared projects carry no `PackageReference`; the importing csproj (tests / consumer) supplies
`Grpc.Net.Client`.

## Conventions / notes

- **Code generation is out of scope** — consumers bring generated clients via `Grpc.Tools`. These
  primitives only configure/wrap them.
- All code compiles without nullable warnings (CS8600–CS8625). `Activator.CreateInstance` result is
  null-checked before cast.
- Namespace is `Birko.Communication.gRPC` (lowercase `g`, matching the project name in TASK-026).

## Maintenance

Follow the root [CLAUDE-maintenance.md](../Birko.Framework/CLAUDE-maintenance.md). Every new public
type needs xUnit + FluentAssertions tests in `Birko.Communication.gRPC.Tests`.
