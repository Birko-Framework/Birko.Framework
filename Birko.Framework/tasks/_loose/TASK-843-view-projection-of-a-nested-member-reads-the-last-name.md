---
id: TASK-843
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P2
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-07
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# A view that projects a nested member reads the field named by the last segment only

## Context

Found by TASK-840 (2026-11-07), from reading the code; not yet reproduced.
- `ViewDefinitionBuilder.GetMemberName` (Birko.Data.Views) keeps only the last member name. A projection
  `Select(r => r.Sub.X, ...)` is recorded as source field `X`, so Cosmos DB projects `c["X"]`, a different field or
  none.
- It is in the shared view definition, so the SQL, MongoDB and Elasticsearch view stores probably read the wrong
  column or field the same way.
- The answer is wrong and nothing is raised. This is the projection-side twin of TASK-840, which fixed the
  filter side for Cosmos DB.

## Acceptance criteria

- [x] Reproduced first, per provider that has a view store (Cosmos DB live, SQLite in-process, and others where a
      live suite exists): a view projecting a nested member returns the wrong value today. Record it.
      → On HEAD, 2026-11-07, with a top-level X/Name/Level beside different Sub.X/Name/Deep.Level values:
        - **SQLite:** SubX read 7, 6, 5 (the top-level X) where the nested values were 0, 1, 2.
          `SqlViewManager.EnsureAsync` created the wrong view silently.
        - **Cosmos DB, MongoDB, RavenDB, Elasticsearch (live):** every nested view property read the same-named
          top-level field. SubX read 10–15 for 5–0, SubName read "top0", and the two-level Level read 100+.
        - Filters and sorts on SubX used the top-level field. MongoDB's `Sub.Id` read an empty Guid.
- [x] A nested projection reads the full path where the provider can express it, or is refused by name when the view
      is defined (never silently flattened). One producer for the path (rule 16), shared by every provider.
      → `ViewDefinitionBuilder.GetMemberPath` is the producer. `FieldSelector` gains `SourcePath` and `IsNested`;
        `SourceProperty` is the dotted path, so a single segment is unchanged.
      → Only `Select` takes a nested path. Join keys, group keys, aggregate sources and view properties refuse one by
        name, as do `Length`, `Count`, struct members and captured variables.
      → Per provider:
        - **SQL:** refused when the store or view manager receives the definition; a table has no nested member.
        - **Cosmos DB:** projects `c["Sub"]["X"]` (`CosmosAggregationHelper.PathRef`); filters and sorts read
          `c.Sub.X`.
        - **MongoDB:** projects `$Sub.X`, each segment named by the driver's class map, so `[BsonElement]` names and
          `_id` come out right.
        - **RavenDB:** the index map reads the path with a null guard. Without it, a null `Sub.Deep` fell back to the
          top-level `Level` (measured 103).
        - **Elasticsearch:** reads and sorts use `sub.x`. A filter on a nested view property refuses by name, since
          the entity translator cannot express it (SH-M142).
      → **Deviation:** `c => c.Guid!.Value` is now recorded as `Guid`, matching TASK-840's filter rule; it used to be
        `"Value"`, which one test pinned and which is now changed. No consumer uses `ViewDefinitionBuilder`.
      → **Stated limits (refused, not wrong):** nested join keys, group keys and aggregate sources; Elasticsearch
        nested filters.
- [x] Regression tests red before the fix, one per provider covered.
      → `tests/*/Task843*.cs` (6 files). Against HEAD: SQL 3, Cosmos DB 3, MongoDB 3, RavenDB 2 and Elasticsearch 3
        failed; the RavenDB null-guard test was red without the guard.
      → Suites live: CosmosDB.Views 96, CosmosDB 200, ElasticSearch.Views 30, MongoDB.Views 19, RavenDB.Views 13,
        SQL.Views 146 (PostgreSQL, SQL Server, MySQL), SQL.PostgreSQL.View 22, Views 49.
      → **Found, filed:** `ModelMap.GetMemberName` (Birko.Models.SQL) flattens `map.Property(x => x.Sub.X)` to `X`
        the same way → TASK-845.

## Out of scope

- Filters on nested view properties: TASK-840 fixed Cosmos DB's.

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
