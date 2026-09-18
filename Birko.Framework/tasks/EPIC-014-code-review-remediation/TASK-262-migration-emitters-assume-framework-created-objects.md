---
id: TASK-262
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P2
assignee: ai
created: 2026-08-18
depends-on: []
blocks: []
related: [TASK-209, TASK-253, TASK-472, TASK-271, TASK-272]
findings: []
pr: "Birko.Data.SQL e1bce19 · Birko.Data.SQL.MSSql 8cc7313 · Birko.Data.SQL.MySQL 08c6fa4 · Birko.Data.Migrations.TimescaleDB 9b7f943 · tests: Birko.Data.SQL.Tests 526370f · Birko.Data.Migrations.TimescaleDB.Tests 1f4eb7f"
github-issue: null
jira-key: null
affects: [Birko.Data.Migrations.TimescaleDB]
---

# The migration emitters' identifier rules assume this framework created the object — twice over

Both halves came out of **[[TASK-253]]**'s close-gate `code-review`, and both are regressions **that task
introduced**. They are grouped because they are one premise failing in two directions: the quoting and folding
rules were derived from what `AbstractConnector.CreateTable` provably emits, and the *migrations* layer hands
those rules names for objects it did not necessarily create.

Neither is firing: `Birko.Data.Migrations.TimescaleDB` has exactly **one** importer in the family
(`Consumers/Birko.Sandbox`), and TASK-247's sweep of all 16 consumer repos found **0** migration-declared usage.
That measurement is why TASK-253 closed rather than holding — but they are traps laid for the next author, and
the API is public.

## 1. A schema-qualified name is now one identifier containing a dot

`RegclassLiteral("reporting.evts")` emits `'"reporting.evts"'`, and `QuoteIdentifier("reporting.evts")` emits
`"reporting.evts"` — in both cases a *single* identifier whose name literally contains a period.

**Measured on TimescaleDB 2.29.2 / PostgreSQL 16:**

```
SELECT create_hypertable('reporting.evts','ts');      -- (82,reporting,evts,t)   works
SELECT create_hypertable('"reporting.evts2"','ts');   -- ERROR: relation "reporting.evts2" does not exist
```

So `CreateHypertable(ctx, "reporting.evts", "ts")`, `AddCompressionPolicy(ctx, "reporting.evts", …)`,
`AddRetentionPolicy`, `RemoveCompressionPolicy`, `RemoveRetentionPolicy`, `RefreshContinuousAggregate` and
`CreateContinuousAggregate`'s view/source arguments all worked before TASK-253 and now raise `42P01`.

**Why the store path is unaffected**, and why that made this invisible: it takes its table name from
`Table.Name`, which is never schema-qualified. Schema qualification is idiomatic in a *migration* and nowhere
else in the changed surface — so the premise TASK-472 established one layer down does not transfer, and TASK-253
carried it over without noticing.

## 2. The fold and the quote assume framework-emitted DDL, which a raw-SQL migration is not

`CatalogueNameLiteral`'s pre-fold is justified in `TimescaleDBConnector` because `CreateTable` *provably* emits
column definitions bare. In the migrations layer an author can create the object with hand-written SQL — via
`SqlScriptMigration` or any raw `ExecuteScript` — and then the premise is simply false. Two regressions, in
opposite directions:

- **A quoted mixed-case column becomes unaddressable.** `CREATE TABLE metrics ("Timestamp" timestamptz)` then
  `CreateHypertable(ctx, "metrics", "Timestamp")` used to work; the fold now emits `'timestamp'` → `42703`, and
  there is no spelling of the argument that reaches the real column.
- **A bare-created table can no longer be referenced by its source spelling.** `CREATE TABLE Metrics (…)`
  stores `metrics`; `CreateContinuousAggregate(ctx, "Agg", "Metrics", …)` used to work through the parser's own
  folding and now emits `FROM "Metrics"` → `42P01`.

Objects created through `SqlSchemaBuilder` **are** safe — it quotes tables and emits columns bare, so the new
rules are exactly right for them. That is why this is the smaller half.

## Measurement — done 2026-08-21 on live TimescaleDB 2.29.2 / PostgreSQL 16.15

The exact version this file's original measurements were taken on. All three sub-regressions reproduce, and
two further measurements settle the shape of the fix.

### Both halves reproduce

| Emitted today | Result |
|---|---|
| `create_hypertable('"reporting.evts2"','ts')` | **42P01** `relation "reporting.evts2" does not exist` |
| `create_hypertable('reporting.evts','ts')` (bare, i.e. pre-TASK-253) | works — hypertable 31 |
| `create_hypertable('metrics','timestamp')` — folded, against a hand-created `"Timestamp"` | **42703** `column "timestamp" does not exist` |
| `create_hypertable('metrics','Timestamp')` — unfolded | works — hypertable 32 |
| `SELECT 1 FROM "Metrics2"` — quoted, against a bare-created `Metrics2` | **42P01** `relation "Metrics2" does not exist` |
| `SELECT 1 FROM Metrics2` — bare | works |

### Per-part quoting is the answer for half 1, and it is strictly better than the pre-TASK-253 behaviour

Quoting each dot-separated part independently works for every shape, including the ones a bare name could
never reach:

| Emitted | Result |
|---|---|
| `'"reporting"."evts3"'` | hypertable 33 |
| `'"reporting"."Evts4"'` — **mixed-case table in a schema** | hypertable 34 |
| `'"Rep Ort"."Ev ts"'` — **spaces in both parts** | hypertable 35 |
| `'"a.b"'` — whole-name quoted (today's behaviour) | hypertable 36 |

So the trade is explicit: splitting on **unquoted** dots gains schema qualification (plus mixed-case and
spaces *within* a qualified name, which the pre-TASK-253 bare form also could not do) and loses the ability to
address a table whose name literally contains a dot — **unless the caller quotes it themselves**, which the
unquoted-dot rule preserves, since `"a.b"` is then one part.

**Blast radius of that trade, measured rather than assumed: 0.** Of **317** distinct `[Table("…")]`
declarations across the framework, its tests and all 16 consumer repos, **none** contains a dot.

### Why half 2 cannot be fixed by inference — the constraint that decides it

`RegclassLiteral` and `CatalogueNameLiteral` have **two callers with opposite premises**:

- **`Birko.Data.TimescaleDB/…/TimescaleDBConnector.cs:168-169`** — the *store* path (TASK-472's fix). Its
  table name is `Table.Name` and its column is a mapped property, so an unquoted input here means *"the
  quoted identifier this framework created"*. Today's whole-name quote and pre-fold are **correct** for it.
- **`Birko.Data.Migrations.TimescaleDB/TimescaleDBMigration.cs`** (7 sites) — the *migration* path. Its names
  come from the author, who may qualify them and may be naming a hand-created object.

The tempting unified rule — *interpret the caller's string as SQL identifier syntax, so unquoted folds and
quoted is literal* — **would re-break TASK-472**: the store passes `Widgets` unquoted, meaning the quoted
`"Widgets"` that `CreateTable` created, and folding it returns to `42P01` with the swallow that hid it. So an
unquoted name cannot be made to mean "fold me" without undoing the defect this family started with.

Half 1's fix is unaffected by that constraint (`Widgets` has no dot → one part → quoted exactly as today), so
it is safe and additive. **Half 2 is therefore a genuine API decision, not a bug fix**: either the emitters
keep assuming framework-created objects and say so as a precondition, or an author who created the object by
hand needs an explicit way to say that.

## Acceptance criteria

- [x] A decision on schema qualification. → **Yes, supported**, via a new one producer
      `AbstractConnectorBase.QualifiedIdentifier`: split on **unquoted** dots, quote each part.
      **All four positions** go through it — `RegclassLiteral` (5 emitter sites) now composes on top of it, and
      the three bare-SQL positions (`ALTER TABLE`, `CREATE MATERIALIZED VIEW`, `FROM`) were retargeted from
      `QuoteIdentifier`. Chosen over reverting to a bare name because per-part quoting is **strictly more
      capable**: measured, `'"reporting"."Evts4"'` and `'"Rep Ort"."Ev ts"'` each created a hypertable, and the
      bare form reaches neither.
      **The trade, stated:** splitting gives up a table whose name literally contains a dot — *unless the
      caller quotes it*, which the unquoted-dot rule preserves (`"a.b"` is one part). Blast radius measured at
      **0 of 317** `[Table("…")]` declarations across the framework, its tests and all 16 consumer repos.
      `QuoteIdentifier` itself is **unchanged** — it is used framework-wide for single identifiers including
      columns, and splitting there would alter table quoting everywhere.
- [x] A decision on the raw-SQL premise. → **Kept as a precondition, and the remarks rewritten to say what
      is now true.** They previously described both halves as TASK-262's problem; they now record that
      qualification *is* supported and that the surviving limit is specifically about **columns**: a column
      created quoted and mixed-case cannot be addressed, because `CatalogueNameLiteral` pre-folds.
      **Why not an opt-out:** the fold cannot be made conditional on the caller's spelling, because these
      producers are shared with the *store* path (`TimescaleDBConnector.CreateHypertableSql:168-169`) where an
      unquoted name means "the quoted identifier this framework created" — TASK-472's premise. Teaching
      unquoted to mean "fold me" would re-break that defect, and it was invisible precisely because the
      failure is swallowed. With **0** emitter call sites across 16 consumer repos, an explicit opt-out on
      seven methods is speculative API rather than a missing capability; the remarks name it as the shape to
      add if a real caller appears.
- [x] Verified against live TimescaleDB. → `QualifiedNameEmitterLiveTests` (4), every assertion reading
      `timescaledb_information` with case intact and **matching on schema AND name**, so a hypertable created
      in the wrong schema cannot pass. Covers a qualified table, a qualified name whose both parts need
      quoting (spaces + mixed case), a *policy* emitter through the same producer, and `IsHypertable`'s
      catalogue probe. The hand-created quoted mixed-case **column** is not made to work — that is the
      precondition above; it is documented rather than tested as passing, which is the honest record.
- [x] Proven able to fail. → Reverting `QualifiedIdentifier` to whole-name quoting fails **4 of 52** — all
      four new live tests — while the **48 pre-existing tests stay green**, which is what shows the fix is
      additive and that the new tests are what witness it. Offline, 12 new cases in
      `LiteralIdentifierProducerTests` cover the producer itself.
- [x] The blast radius re-measured rather than inherited from this file.
      → **Unchanged, still latent.** One importer of `Birko.Data.Migrations.TimescaleDB` in the family
      (`Consumers/Birko.Sandbox`, an aggregator that imports everything) and **0** call sites of any of the
      seven emitters across all consumer repos. Separately measured: 0 of 317 `[Table]` names contain a dot,
      which is what makes half 1's trade free.

## Verification

Live **TimescaleDB 2.29.2 / PostgreSQL 16.15** — the version this task's measurements were taken on — plus live
SQL Server 2022, PostgreSQL 16, MySQL 8.4 and on-disk SQLite, with `BIRKO_REQUIRE_LIVE` set throughout.
**1,225 passed, 0 failed** across nine suites; **16 new**.

| Suite | Result |
|---|---|
| `Birko.Data.Migrations.TimescaleDB.Tests` | 52 (was 48) |
| `Birko.Data.SQL.Tests` | 587 (was 575) |
| `Birko.Data.TimescaleDB.Tests` | 44 |
| `Birko.Data.SQL.MSSql.Tests` | 87 |
| `Birko.Data.SQL.MySQL.Tests` | 78 |
| `Birko.Data.SQL.PostgreSQL.Tests` | 82 |
| `Birko.Data.SQL.SqLite.Tests` | 229 |
| `Birko.Data.Migrations.SQL.Tests` | 49 |
| `Birko.Data.Migrations.Tests` | 17 |

The provider delimiter overrides added for the scanner (`IdentifierQuoteOpen`/`Close`) are covered offline for
all three shapes — ANSI `"`, MySQL backticks, and MSSql's **asymmetric** `[`/`]`, which is the case a
same-character scanner gets wrong. A test fake that overrode `QuoteIdentifier` without them was corrected in
the same change, since an unfaithful fake is how such an override goes untested.

## Out of scope

- The identifier rules themselves for framework-created objects — **[[TASK-253]] owns those and they are
  correct**; this task is about the objects that premise does not cover.
- **A first-class notion of which schema an entity lives in** — [[TASK-272]] owns it. This task fixed a
  caller-supplied qualified *string*; it deliberately did not give the framework a schema concept. Measured
  here: none exists anywhere (`Attributes.Table` takes only `Name`, `Tables.Table` holds no schema, no
  `Settings` class has one), and the design splits table-for-identity / connector-for-rendering because
  `Tables.Table` holds no connector while quoting is provider-specific. Also measured: `SupportsSchemas` would
  be true on PostgreSQL and MSSql and false on MySQL (where SCHEMA *is* DATABASE) and SQLite.
- The hardcoded `time` bucketing column (**[[TASK-255]]**), the raw SQL fragments (**[[TASK-260]]**), and
  `GetChunkInterval`'s stale catalogue column (**[[TASK-261]]**).
- `SqlSchemaBuilder`'s uncleared external transaction (**[[TASK-259]]**).

## Human test plan

- [x] N/A — mechanical. The proof is a hypertable created over a schema-qualified table, read back from the
      catalogue by schema and name. The second half of the original sentence — a hand-created quoted
      mixed-case column reachable through the emitter — is **not** delivered and is not a pending human step:
      it is the recorded precondition, for the reason in criterion 2.
