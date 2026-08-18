---
id: TASK-248
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P1
assignee: unassigned
created: 2026-08-18
depends-on: []
blocks: []
related: [TASK-245, TASK-204]
findings: []
pr: "Birko.Data.SQL 022c67c | Birko.Data.SQL.MySQL 73e1cfd"
github-issue: null
jira-key: null
affects: [Birko.Data.SQL, Birko.Data.SQL.MySQL]
---

# MySQL cannot index an unbounded `string` column — and that is the canonical documented pattern

## Context

Spawned from [[TASK-245]], which fixed the *syntax* half of "MySQL builds no declared index"
(`CREATE INDEX IF NOT EXISTS` → ERROR 1064). This is the remaining half, a different cause needing a
different fix, and it was found while choosing that task's test model.

A `string` property with no length attribute maps to **`LONGTEXT`** on MySQL
(`MySQLConnector.ConvertType`, `Birko.Data.SQL.MySQL/Database/Connectors/MySQLConnector.cs:224-238` —
`CharField` → `VARCHAR(n)`, everything else → `LONGTEXT`). **MySQL cannot index a BLOB/TEXT column without
a key length.** Measured on MySQL 8.4:

```
CREATE TABLE TxtIdx (Guid CHAR(36), TenantGuid CHAR(36), Number LONGTEXT, Status LONGTEXT);

CREATE INDEX ix_txt ON TxtIdx (Status, Number);
  ERROR 1170 (42000): BLOB/TEXT column 'Status' used in key specification without a key length
CREATE UNIQUE INDEX ux_txt ON TxtIdx (TenantGuid, Number);
  ERROR 1170 (42000): BLOB/TEXT column 'Number' used in key specification without a key length
CREATE INDEX ix_txt_len ON TxtIdx (Status(64));
  (succeeds)
```

Unique and non-unique alike. So after TASK-245 an index over a bounded string (`[MaxLengthField(64)]`) or
any other type builds correctly on MySQL, and an index over an **unbounded** string still does not — it
merely fails with 1170 instead of 1064.

**This is the canonical documented pattern, not a corner case.** The reference end-to-end test for composite
unique indexes, `Birko.Data.SQL.SqLite.Tests/CompositeUniqueIndexEndToEndTests.cs`, declares exactly this
shape:

```csharp
[IndexedField("ux_docnum", 0, IsUnique: true)] public Guid TenantGuid { get; set; }
[IndexedField("ux_docnum", 1, IsUnique: true)] public string Number { get; set; } = null!;   // <-- LONGTEXT
```

That test passes on SQLite, which has no such restriction. Anyone following it on MySQL gets no index and,
for the unique case, **no constraint** — recorded in `IndexCreationFailures`, thrown nowhere, which is the
TASK-204 degradation working as designed and being invisible for the usual reason.

The boundary is currently pinned by
`Birko.Data.SQL.MySQL.Tests/DeclaredIndexLiveTests.An_index_over_an_unbounded_string_is_still_unbuildable_on_mysql`,
which asserts error **1170** specifically. That test fails if this ever starts working — deliberately, so
the boundary cannot move silently. **Closing this task means updating that test**, not deleting it.

## Shape of a fix — three candidates, none free

1. **Refuse at table load.** An `[IndexedField]`/`[CompositeIndex]` naming an unbounded string throws
   `FieldAttributeException` naming the property, exactly as § SH-H037 requires for an unmappable property.
   Portable (the declaration is wrong on MySQL regardless of which provider is running today) and loud, but
   it **breaks any consumer model that currently declares this** — and on SQLite/PostgreSQL those indexes
   work fine right now, so this converts working deployments into start-up failures. Blast radius must be
   measured before choosing it.
2. **Emit a key length on MySQL.** `MySQLConnector.CreateIndexSql` appends `(n)` for a TEXT-typed column.
   Needs a length from somewhere: MySQL's index-key limit is 3072 bytes on InnoDB/DYNAMIC, so a prefix must
   be chosen, and a *silently truncated* unique constraint is worse than no constraint — `ux(Number(64))`
   rejects two rows whose first 64 characters match but which are genuinely different. **Do not do this for
   a UNIQUE index** without deciding that explicitly.
3. **Map an indexed string to `VARCHAR` instead of `LONGTEXT`.** The field is known to be indexed at DDL
   time, so `AbstractField` could carry that and MySQL could pick `VARCHAR(255)`. Changes the column type of
   existing tables (a migration), and `CREATE TABLE IF NOT EXISTS` will not alter one that already exists —
   so it fixes new deployments and silently does nothing for old ones, which is its own trap.

Option 1 is the honest default (a declaration that cannot be honoured should not be accepted quietly);
option 3 is the one that makes the pattern *work*. They are not exclusive — 1 for the unique case, 3 for the
plain one, is a defensible split. Decide explicitly rather than picking the cheapest.

## Acceptance criteria

- [x] A decision recorded in the task body naming which option was taken, per index kind (unique vs plain),
      with the blast radius of option 1 measured against the `Birko.Models.*.SQL` domain suites and every
      in-tree model carrying `[IndexedField]`/`[CompositeIndex]` on an unbounded string — the same clearance
      § SH-H037 demanded before turning silence into a throw.
- [x] A gated MySQL test that the chosen behaviour holds end-to-end: either the declaration is refused at
      table load with a message naming the property and the fix, or the index is **present in
      `information_schema.statistics`** and (if a prefix was used) the prefix length is asserted.
- [x] If a prefix is emitted for a UNIQUE index, a test that documents the truncation semantics — two rows
      differing only after the prefix are **rejected** — so the weaker-than-declared constraint is a recorded
      decision rather than a surprise.
- [x] `An_index_over_an_unbounded_string_is_still_unbuildable_on_mysql` updated, not deleted: it is the
      boundary marker and must now assert the new behaviour.
- [x] The other three providers are unaffected, asserted rather than assumed — SQLite, PostgreSQL and MSSql
      all index a TEXT column without a key length today and must keep doing so.
- [x] Prove each test can fail, with counts.
- [x] `CompositeUniqueIndexEndToEndTests`' model reviewed: if the canonical example is unusable on MySQL it
      should say so or change, because it is what consumers copy.

## Out of scope

- The `IF NOT EXISTS` syntax defect and the identifier quoting — [[TASK-245]], landed.
- Non-string BLOB columns (`byte[]` → `LONGBLOB`) have the same 1170 restriction. Worth handling in the same
  pass if the chosen option generalises, but measure rather than assume — nothing in the tree declares an
  index over a `byte[]` today.
- MySQL's 3072-byte index-key limit for *bounded* columns (a `VARCHAR(1000)` composite can exceed it) is a
  separate ceiling and a separate task if it is worth one.

## Human test plan

**N/A — covered by automated tests.** Every claim is a catalogue fact (column type, column length, index
presence) or an engine behaviour (the UNIQUE write is refused). Verified against live MySQL 8.4.

## Outcome — DECISION: bound the column, on MySQL only

**The decision the first acceptance criterion demanded, and the measurement that produced it.**

### Blast radius, measured before choosing

| where | indexed unbounded strings | note |
|---|---|---|
| `Birko.Models.*.SQL` domain models | **0** | the framework declares no index attributes at all |
| Consumer production entities | **7**, all UNIQUE | Symbio: `Order.OrderNumber`, `ProductionOrder.OrderNumber`, `JournalEntry.EntryNumber`, `ExportBatch.BatchNumber`, `ImportBatch.BatchNumber`, `InventoryCount.DocumentNumber`, `CustomerAccount.Email` |
| Birko test models | 11 | |

All seven consumer entities **work correctly on PostgreSQL today** — they are the same
`(TenantGuid, <Number>)` composites CLAUDE.md cites from the TASK-204 incident.

### The decision, per index kind

**Both kinds: option 3, scoped to MySQL.** `MySQLConnector.ConvertType` emits `VARCHAR(255)` instead of
`LONGTEXT` when `AbstractField.IsIndexed` is set. Same treatment for UNIQUE and plain — the distinction the
task anticipated turned out not to matter once the column is bounded rather than the index prefixed.

- **Option 1 (refuse at table load) was measured and rejected.** It would convert all seven live entities into
  start-up failures on a provider where their indexes are correct. § SH-H037's fail-fast is right when a
  declaration cannot work *anywhere*; this one works on three of four providers, so the unhonourable thing is
  the provider's limit and that is where it is absorbed.
- **Option 2 (prefix index) rejected on the semantics.** Every real case is UNIQUE, and `ux(Col(64))` makes
  the constraint **weaker than declared** — silently refusing two genuinely different values sharing a prefix.
  A bounded column refuses the over-long *write* instead: loud, and visible to the caller.
- **Option 3 framework-wide rejected too.** Bounding on PostgreSQL/SQLite/MSSql would impose a 255-character
  ceiling where none exists today, so a longer indexed value that writes fine now would start failing —
  breaking three working providers to fix one.

`byte[]` (`LONGBLOB`) is left recorded-not-thrown: same 1170 restriction, nothing in the tree declares an
index over one, and inventing a bound for a case with no instances is speculation.

### Verification

**1,170 tests green across 19 suites**, live MySQL 8.4, including the five `Birko.Models.*.SQL` domain suites
the criterion named for clearance. 6 new tests.

| revert | result | proves |
|---|---|---|
| **M** drop the `ConvertType` VARCHAR arm | **2 of 68** fail | the emitter half |
| **O** drop the composite-site `IsIndexed` marking | **2 of 68** fail | `[CompositeIndex]` resolution |
| **N** drop the per-property `IsIndexed` marking | **1 of 68** fail | `[IndexedField]` resolution — **0 of 67 before a test for that attribute form existed** |

**Revert N is the finding inside the fix.** `DataBase.LoadIndexes` resolves index columns to fields at two
points, one per attribute form, and every model in the first version of the suite used `[CompositeIndex]` — so
half the fix was unproven and a future edit could have silently broken it. Adding `IdxTextAttrRow` made the
revert fail exactly 1. A revert that fails nothing is a missing test, not a redundant fix; second instance in
three days after TASK-245's async-site revert.

### Boundary marker and the canonical example

`An_index_over_an_unbounded_string_is_still_unbuildable_on_mysql` was **renamed and rewritten**, not deleted,
to `An_index_over_an_unbounded_string_is_now_buildable_on_mysql` — it asserts the column type *beside* the
index, because the index building is a consequence of the column type and asserting only the index would not
record why it works.

`CompositeUniqueIndexEndToEndTests.UxDoc` — the example consumers copy — now carries a portability note and
**deliberately stays unbounded**, so the automatic path keeps being exercised by the shape people actually
copy. `[MaxLengthField(n)]` is still documented as preferable: portable, visible at the model, and applied on
every provider rather than by one connector's default.

### Notes

- The other three providers are **asserted** unaffected, not assumed: each has an offline test that an indexed
  unbounded string still maps to TEXT, and that an explicit length still produces its own type.
- **The blast-radius survey was wrong twice before it was right.** The consumer entities declare their
  attributes fully qualified (`[Birko.Data.SQL.Attributes.CompositeIndex(...)]`), so an unqualified
  `[CompositeIndex(` grep found none of them; a hand-rolled property parser then mis-classified them as
  bounded. Both corrections came from checking one known instance by hand. A survey that under-reports reads
  exactly like a clean bill of health — verify the count against a known case before trusting it.
- `IndexedStringColumnLength` is `protected virtual` so a consumer can raise it, with the note that InnoDB's
  3072-byte index-key limit is the real ceiling rather than that number.
