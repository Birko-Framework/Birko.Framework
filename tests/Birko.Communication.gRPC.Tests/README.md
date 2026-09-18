# Birko.Communication.gRPC.Tests

xUnit + FluentAssertions tests for [`Birko.Communication.gRPC`](../Birko.Communication.gRPC).

## Coverage

- **`GrpcSettingsTests`** — `Endpoint`/`Location` aliasing, defaults, `ExtraMetadata`.
- **`GrpcChannelPoolTests`** — per-endpoint caching, distinct endpoints, eviction, guard clauses.
- **`GrpcAuthenticationInterceptorTests`** — bearer injection, empty-token omission, custom
  header/scheme + extra metadata, blocking-call path, raw mutator constructor, null guard.
- **`GrpcClientFactoryTests`** — client construction from an explicit `CallInvoker` (in-memory) and a
  pooled channel, interceptor wrapping, null guard.
- **`GrpcExceptionTests`** — `RpcException` mapping, trailers, guards.

## Test framework

- xUnit
- FluentAssertions
- Grpc.Net.Client (in-memory channel / call invoker — no network)

## Running tests

```
dotnet test
```

## License

MIT — see [License.md](License.md).
