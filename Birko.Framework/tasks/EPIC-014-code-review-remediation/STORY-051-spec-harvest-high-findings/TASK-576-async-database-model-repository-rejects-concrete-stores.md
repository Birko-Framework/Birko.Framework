---
id: TASK-576
parent: STORY-051
feature: FEATURE-014
status: todo
priority: P2
assignee: ai
created: 2026-10-12
depends-on: []
blocks: []
findings: [SH-H060]
pr: null
github-issue: null
jira-key: null
---

# `AsyncDataBaseModelRepository<T>` rejects every store built with a concrete connector

## Context

Found by the 2026-10-12 `/specs regen` of all 25 areas at `c50ed580` (`repository-contract`); traced 2026-10-12.

The constructor (`Birko.Data.SQL/Repositories/AsyncDataBaseModelRepository.cs:30`) requires
`store.IsStoreOfType<T, AsyncDataBaseBulkStore<AbstractConnector, T>>()`, which is `store is TStore` on the store or its
innermost wrapped store (`StoreExtensions.IsOrWraps`). Class generics are invariant, so an
`AsyncDataBaseBulkStore<SqLiteConnector, T>` is not an `AsyncDataBaseBulkStore<AbstractConnector, T>`: the constructor
throws `ArgumentException` for every store a provider builds. `DataBaseStore` has the same cast, so it is null even when
construction is bypassed.

Reach, measured: no consumer references the class; the provider repositories (`SqLiteModelRepository`, …) do not derive
from it. Hence P2.

## Acceptance criteria

- [ ] The repository accepts an `AsyncDataBaseBulkStore<TConnector, T>` for any connector (a `TConnector` type parameter, or a non-generic check), and `DataBaseStore` resolves it
- [ ] Or, if the class has no use, it is removed with a CHANGELOG note — decide which in the task, and say why
- [ ] A regression test fails on the current code and passes with the fix
- [ ] `/specs regen` of the area, with the spec diff reviewed, is the fix's evidence (EPIC-014's standing constraint)

## Out of scope

- The sync `DataBaseModelRepository` and the provider repositories, which carry their connector type

## Human test plan

N/A — the behaviour is asserted by the regression test; nothing here is visual.
