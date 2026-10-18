---
id: TASK-666
parent: STORY-053
feature: FEATURE-014
status: done
priority: P2
assignee: ai
created: 2026-10-18
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# A SQL view built concurrently takes a cached field by source column name and loses its aggregate

## Context

Found by CI on the TASK-665 push (run 39102580042): `ViewDdlTests.BuildViewSelectSql_CustomerOrderView_ContainsCountAndSum`
and `..._AggregateAliases_...` failed with `"Orders".Guid AS CustomerId` where `COUNT("Orders".Guid) AS OrderCount`
belonged. TASK-665 touches no SQL code; the same suite passed on the previous push and 3 of 3 times locally.

Cause: `DataBase.LoadView` ends by caching the built view's fields in `_fieldsCache[viewType]`, and its per-field loop
first looked a field up in that cache **by source column name**. `ViewCacheConcurrencyTests` loads `CustomerOrderView`
from 64 threads at once; a build still running found the finished one's fields and took the first named `Guid`
(Customers' `CustomerId`) for `OrderCount`. Whichever build was stored last decided the view every later test saw.

## Acceptance criteria

- [x] A view is built from its attributes alone; the column-name lookup in the field cache is gone
- [x] Deterministic test: rebuild a view while its fields are cached (red on HEAD 3/3); concurrent first loads of a
      fresh view all keep the aggregate (red on HEAD 1/3)
- [x] Every importer of `Birko.Data.SQL.View` green

## Outcome (2026-10-18)

The branch is removed. It was reachable only after `_fieldsCache[viewType]` was filled: by a concurrent build (this
defect), or by `LoadFields(viewType)`, which yields no fields for `[ViewField]` properties (TASK-651), so nothing
legitimate used it. `View.AddField` already tolerates the idempotent re-adds that remain. The CI run was re-run to
confirm it was this flake. Spec `views-and-aggregation` gains the requirement.

## Human test plan

N/A — library behaviour, covered by unit tests.

**Regression:** all 33 importers of `Birko.Data.SQL.View` green, live (Data.SQL 723, SqLite 583, MSSql 208, MySQL 196,
PostgreSQL 175, the four View suites, Migrations, ViewModel, Models.*, ...). Sandbox builds.
