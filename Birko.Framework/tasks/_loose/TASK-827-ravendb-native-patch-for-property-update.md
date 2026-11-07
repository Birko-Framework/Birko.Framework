---
id: TASK-827
parent: null
feature: null
status: todo
priority: P3
assignee: ai
created: 2026-11-07
depends-on: [TASK-826]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# RavenDB: translate `PropertyUpdate<T>` to a server-side patch instead of read-modify-save

Split from [[TASK-826]] on the user's word (2026-11-07): investigate first, then decide.

## Context

`Birko.Data.RavenDB` has no native `Update(filter, PropertyUpdate<T>)`: it falls back to `PropertyUpdate.ApplyTo`
(read each match, apply in C#, save), which is **not atomic**. That covers `Increment` (TASK-498) and, after
TASK-826, a computed set. RavenDB has a set-based patch (`PatchByQueryOperation`, RQL `from X where … update { … }`)
that runs JavaScript on the server, atomically per document.

## The question to measure before building

- The patch runs in RavenDB's JavaScript engine (Jint), where a number is a **double**. A constant `Set` and an
  integer `Increment` translate safely; a `decimal` increment or a computed set (`gross / 1.2`, rounded half to even)
  would not reliably equal C#'s `decimal` result to the cent.
- Does RavenDB 6's engine support `BigInt` (scaled-integer exact arithmetic)? And how does a `decimal` round-trip
  through the stored JSON number (`LazyNumberValue`) and back to the C# client?
- Is the filter expressible as RQL by the store's existing query translation, so the patch scopes exactly what a
  read would (tenant, soft-delete)?

## Acceptance criteria

- [ ] The three questions above answered by measurement on the live container (`birko-live-raven`, RavenDB 6.0),
      recorded here.
- [ ] Then, per the answer: a native patch for the kinds it can compute exactly, the C# fallback kept for the rest,
      and the TASK-826 cross-provider contract still passing on RavenDB; or a recorded decision to stay on the fallback.

## Human test plan

N/A — a library change verified by the live RavenDB suites.
