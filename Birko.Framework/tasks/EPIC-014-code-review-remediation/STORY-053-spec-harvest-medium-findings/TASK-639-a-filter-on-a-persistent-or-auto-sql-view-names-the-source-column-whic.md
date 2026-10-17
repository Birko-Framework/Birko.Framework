---
id: TASK-639
parent: STORY-053
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P1
assignee: ai
created: 2026-10-16
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# A filter on a Persistent or Auto SQL view names the source column, which the view does not have

## Context

Found by the batched spec regeneration of 2026-10-16 (17 areas, harvested by reading the code), area `views-and-aggregation`. **A
suspected defect from reading, not a measurement: reproduce it first** (rule 54). If it does not reproduce, close this
as refuted with the measurement.

A consequence of [[TASK-151]] (SH-M405). `SqlViewStore`'s scoped filter resolver returns
`field.GetSelectName(withTableName)`, the source column, usually table-qualified (`Persons.Name`)
(`Birko.Data.SQL.Views/SqlViewStore.cs:121-130`). The persistent read path applies those conditions to
`SELECT … FROM "<view>"` (`Birko.Data.SQL.View/SQL/Connectors/AbstractConnectorBase_View.cs:91`), whose columns are the
view-property aliases. TASK-151's tests covered the filter on OnTheFly only.

## Acceptance criteria

- [x] Reproduced (or refuted, with the measurement)
- [x] A filter on a renamed view property works on a Persistent and an Auto view, live on the four SQL providers
- [x] Red on the current code; the spec `docs/specs/views-and-aggregation.md` updated if behaviour changes

## Resolution (2026-10-17)

**Reproduced live on SQLite, PostgreSQL, MySQL and SQL Server, 16 of 16 red.** Two views over seeded persons and orders:
one renames `Person.Name` to `PersonName` (single source), and one joins and groups. Each was filtered and counted on
`PersonName` in all three modes. OnTheFly passed (8 of 8). Persistent, and Auto once `SqlViewManager.EnsureAsync` had
created the view, failed on every provider: "no such column: T639Persons.Name", 42P01 "missing FROM-clause entry for
table t639persons", "Unknown column 'T639Persons.Name' in 'where clause'", and "The multi-part identifier could not be
bound". `SqlViewStore.ParseFilter` resolves a view property to `field.GetSelectName(withTableName)`, the column the
on-the-fly SELECT reads. The persistent command puts that WHERE under `FROM "<view>"`, whose columns are the view
properties. The attribute-declared path (`SelectView(Type, …)`, `SelectCountView`) failed the same way (4 of 4),
because it parses through the same funnels.

**Fix: one resolution point beside TASK-128's.** The four view funnels (`Select` and `SelectCount`, sync and async)
already resolved sort keys once `usePersistent` was known. They now also call `DataBase.ResolveViewConditions(view,
conditions, usePersistent)` (new `Birko.Data.SQL.View/SQL/DataBase_ViewConditions.cs`). On the persistent path, each
source name a view field knows, qualified or not, becomes that field's persistent column (`Property.Name`, as
`ViewOrderFieldName` spells it). The rename applies to the other column of a field-to-field comparison and inside
groups too. A name that is already a persistent column, or that no field knows, passes through. An unqualified name
two fields share with different columns is refused (`ArgumentException`), rather than filtering on whichever came
first. The caller's conditions are copied, not modified, and the on-the-fly path is untouched. Resolving in the store
instead was rejected: Auto decides inside the connector, and only a positive existence check is cached (CR-M149), so
a store-side guess could disagree with the path the connector then takes.

**Red check:** with the resolver neutralised, 23 tests went red: 16 view-store runs, 4 attribute-declared runs and 3
resolver unit tests. The 2 pass-through tests are pins. `Birko.Data.SQL.Views.Tests` now imports the three server
providers and their `.View` partials, so its live twins run under `BIRKO_*_HOST`.

**Docs:** the spec `views-and-aggregation` (a new scenario under *SQL view store execution*), `docs/views.md`,
`Birko.Data.SQL.View/CLAUDE.md`, and Recent Updates.

**Regression:** the 32 suites that import `Birko.Data.SQL.View` were run live against PostgreSQL, MySQL and SQL Server
(TimescaleDB too): 2,800 passed and 1 failed. The failure was
`Birko.Data.SQL.MySQL.Tests.EscapeAnomalyDiscriminationLiveTests.TASK295_a_vanished_table_heals_on_the_next_write_here_too`,
which touches no view code. It did not reproduce: 3 isolated runs and 2 full-suite runs (190/190) were green. It is
recorded here as an unexplained one-off, not adopted as a flake hypothesis (rule 54).

## Human test plan

N/A: a library contract, asserted by tests.

## Implementation plan

_Populated by `/tasks plan TASK-639` — leave empty until then._
