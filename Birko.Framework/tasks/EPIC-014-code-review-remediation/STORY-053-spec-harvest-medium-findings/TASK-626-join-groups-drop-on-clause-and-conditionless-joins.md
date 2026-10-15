---
id: TASK-626
parent: STORY-053
feature: FEATURE-014
status: done
priority: P2
assignee: ai
created: 2026-10-15
depends-on: []
blocks: []
findings: [SH-M147, SH-M148]
pr: null
github-issue: null
jira-key: null
---

# A CROSS join with conditions loses its ON clause, and a join with no conditions disappears

## Context

Spawned by [[TASK-153]] (2026-10-15), which confirmed both findings by reading `AbstractConnectorBase`'s join
rendering and left them unfixed because the fix changes what existing joins render.

- **SH-M147.** `if (joingroup.Key.JoinType != JoinType.Cross && … Any())` renders ` ON (…)` only for non-cross joins,
  so a `JoinType.Cross` group's conditions are discarded. `Cross` is the **default** of both `Join.Create` overloads and
  the `Join` constructor, so a caller who supplies conditions and forgets the join type gets a cartesian product.
- **SH-M148.** `joingroups.Where(x => x.Value.Any())` skips a group with no conditions, so a conditionless CROSS JOIN,
  the only way to express one, vanishes from the FROM clause, and an INNER / LEFT join whose conditions failed to
  translate vanishes the same way.

## What to decide

1. A Cross join carrying conditions: refuse it, or treat the conditions as the ON of an INNER join? Refusing is the
   rule-42 answer; promoting changes the meaning of the declared join type.
2. Should the default join type stay `Cross`? A default that discards the caller's conditions is the trap itself.

## Acceptance criteria

- [x] A Cross join with conditions never renders without them: refused, or rendered with them, per the decision
- [x] A conditionless Cross join is emitted, measured against a real database (row count of the product)
- [x] An INNER / LEFT join whose conditions are empty is refused rather than dropped
- [x] Regression tests red before, green after, on SQLite and at least one server provider

## Decisions (2026-10-15, the user)

Refuse, with an Inner default: a Cross join carrying conditions is refused; a join given conditions and no type is Inner
(both `Join.Create` overloads and the constructor); a conditionless Cross renders `CROSS JOIN`; an Inner or LeftOuter join
with no conditions is refused; and a `[View]` declaring join keys with the default `ViewConnect.None` is an inner join.

## Resolution (2026-10-15)

**Found while deciding, worse than filed:** `DataBase.LoadView` started every attribute view's join as `Cross` and mapped
only `Check` (LeftOuter) and `CheckExisting` (Inner), so a `[View(A, B, "Guid", "AId")]` declared without `connect:`
attached its condition to a Cross join, the condition was dropped, and the view returned the cartesian product. No
consumer declares `[View]`; five framework test fixtures do, and none asserted a row count.

- **One producer for the keyword:** `Join.Keyword(type, hasConditions, left, right)`, used by both renderers
  (`AbstractConnectorBase.CreateSelectCommand` and `ViewSelectSqlBuilder`), which each kept their own copy of the switch.
  Both now render every join group: the `.Where(x => x.Value.Any())` that dropped a conditionless group is gone.
- `Join`'s constructor and the type-taking `Create` take `JoinType? = null` (Inner with conditions, Cross without); the
  condition-taking `Create` defaults to Inner. `ViewConnect.None` maps to Inner. `SqlViewTranslator` refuses an unknown
  portable join type instead of mapping it to Cross.
- Docs: `Birko.Data.SQL.View/README.md` documented attributes that do not exist (`ViewJoin`, `ViewColumn`); replaced with
  the real `[View]` / `[ViewField]` API and how its join type resolves.

**Measured live, in rows** (3 customers, 2 with an order): on SQLite, PostgreSQL, MySQL and SQL Server, a join given a
condition and no type is 2 rows (was 6, the product), the left join 3, a conditionless cross join 6 (was 3: the join was
skipped), Cross-with-conditions and Inner-without both refused, and the `[View]` with no `connect:` 2 (was 6).
**Red:** 16 of 16 fail on the old code.

**Regression:** the 32 suites importing Birko.Data.SQL, .View or .Views (2,690 tests), live on SQLite, PostgreSQL, MySQL, SQL Server and TimescaleDB, green.

## Human test plan

N/A: asserted by row counts.
