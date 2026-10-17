---
id: TASK-576
parent: STORY-051
feature: FEATURE-014
status: done
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

- [x] The repository accepts an `AsyncDataBaseBulkStore<TConnector, T>` for any connector (a `TConnector` type parameter, or a non-generic check), and `DataBaseStore` resolves it
- [x] ~~Or, if the class has no use~~ (decided against, below), it is removed with a CHANGELOG note — decide which in the task, and say why
- [x] A regression test fails on the current code and passes with the fix
- [x] `/specs regen` of the area, with the spec diff reviewed, is the fix's evidence (EPIC-014's standing constraint)

## Out of scope

- The sync `DataBaseModelRepository` and the provider repositories, which carry their connector type

## Human test plan

N/A — the behaviour is asserted by the regression test; nothing here is visual.

## Resolution (2026-10-17)

Measured on HEAD: `new AsyncDataBaseModelRepository<T>(new AsyncDataBaseBulkStore<SqLiteConnector, T>())` threw
`ArgumentException: Store must be of type AsyncDataBaseBulkStore<T> or a wrapper around it.`

**Fixed, not removed:** the class is now `AsyncDataBaseModelRepository<TConnector, T>`, mirroring the sync
`DataBaseModelRepository<TConnector, T>`. It checks `AsyncDataBaseBulkStore<TConnector, T>` and adds `Connector`,
`AddOnInit` and `RemoveOnInit`, as the sync class has. Why not remove it:
- The sync sibling exists and is used (every provider repository derives from it), so removal leaves the async side
  with no model repository.
- The class rejected every real store, so no working caller can exist. Changing the arity breaks nothing that ran. No
  reference in the framework or in `Birko\Consumers`.

Tests (`tests/Birko.Data.SQL.SqLite.Tests/Task576AsyncModelRepositoryTests.cs`, 3):
- a SQLite store is accepted and does a create/read round trip;
- a wrapped store resolves to the inner one;
- a store closed over another connector type is refused.

Red: the probe above failed on HEAD. The new tests do not compile against the old arity, so the probe is the evidence.

Spec `repository-contract.md`: the "fixed to the abstract connector type" scenario is replaced by "takes the store's
connector type". The area spec is updated by hand from the code (the fix's diff); there was no full regen run.
Regression: all 32 importers of `Birko.Data.SQL.projitems` pass live, 2809 tests.
