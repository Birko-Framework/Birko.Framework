---
id: TASK-026
parent: STORY-019
status: done
priority: P2
assignee: ai
created: 2026-05-28
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# gRPC client + server support

## Context

Add gRPC primitives to `Birko.Communication`. Mirrors the REST client/server split. Dependencies: `Grpc.Net.Client` (client), `Grpc.AspNetCore` (server).

## Acceptance criteria

- [x] `Birko.Communication.gRPC` shared project exists (split into client `Birko.Communication.gRPC` + server `Birko.Communication.gRPC.Server`, mirroring REST / REST.Server), registered in `.slnx` + `.code-workspace`
- [x] Client primitives over `Grpc.Net.Client` with channel pooling (`GrpcChannelPool`, `GrpcClientFactory`)
- [x] Server primitives over `Grpc.AspNetCore` (`AddBirkoGrpc`, `GrpcServerSettings`)
- [x] Settings descendant of `RemoteSettings` (`GrpcSettings`: endpoint, credentials, TLS, message sizes, deadline)
- [x] Authentication interceptor scaffold (client `GrpcAuthenticationInterceptor` + server `GrpcServerAuthenticationInterceptor`)
- [x] xUnit tests with in-memory channel/invoker (24 client + 8 server = 32 passing)
- [x] CLAUDE.md / README.md / License.md / .gitignore (all four projects)

## Resolution

Done 2026-06-11. Two shared projects (`Birko.Communication.gRPC`, `Birko.Communication.gRPC.Server`)
+ two `.Tests` companions. Code generation kept out of scope per the task. See the Recent Updates
entry in the root `CLAUDE.md`.

## Out of scope

- gRPC-Web (browser support) — separate follow-up
- Code generation tooling
