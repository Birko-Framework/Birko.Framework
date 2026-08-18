---
id: TASK-245
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: unassigned
created: 2026-08-18
depends-on: []
blocks: []
related: [TASK-204, TASK-243]
findings: []
pr: ""
github-issue: null
jira-key: null
affects: [Birko.Data.SQL, Birko.Data.SQL.MySQL]
---

# MySQL cannot create any declared index — the framework emits syntax MySQL rejects

## The defect — measured end-to-end

`AbstractConnectorBase.CreateIndexSql` emits:

```sql
CREATE {UNIQUE }INDEX IF NOT EXISTS `name` ON `Table` (`Col`, ...)
```

**MySQL does not support `IF NOT EXISTS` on `CREATE INDEX`.** Measured on MySQL 8.4:

```
mysql> CREATE INDEX IF NOT EXISTS ix_probe ON MdlProbe (v);
ERROR 1064 (42000): You have an error in your SQL syntax; ... near 'IF NOT EXISTS ix_probe ON MdlProbe (v)'
```

MSSql overrides `CreateIndexSql` (it wraps the statement in
`IF NOT EXISTS (SELECT * FROM sys.indexes ...)`); PostgreSQL and SQLite both support the `IF NOT EXISTS`
clause. **MySQL is the one supported provider that neither overrides nor supports it**, so every declared
index on a MySQL entity fails to build.

End-to-end against MySQL 8.4, via `MySQLConnector.CreateTable`:

```
declared indexes = 1
index SQL        = CREATE INDEX IF NOT EXISTS `ix_idxrows_code` ON `IdxRows` (`Code`, `Other`)
FAILURE          = index 'ix_idxrows_code' on table 'IdxRows': <that statement>
present on table = (none)
```

Every `[IndexedField]`, `[CompositeIndex]` and fluent `HasIndex` on MySQL is therefore **absent** — no
index, and for a `UNIQUE` one, no constraint either.

## Why it has been shipping unnoticed

[[TASK-204]] deliberately made schema-ensure **record** an unbuildable index instead of throwing, because
an unbuildable index used to take down the entity's whole read surface. That is the right call and this is
its cost: on MySQL the failure lands in `AbstractConnector.IndexCreationFailures` and raises
`OnIndexCreationFailed`, and a host that subscribes to neither sees nothing at all. The degradation is not
silent by accident — it is silent because nobody is listening.

Not caught by tests either: there was no live MySQL suite in the tree until TASK-242, and the index tests
that exist (`ClassLevelCompositeIndexEndToEndTests`, `CompositeUniqueIndexEndToEndTests`) run on SQLite,
which accepts the clause.

## Shape of a fix

Override `CreateIndexSql` in `MySQLConnector`. MySQL has no conditional-DDL form for this, so the options
are:

1. **Probe then emit.** `SqlIndexManager.IndexExistsSql` already asks
   `information_schema.statistics` for exactly this, and `MySqlIndexManager` already declares the base
   implementation correct for MySQL — so the query exists and is the natural reuse. Costs one round trip
   per declared index per schema-ensure, which is per store instance, which with scoped stores is per
   request. Measure that before accepting it.
2. **Emit and tolerate duplicate-key-name (error 1061).** One statement, no probe; `CreateTable` already
   catches and records, so the change is to *not record* a failure whose only cause is "already there".
   Cheaper and closer to the `IF NOT EXISTS` semantics being emulated. Needs the error code checked
   against the driver, not the message.
3. **`ALTER TABLE ... ADD INDEX`** — same problem, no conditional form. Not a fix on its own.

Option 2 looks right; option 1 is the safer read if 1061 turns out not to be the only shape.

⚠ Whichever is chosen, the emitted statement is **DDL** and must go through `DoDdlCommand`
(TASK-243) — on MySQL it implicitly commits an open transaction, and index creation runs inside
schema-ensure, which is exactly the path that defect was about. `CreateIndexes` already routes through the
funnel today; a new MySQL override must not reintroduce a `DoCommand` bypass, which is precisely how
TASK-243's first attempt failed.

## Acceptance

- A gated MySQL test that declares a `[CompositeIndex]` (unique and non-unique), runs schema-ensure, and
  asserts the index is **present in `information_schema.statistics`** — not merely that nothing threw, and
  not merely that `IndexCreationFailures` is empty.
- The same assertion for the second schema-ensure on an already-indexed table: no failure recorded, no
  duplicate index, and — if option 1 is taken — a stated count of the extra round trips.
- A test that a genuinely unbuildable index (a `UNIQUE` over data that already violates it) is still
  **recorded rather than thrown**, so this fix does not undo TASK-204.
- Prove the index DDL still honours a transaction boundary on MySQL: declare the index, open a boundary,
  write, roll back, expect zero rows.
