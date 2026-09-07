---
id: TASK-269
parent: EPIC-014
feature: FEATURE-014
status: review
priority: P2
assignee: ai
created: 2026-08-21
depends-on: []
blocks: []
related: [TASK-204, TASK-257]
findings: []
pr: "Birko.Data.SQL 2590cbf + Health.Data.SQL fix c59b5f9 + SqLite ade986f + MySQL 704c5b7 + MSSql 87654ff + PostgreSQL e4725fc + Birko.Health.Data.SQL 17b4098"
github-issue: null
jira-key: null
---

# Nothing reports a column whose stored type no longer matches what the model declares

## Context — spawned by TASK-257, whose decision depends on an operator noticing

TASK-257 settled that the framework does **not** repair an existing database: `CreateTable` is guarded by
`IF NOT EXISTS` and schema-ensure never reconciles an existing table's columns, so a database created
before that fix keeps its `TEXT` columns and **keeps failing every string predicate afterwards**. The
remedy is a hand-run `ALTER TABLE … ALTER COLUMN … NVARCHAR(MAX)`.

That decision is only reasonable if somebody finds out. Today nobody does:

- The framework has **no schema-drift surface at all** — no equivalent of `IIndexManager` for column types.
- Consumer Symbio's `Symbio.DataAccess/Sql/SchemaDriftCheck.cs` compares column **presence** only, so a
  column of the wrong *type* reads as perfectly healthy.

So the failure mode is: an upgraded deployment starts throwing Msg 402 on every string predicate, and the
only signal is the exception at the call site. The blast radius for TASK-257 specifically is zero (no
deployment selects MSSql today), which is why it was allowed to close — but the gap is general and outlives
that particular fix. Any future column-type change has the same problem.

## Why it matters beyond TASK-257

This is the third member of a family the epic keeps rediscovering: a condition the framework *knows* about
and reports to nobody.

- TASK-204 made schema-ensure record an unbuildable index on `IndexCreationFailures` and raise
  `OnIndexCreationFailed` — **nothing in the tree subscribes**, so every index failure since has been
  silent. TASK-245, TASK-248 and TASK-257 each found real, long-standing index failures hiding behind it.
- The same is now true one level down, for the column type itself.

A report nobody reads is indistinguishable from no report, and this epic has paid for that three times.

## What to decide

1. **Where does the check live?** A framework-side probe (compare `DataBase.LoadTable`'s fields against the
   provider's catalogue — `sys.columns`, `information_schema.columns`, `pragma_table_info`) is reusable by
   every consumer; a consumer-side one only fixes Symbio. Framework-side is the obvious call, but it needs a
   home — an `ISchemaDriftCheck` beside `IIndexManager` is the natural shape.
2. **When does it run?** On demand (a diagnostic a host can call, or a health check — note
   `CLAUDE-maintenance.md` § *Health Check Requirements* already expects external-service projects to ship
   one), or at schema-ensure? Not at schema-ensure by default: it costs a catalogue round-trip on first use
   of every store.
3. **Does anything subscribe?** This is the part that actually matters, and the part TASK-204 got wrong.
   A new reporting channel with no consumer repeats the defect exactly. Decide the subscriber before the
   producer — and consider whether `IndexCreationFailures` should be surfaced through the same door, so
   there is one place a host asks "is my schema what my models think it is?".

## Acceptance criteria

- [x] A decision recorded on all three questions above, with reasons.
- [x] A drift check that detects, against a real server, a column whose stored type differs from the
      declared one — proven with the actual TASK-257 case: create a table with a `TEXT` column, point a
      model declaring an unlengthed `string` at it, and see the drift reported.
- [x] **Something consumes the report** — a health check, a startup log line, or a diagnostic endpoint.
      A channel with no subscriber does not satisfy this criterion; that is the TASK-204 failure being
      repeated.
- [x] Decide whether `IndexCreationFailures` joins the same surface, and record the answer either way.
- [x] Proven able to fail.

## Out of scope

- Auto-repairing the drift. TASK-257 rejected auto-`ALTER` on schema-ensure (store init rewriting existing
  production columns is a quiet destructive write); this task is about *detection*, and it should not
  quietly acquire the remedy.
- The column typing itself — TASK-257 (strings), TASK-266 (binary/width), TASK-268 (`TimeOnly`).

## Human test plan

- [ ] A human confirms the drift report is actually visible where an operator would look (health endpoint,
      log, or diagnostic output) — the whole point of the task is that a person finds out, so a green
      automated assertion that the API returns a list is not sufficient on its own.

---

## Step 0 — measured 2026-09-07, before a line changed

Every number below was taken before any design was written, and three of them changed the design.

### (a) Blast radius: all five column-typing fixes have a ZERO deployed population

The task's own framing leans on TASK-257, and my recommendation to work this leaned on TASK-264.
Both were re-measured, and the second claim I made was **wrong**.

| fix | provider affected | deployed population |
|---|---|---|
| TASK-257 — unlengthed `string` → `TEXT` | MSSql | **0** |
| TASK-265 — unique/primary unlengthed string | MySQL | **0** |
| TASK-266 — `byte[]` index key width | MSSql, MySQL | **0** |
| TASK-275 — nullable unique column | MSSql | **0** |
| TASK-264 — migration column metadata (incl. `DECIMAL`→`REAL` on SQLite) | all | **0** |

- **No consumer selects a server provider.** `Symbio.Api/appsettings.json` reads `"Default": "SQLite"`,
  and so do `.Development` and `.Testing`. Every non-test `DataProvider.MsSql` / `.MySql` /
  `.PostgreSql` reference across all 16 consumer repos is a **switch case in a factory** — provider
  *support*, never provider *selection*.
- **TASK-264's population is 0, not "every deployed consumer" as I claimed when proposing this task.**
  TASK-247's "0 production uses of `ISchemaBuilder`" **still holds**. The one hit outside a test —
  `WorkoutTracker/Reps.Domain/Storage/InitialSchemaMigration.cs` — is a **doc comment explaining why it
  does not use it** (*"Birko's `ISchemaBuilder` exposes no terminal build call on its public surface"*).
  The only real usage is `Symbio.Tests.Unit/MigrationRuntimeTests`. Second instance today of
  § TASK-283's *grep for the subscription, not the identifier*; third counting (c) below.

**This does not kill the task, but it re-aims it, and the task file anticipated exactly that**: *"the
gap is general and outlives that particular fix."* The justification is **not** the five fixes. It is
**model evolution against an existing database** — a developer adding `[MaxLengthField]`, changing a
decimal's precision, or changing a property's type, on a consumer whose SQLite file already exists.
That population is every consumer, it is live today, and it is precisely what Symbio's own
`SchemaDriftCheck` was written for — minus the type half.

### (b) ⚠ The provider-independent mechanism CANNOT see the width, and that inverts the design

Symbio's `SchemaDriftCheck` deliberately avoids the catalogue, and says why:

> ⚠ **Deliberately not `information_schema.columns` and not `PRAGMA table_info`.** The first does not
> exist on SQLite and the second exists only there, so either would have meant a dialect branch in a
> diagnostic — and a diagnostic that only runs on the dialect the developer happens to use is precisely
> how this defect survived in the first place.

It reads `SELECT * FROM T WHERE 1 = 0` and takes `reader.GetName(i)`. The obvious move was to extend
that with `GetDataTypeName()` / `GetFieldType()` and get the type half for free, on every provider,
with no dialect branch. **Measured on Microsoft.Data.Sqlite, it does not work:**

| approach | keyword | width / precision / scale | dialect branch |
|---|---|---|---|
| `GetDataTypeName()` | ✓ `VARCHAR` | ✗ **stripped** | none |
| `GetColumnSchema()` (`DbColumn`) | ✓ `VARCHAR` | ✗ `ColumnSize = -1`, precision + scale **null** | none |
| `PRAGMA table_info` | ✓ | ✓ **`DECIMAL(18,2)` verbatim** | SQLite-only |

`VARCHAR(255)` reads back as `VARCHAR`, `DECIMAL(18,2)` as `DECIMAL`, `VARBINARY(32)` as `VARBINARY`.

**So a reader-based check would have reported the worst defect in the family as healthy.** TASK-264's
money case is `DECIMAL(18,0)` against `DECIMAL(18,2)` — *the same keyword*. Both provider-independent
APIs answer identically for both, so silent money truncation reads as a clean bill of health. TASK-266's
`VARBINARY(MAX)` vs `VARBINARY(255)` is the same shape.

**Conclusion: the catalogue branch is not a style choice, it is the only mechanism that can answer.**
Symbio's reasoning was right *for Symbio* and does not transfer: it needed only names, which the reader
gives free, and it has no connector abstraction to hang a dialect branch on. **The framework does** —
this is exactly what `SupportsTransactionalDdl`, `FoldsUnquotedIdentifiers`, `IsMissingTableException`,
`SupportsPartialIndexes` and `RequiresOrderByForPaging` already are. Stated once per provider, both
sides asserted per provider, is this repo's established answer to "don't let a dialect branch rot", and
it is stronger than avoiding the branch.

### (c) ⚠ `GetFieldType()` is value-dependent and must NOT be the oracle

The same probe, run three ways on one table:

| column | declared | zero-row | with a row | with a *contradicting* row |
|---|---|---|---|---|
| `Money_Decimal` | `DECIMAL(18,2)` | `String` | `Double` | — |
| `Blob_Bounded` | `VARBINARY(32)` | `String` | `Byte[]` | — |
| `NoDeclaredType` | *(none)* | `Byte[]` | `String` | — |
| `Money_Real` | `REAL` | `Double` | `Double` | **`String`** |

Storing the text `'not-a-number'` in a `REAL` column makes `GetFieldType` report `String`: it reports
the **stored value's affinity**, not the declaration. A drift check built on it would report or not
report drift depending on which rows happen to be in the table. `GetDataTypeName()` is stable across all
three — always the declared type. Two APIs that look interchangeable, and only one is usable.

### (d) The subscriber already has a home, and the existing channel still has none

- `Birko.Health.Data` ships `SqlHealthCheck.cs` beside twelve siblings, and
  `CLAUDE-maintenance.md` § *Health Check Requirements* already expects one per external-service
  project. So criterion 3 can be satisfied **inside the framework**, with no consumer edit.
- **`OnIndexCreationFailed +=` across all 16 consumer repos: 0.** Re-measured today; TASK-283's
  2026-09-03 count holds. The single textual hit is a doc comment in a Symbio test. TASK-204's channel
  has been unread since it shipped, which is the defect this task must not repeat.

### (e) `IIndexManager` is the wrong neighbour

The task proposes *"an `ISchemaDriftCheck` beside `IIndexManager`"*. Measured: `IIndexManager` lives in
`Birko.Data.Patterns.IndexManagement` and its own summary reads *"Provider-agnostic interface for
managing **NoSQL** indexes"* (MongoDB / RavenDB / ElasticSearch), with `SqlIndexManager` as a later
implementer. A schemaless store has no declared column type, so drift has no NoSQL meaning at all. This
belongs in `Birko.Data.SQL`, not in `Birko.Data.Patterns`.

## Decisions on the three questions (criterion 1)

### Q1 — where does the check live? **`Birko.Data.SQL`, on the connector, not `Birko.Data.Patterns`.**

Two measured reasons. Step 0(e): `IIndexManager` is a *NoSQL* abstraction that SQL later joined, and a
schemaless store has no declared column type, so drift has no meaning there. Step 0(b): the width half
**requires** a dialect branch, and the connector is where this framework already states every provider
capability — `SupportsTransactionalDdl`, `FoldsUnquotedIdentifiers`, `IsMissingTableException`,
`SupportsPartialIndexes`, `RequiresOrderByForPaging`. Putting it anywhere else means re-deriving the
dialect somewhere that has no provider.

**The declared side has ONE producer, and it is the method that emits the DDL.**
`AbstractConnectorBase.ConvertType(DbType, AbstractField)` is what `CREATE TABLE` uses, so the check
asks it the same question. That is the § Conventions one-producer rule applied to a *comparison*: the
drift check and the DDL cannot disagree about what a column should be, and every past and future
column-typing rule (TASK-257's `NVARCHAR(MAX)`, TASK-264's `DECIMAL(18,2)`, TASK-266's
`VARBINARY(255)`, TASK-265, TASK-275) is covered **without being told**. A check that re-derived the
expected type would be a second implementation of the rule, which is the shape this epic keeps paying
for.

### Q2 — when does it run? **On demand only. Never at schema-ensure.**

The task already gives the reason and Step 0 does not change it: a catalogue round-trip on first use of
every store, on every process start. It is also the wrong *moment* — schema-ensure runs before a store
is usable, and TASK-204/TASK-254 establish that schema-ensure must degrade rather than block. A
diagnostic that can make a store fail to start is the defect those tasks removed.

### Q3 — does anything subscribe? **Yes, and it ships in this change.**

`SchemaDriftHealthCheck` in `Birko.Health.Data`, beside the twelve existing `IHealthCheck`
implementations including `SqlHealthCheck`, which `CLAUDE-maintenance.md` § *Health Check Requirements*
already expects. The consumer lives **inside the framework**, so criterion 3 is satisfied without a
consumer edit — which matters, because Step 0(d) re-confirmed that the existing channel has **0**
subscribers across all 16 consumer repos and has been unread since TASK-204 shipped.

### Q4 — does `IndexCreationFailures` join the same surface? **Yes.**

Deliberately, and it is the part of this task with the largest measured return. The same health check
reports both, so a host has one door for *"is my schema what my models think it is?"*. That is what
finally gives TASK-204's channel a reader — the defect this task's own Context names as something *"this
epic has paid for three times"* (TASK-245, TASK-248 and TASK-257 each found real index failures hiding
behind it). Building a second, separate drift channel while leaving the first unread would repeat the
defect inside the task that exists to close it.

---

## Worked 2026-09-07 — status `review`, pending the human read of one report

### Verified

`BIRKO_REQUIRE_LIVE` set throughout, against live **PostgreSQL 16**, **MySQL 8.4**, **SQL Server 2022**
and on-disk SQLite: **1,562 tests, 0 failed, 0 skipped** across eight suites — `Birko.Data.SQL` 672,
SqLite 341, PostgreSQL 107, MySQL 119, MSSql 132, Migrations.SQL 87, `Birko.Health.Data.SQL` 13,
`Birko.Health` 91. **23 new.** 0 nullable warnings in any touched file (the 58 in `Birko.Data.SQL.Tests`
are pre-existing, in its own condition-strategy test files, and were attributed rather than assumed).

### Mutations — five, disjoint, each isolating one claim

| mutation | reds | why that is the right set |
|---|---|---|
| Compare only the type **keyword** (strip parameters) — i.e. the reader-based design | **1** — the scale test | It is the *only* test that can see the difference, which is exactly the argument for the catalogue |
| Transform loops internally (per-row reader contract violated) | **8 of 13** | Loses the first column of every table; the 5 survivors are the ones that assert nothing about column identity |
| `IsClean` ignores `Supported`/`TableExists` | **2** — absent table, unmapped type | The two "could not answer" cases and nothing else |
| MSSql: drop the byte→char halving | **1** — the clean control | Proves that control is a prover, not a pin |
| PostgreSQL: drop `attnum > 0 AND NOT attisdropped` | **3 of 4** | System columns and tombstones flood the report |

### ⚠ Two defects only the live run could find

- **`RegclassLiteral` returns the literal's CONTENTS, not a quoted literal** — the caller supplies the
  quotes, as `create_hypertable('{0}', …)` already does. Emitted bare, the catalogue query raised
  `42703: column "PgDriftClean" does not exist` on **every** table. Offline it compiled and looked right.
- **`RunReaderCommandOn` invokes its transform ONCE PER ROW**, with the reader already positioned. A
  transform that loops internally skips the current row and consumes the rest, so exactly the **first
  column of every table** went missing and every entity reported a spurious `Missing` drift. Found by the
  first test run, not by reading the code — and now written on the method that does it.

### ⚠ Corrections to my own framing, recorded because both were load-bearing

- **When recommending this task I said TASK-264 meant "money held as binary floating point on every
  already-deployed consumer". That was wrong**, and Step 0(a) says so: TASK-247's "0 production uses of
  `ISchemaBuilder`" still holds — the one non-test hit is a doc comment in `Reps.Domain` explaining why it
  is *not* used. All five column-typing fixes have a **zero** deployed population.
- **So the justification is not the five fixes**, and the task file's own framing leaned on them. It is
  **model evolution against an existing database** — a developer adding `[MaxLengthField]`, changing a
  decimal's precision, changing a property's type, against a SQLite file that already exists. That
  population is every consumer and it is live today.

### Deliberately not done

- **No auto-repair.** Out of scope by the task, and TASK-257 already rejected auto-`ALTER` on
  schema-ensure as a quiet destructive write. `DetectDrift` detects; the operator writes the `ALTER`.
- **Not wired into schema-ensure.** A catalogue round-trip on first use of every store, and a diagnostic
  that can stop a store starting is the defect TASK-204 and TASK-254 removed.
- **No consumer-side wiring.** Symbio is off-limits this session, and it already has its own
  `SchemaDriftCheck` for column *presence*; adopting this for the type half is a consumer decision.
- **TimescaleDB not separately measured.** It inherits `PostgreSQLConnector`'s override unchanged, so it
  is covered by construction rather than by evidence — said plainly rather than counted as verified.
- **`Birko.EventBus.Outbox.SQL` is undocumented** — a pre-existing doc-index gap the close gate's
  full-repo sweep surfaced, unrelated to this change. Spawned as [[TASK-301]] rather than fixed here.

## Human test plan

- [x] A human confirms the drift report is visible where an operator would look. Register
      `SchemaDriftHealthCheck` in a host, point it at a database whose column was changed by hand, and
      confirm the health endpoint reports **Degraded** with the column named — a green automated
      assertion that the API returns a list is explicitly not sufficient for this task.

---

## Human review, 2026-09-07 — run, and it found a defect

The plan asked a human to read a real health report rather than trust an automated assertion. A harness
stood up three SQLite databases and rendered what an operator sees. **That is what caught the defect the
15 automated tests did not.**

**Case 2, a database whose columns were changed by hand — correct, and the reason the task exists:**

```
Status      : Degraded
Description : Schema disagrees with the models: 4 column(s) drifted, 0 index(es) not built.
   drift:
      - Invoice.Total: declared NUMERIC(18,2), stored REAL
      - Invoice.Reference: declared TEXT, not present
      - Invoice.Note: present as TEXT, not declared
      - Invoice.Legacy_Id: present as INTEGER, not declared
```

`Invoice.Total` is TASK-264's shape exactly: money declared `DECIMAL(18,2)` and stored as binary floating
point, and the case both provider-independent reader APIs report as healthy.

**Case 3, a table that does not exist yet — the defect:**

```
Status      : Healthy
Description : Schema matches the models (1 type(s) checked).      <-- never checked it
   tablesNotYetCreated: 1
```

It asserted a match for a type whose table it had never read — the exact silence this task exists to
remove, arriving in the one line an operator reads. `absent` was computed, placed in `Data`, and never
branched on. **Every automated test about an unchecked type asserted `SchemaDriftReport.IsClean`**, which
was correct and still is; none read the rendered status. A report model can be right while the output
lies.

Fixed in `Birko.Health.Data.SQL` c59b5f9. `Healthy` is kept and the two "could not answer" cases stay
split, because they are different conditions — an unsupported provider is permanent (Degraded), an absent
table is expected and self-healing, and Degraded there would make every fresh deployment Degraded until
each entity happened to be touched. The honesty requirement moved to the wording:

```
Description : Schema matches the models for 0 of 1 type(s); 1 table(s) not created yet, so they were
              not checked.
```

Two tests added as a pair (hedged wording when something was skipped, plain wording when nothing was —
otherwise the fix could be satisfied by always hedging). Mutation F, restoring the old line, reds the
first and nothing else. Suite 13 -> 15, all green. § Conventions' rule amended, because as first written
it said an unchecked type "must not read as healthy" and the shipped code now deliberately does.

**The harness is now in the suite** (`Birko.Health.Data.SQL.Tests/SchemaDriftOperatorViewTests`, d167166),
so the review is repeatable rather than a one-off transcript:

```
dotnet test --nologo --filter SchemaDriftOperatorViewTests --logger "console;verbosity=detailed"
```

It asserts as well as printing — a class that only rendered would be a test that cannot fail — and its
assertions are deliberately about the **rendered** output rather than about `SchemaDriftReport`, since
that exact split is what let the defect through. All three cases stay in one test because their value is
the comparison: read in sequence, the third case's wording is obviously load-bearing; apart, they read as
three unrelated passes.
