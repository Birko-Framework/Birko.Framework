---
id: TASK-780
parent: null
feature: null
status: done
priority: P2
assignee: ai
created: 2026-10-28
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Birko.Communication.GraphQL does not compile beside Birko.Communication.WebSocket

## Context

Found while extending the wiki (every module group now has a compiled page): GraphQL, gRPC and eight other
projects were missing from Sandbox's compile-gate aggregator, so the wiki could not compile their examples. Adding
all ten to a scratch copy of the gate left one failure:

```
Birko.Communication.GraphQL/WebSocketMessageReader.cs(16,62): error CS0118: 'WebSocket' is a namespace but is used like a type
```

`WebSocketMessageReader.ReceiveTextAsync` took a bare `WebSocket`. Inside `namespace Birko.Communication.GraphQL`,
C# looks the name up in the enclosing `Birko.Communication` namespace before the file's `using
System.Net.WebSockets;`, and once `Birko.Communication.WebSocket` is compiled into the same assembly that sibling
namespace wins. So **any consumer aggregator importing both projects failed to build** — the likely reason GraphQL
was never in the gate. The GraphQL test project had the same bare name (`ScriptedWebSocket : WebSocket`).

## Acceptance criteria

- [x] GraphQL compiles in one assembly with a `Birko.Communication.WebSocket` namespace
- [x] A regression check that fails to compile on the code before
- [x] The README's stale `C:\Source\Birko.Communication.REST.Server\` location corrected (same doc sweep)

## Human test plan

N/A — fully covered by automated tests: the regression is a compile-time defect, and the test project is the build
that reproduces it. Sandbox's compile gate additionally imports GraphQL beside WebSocket from this change on.

## Outcome (2026-10-28)

Both bare uses are written `System.Net.WebSockets.WebSocket`. New
`tests/Birko.Communication.GraphQL.Tests/WebSocketNamespaceCollisionTests.cs` declares a stand-in
`Birko.Communication.WebSocket` namespace in the test assembly; on the code before, the project fails with the CS0118
above (verified red), after it GraphQL.Tests passes 59/59. The test project is the only importer of the GraphQL
projitems in this repo.

The scratch gate with all ten missing projects (`Communication.GraphQL`, `.gRPC`, `.gRPC.Server`,
`Data.EventSourcing.SQL`, `EventBus.Tenant`, `Health.Data.ElasticSearch`, `Health.Data.IndexManagement`,
`Health.Data.SQL`, `Health.Security.Grants`, `Serialization.Yaml`) plus the 133 wiki snippets builds; the imports
land in Birko.Sandbox separately.
