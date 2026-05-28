---
id: STORY-019
parent: EPIC-009
status: planned
created: 2026-05-28
---

# gRPC support

## User story

As a developer, I want gRPC support in `Birko.Communication` so I can call gRPC services or expose gRPC endpoints alongside HTTP/REST.

## Behaviour

- Client primitives over `Grpc.Net.Client`
- Server primitives over `Grpc.AspNetCore` (or `Grpc.AspNetCore.Server`)
- Settings descendant of `RemoteSettings` (endpoint, credentials, tls)
- Reuses Birko.Serialization for non-protobuf payloads where applicable
