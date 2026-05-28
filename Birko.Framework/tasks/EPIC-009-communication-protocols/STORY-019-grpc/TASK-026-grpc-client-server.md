---
id: TASK-026
parent: STORY-019
status: todo
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

- [ ] `Birko.Communication.gRPC` shared project exists (or split into `.Client` + `.Server` if size warrants), registered everywhere
- [ ] Client primitives over `Grpc.Net.Client` with channel pooling
- [ ] Server primitives over `Grpc.AspNetCore`
- [ ] Settings descendant of `RemoteSettings` (endpoint, credentials, TLS)
- [ ] Authentication interceptor scaffold
- [ ] xUnit tests with in-memory channel
- [ ] CLAUDE.md / README.md / License.md / .gitignore

## Out of scope

- gRPC-Web (browser support) — separate follow-up
- Code generation tooling
