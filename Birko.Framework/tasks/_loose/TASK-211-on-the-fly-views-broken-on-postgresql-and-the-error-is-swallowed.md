---
id: TASK-211
parent: null
feature: null
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P1
assignee: ai
created: 2026-08-14
depends-on: []
blocks: []
related: [TASK-209, TASK-110]
pr: null
github-issue: null
jira-key: null
findings: []
---

# On-the-fly views are broken on PostgreSQL — and the error is swallowed, so they return an empty result

## Context

Measured by [[TASK-209]] against real PostgreSQL 16.4 (2026-08-14) while fixing the *persistent* view path.
Two defects, and **the second is worse than the first**.

**1. The same qualifier mechanism, through a different builder.** TASK-209 fixed the view **DDL** builder
(`ViewSelectSqlBuilder`) and the persistent read. The **on-the-fly** path composes its SQL in
`AbstractConnector_CreateSelectCommand` → `view.GetSelectFields()` and emits, verbatim from the server log:

```sql
SELECT PgPersons.Name, COUNT(PgOrders.PersonId) as OrderCount, SUM(PgOrders.Amount) as TotalAmount
  FROM "PgOrders" INNER JOIN "PgPersons" ON (PgOrders.PersonId = PgPersons.Guid)
  GROUP BY PgPersons.Name ORDER BY PgPersons.Name ASC
-- ERROR: missing FROM-clause entry for table "pgorders" at character 143
```

The `FROM` quotes the table; the projection, the `ON` clause, the `GROUP BY` and the `ORDER BY` do not. On
PostgreSQL the bare qualifier folds to `pgorders` and does not match the quoted relation. The fix is the one
TASK-209 established — **quote tables, never quote columns** — applied to this builder;
`Table.GetSelectFields` already accepts a `quoteTable` delegate for exactly this (added by TASK-209), so the
projection is a matter of passing `QuoteIdentifier`. The join / group / order emitters need the same.

**2. The exception is swallowed and the caller gets an empty collection.** This is the part to fix first.
The `PostgresException` above never reaches the caller: `SqlViewStore.QueryAsync` returned **zero rows** with
no error. A view that cannot execute reports "no results" — indistinguishable from a view that legitimately
matches nothing, which is a plausible wrong answer rather than a failure, and top of this project's severity
ladder. It is also why the breakage was invisible: nothing anywhere went red.

The same swallowing hides `CreateView` failures. TASK-209's first draft of
`A_persistent_view_is_created_on_postgresql` asserted `create.Should().NotThrow()` and **passed against the
unfixed code**, because `CreateView` swallowed `42P01` and reported success. That test now asks
`information_schema.views` instead. Any assertion of the form "the view operation did not throw" is
worthless until this is fixed.

**Scope note.** `AbstractConnector_Select.cs:95` / `AbstractAsyncConnector_Select.cs:95` call the same
`Table.GetSelectFields(true)` for ordinary **multi-table joined SELECTs**, so those are broken on PostgreSQL
by the identical mechanism. Not yet measured end-to-end, but it follows from the same emitter — check it as
part of this task rather than filing a third time. This is the framework-wide residue TASK-110's Outcome
first noted and TASK-209 deliberately did not cross.

## Approach

1. **Find and fix the swallowing first**, and give it its own test. Until an error surfaces, every other fix
   here is unverifiable by the usual means — that is how both defects survived. Decide deliberately whether
   the swallow is ever legitimate (`ViewExists` probing is the one place it plausibly is, per CR-M149) and
   narrow it to that.
2. Pass `QuoteIdentifier` into the on-the-fly projection via the `quoteTable` parameter TASK-209 added.
3. Bring the on-the-fly `ON` / `GROUP BY` / `ORDER BY` emitters onto the same rule.
4. Then check the plain joined SELECT path and fix it the same way.

## Acceptance criteria

- [ ] An on-the-fly view over PascalCase models round-trips on **real PostgreSQL** — reproduce first, and
      record how the server was run (TASK-209's § Measured section has a no-Docker recipe that works)
- [ ] A view query whose SQL is rejected by the server **throws** rather than returning an empty collection,
      with a test that fails if the swallow returns
- [ ] The `PostgreSqlViewRoundTripTests` class doc's "on-the-fly is still broken" paragraph is removed and
      replaced by an executing assertion — it exists only because this was out of scope
- [ ] Multi-table plain `SELECT`s are checked on PostgreSQL and either fixed here or filed with evidence
- [ ] Red-verified; split as numbers, contract pins named as pins
- [ ] Full SQL suite sweep (TASK-209 touched 13 suites; the same set applies)
- [ ] `/specs regen views-and-aggregation`

## Out of scope

- The persistent view path — [[TASK-209]] closed it and this task must not regress it.
- Adding a permanent PostgreSQL CI tier (STORY-042's Docker tier). This needs one reproduction.

## Human test plan

N/A — a query either returns the right rows against a live PostgreSQL or it does not, which an automated
test observes directly.

## Implementation plan

_Populated by `/tasks plan TASK-211` — leave empty until then._
