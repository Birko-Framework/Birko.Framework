---
id: TASK-129
parent: STORY-051
feature: null
status: todo
priority: P1
assignee: ai
created: 2026-07-31
depends-on: []
blocks: []
related: [TASK-128]
pr: null
github-issue: null
jira-key: null
findings: []
---

# An aggregate view's generated DDL carries a double alias, so no persistent aggregate view can be created

## Context

Found while writing TASK-128's persistent-aggregate test, and **CONFIRMED by running the generator** — no SH
id, so this is a remediation-discovered defect rather than a harvester claim.

`ViewSelectSqlBuilder.BuildViewSelectSql` (`../Birko.Data.SQL.View/SQL/Connectors/ViewSelectSqlBuilder.cs`)
emits, for a `Count` over a two-table join view:

```sql
SELECT VPersons.Name, COUNT(VOrders.PersonId) as COUNT AS "OrderCount"
FROM "VOrders" INNER JOIN "VPersons" ON ("VOrders"."PersonId" = "VPersons"."Guid")
GROUP BY VPersons.Name
```

`as COUNT AS "OrderCount"` — **two aliases on one column**. SQLite rejects it with
`SQLite Error 1: 'near "AS": syntax error'`, and it is a syntax error on every other provider too. So
**`CREATE OR REPLACE VIEW` for any view containing an aggregate fails**, which means a persistent (or `Auto`)
aggregate view can never be created — the capability is unreachable, not merely degraded.

**Two overlapping aliases, from two different fixes.** `Table.GetSelectFields(withName)`
(`../Birko.Data.SQL/SQL/Tables/Table.cs:30`) already appends `" as " + <Fields dictionary key>` for an
aggregate field, and `SqlViewTranslator` sets that key to the **SQL function name** (`COUNT`, `SUM`). Then
`BuildViewSelectSql` appends a second `" AS " + quoteIdentifier(field.Property.Name)` — the CR-L195 change
that aliases aggregates by view property so two aggregates of the same function cannot collide. CR-L195's
intent is the correct one; it just did not notice the inner alias was already being emitted.

Reached by all three callers of the shared builder, so the blast radius is every persistent-aggregate path:
`ViewSqlGenerator.GenerateCreateViewSql` (migrations), the base connector's view creation, and the SQL Server
`SCHEMABINDING` indexed/materialized-view builder.

**Self-reporting**, which is why this is P1 rather than P0: it fails loudly at view creation, so nothing
returns wrong rows. Contrast [[TASK-128]], whose sink was silent.

## Approach

Emit exactly one alias, and make it the view property (CR-L195's rule — it is the name
`GetPersistentViewSelectFields` queries back, so the two must agree or the persistent read breaks in the other
direction).

The fix is a choice about *which* producer stops aliasing, and the choice matters because
`Table.GetSelectFields` is shared with the on-the-fly select path, where the `as COUNT` alias is what
`View.GetSelectFields()` returns and what the row-materialisation reads positionally against. Check that
before changing it: dropping the inner alias to fix the DDL could break the on-the-fly read. Adding a
parameter (or having `BuildViewSelectSql` request an un-aliased projection) is likely safer than editing the
shared method's output.

Note that TASK-128 resolves an on-the-fly aggregate sort key to `GetSelectName(true)` — the
`COUNT(VOrders.PersonId)` expression, not either alias — so its behaviour is independent of this decision.

## Acceptance criteria

- [ ] `ViewSqlGenerator.GenerateCreateViewSql` emits valid DDL for a view containing `Count`, `Sum` and a
      second aggregate of the same function, asserted by **executing** it against SQLite, not by string match
- [ ] The aggregate column is queryable under the name `View.GetPersistentViewSelectFields()` returns, so a
      persistent aggregate view round-trips end-to-end
- [ ] The on-the-fly aggregate path still materialises rows correctly — the shared
      `Table.GetSelectFields` alias feeds it, so a change there must be proven not to break it
- [ ] The SQL Server SCHEMABINDING builder still produces its two-part table names
- [ ] TASK-128's `Persistent_aggregate_sort_*` tests drop their hand-written DDL and use the generator
- [ ] `/specs regen` for `views-and-aggregation`

## Out of scope

- View ORDER BY resolution — [[TASK-128]].
- The framework-wide identifier-quoting inconsistency noted in TASK-110's Outcome.

## Human test plan

N/A — covered by automated tests; the acceptance criterion is that generated DDL executes.
