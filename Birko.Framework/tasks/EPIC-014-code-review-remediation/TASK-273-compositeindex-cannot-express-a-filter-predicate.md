---
id: TASK-273
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: in-progress
priority: P1
assignee: ai
created: 2026-08-22
depends-on: []
blocks: []
related: [TASK-245, TASK-246, TASK-257]
findings: []
pr: null
github-issue: null
jira-key: null
---

# `CompositeIndex` cannot express a filter predicate, so a unique index over a NULLABLE column is unusable on MSSql

## Context — raised by consumer Symbio while ruling its v1 frozen schema (Symbio TASK-463 / TASK-481)

`CompositeIndex` carries exactly three things (`Birko.Data.SQL/Attributes/Field.cs:131-142`):

```csharp
public class CompositeIndex : System.Attribute
{
    public string   Name       { get; }
    public string[] Properties { get; }
    public bool     IsUnique   { get; set; }
}
```

There is no way to say *"unique **where** the column is not null"*. `IndexDefinition`
(`SQL/Tables/IndexDefinition.cs:5`) carries `Name` / `Unique` / `Columns` and nothing more, and
`AbstractConnectorBase.CreateIndexSql` (`SQL/Connectors/AbstractConnectorBase.cs:740`) emits
`CREATE {unique}INDEX {name} ON {table} ({columns})` with no tail.

**Why a consumer needs it.** Symbio's `CustomerAccount` declares per-tenant uniqueness on `Email` as
`[CompositeIndex("ux_customeraccount_email", TenantGuid, Email, IsUnique = true)]`, while the sibling
`ExternalId` — `public string? ExternalId` — carries **no index at all** and is enforced only by a
service-side read-then-write. That is a race, and Symbio TASK-481 had to accept it *because this attribute
cannot express the index that would close it*. Storage-level uniqueness is exactly the backstop this
framework provides everywhere else.

**Why the obvious index is wrong rather than merely missing.** `ExternalId` is nullable and most rows have
no external id. SQL Server treats NULLs as **equal** for uniqueness purposes, so a plain
`UNIQUE (TenantGuid, ExternalId)` admits **one** NULL row per tenant and rejects the second ordinary
account. PostgreSQL, SQLite and MySQL treat NULLs as **distinct** and admit any number. So adding the index
without a predicate does not merely under-enforce — on MSSql it **breaks ordinary inserts**, and only there.

### Measured, 2026-08-22 — live, all five engines (acceptance criterion 1)

Probe table `(TenantGuid, Number NULL, DeletedAt NULL)`; `A`/`B` are two inserts, second one's outcome shown.
Codes are the driver's, never the message.

| Probe | MSSql 2022<br>16.0.4265.3 | PostgreSQL<br>16.15 | TimescaleDB 2<br>/PG 16.15 | MySQL<br>8.4.11 | SQLite<br>3.53.3 |
|---|---|---|---|---|---|
| **M1a** plain `UNIQUE (T, Number)`, two NULL Numbers | **REJECTED `Msg 2601`** | ACCEPTED | ACCEPTED | ACCEPTED | ACCEPTED |
| **M1b** plain, two `'A'` | REJECTED `2601` | REJECTED `23505` | REJECTED `23505` | REJECTED `1062` | REJECTED `19/2067` |
| **M2** partial `WHERE Number IS NOT NULL` (**key** column) | works | works | works | **DDL `ERROR 1064`** | works |
| **M3** partial `WHERE DeletedAt IS NULL` (**non-key** column) | works | works | works | **DDL `ERROR 1064`** | works |
| **M4** plain index, live row + soft-deleted row sharing `'A'` | REJECTED | REJECTED | REJECTED | REJECTED | REJECTED |
| **M5** DML (INSERT/UPDATE/DELETE) with a filtered index present, driver defaults | **all OK** | — | — | — | — |
| **M6** functional key part `(CASE WHEN DeletedAt IS NULL THEN Number END)` | — | — | — | **emulates it**: live+deleted ACCEPTED, two live REJECTED `1062` | — |

"works" = the partial index admits many excluded-by-predicate rows **and** still rejects a duplicate among
the included ones — both directions, since a one-directional result cannot tell a working index from an
absent one.

Five things the measurement settled, two of them against what this task assumed:

- **The hypothesis holds, and the error code is `2601`** (duplicate key row in an object with a unique
  index), **not** the `2627` its TASK-257 neighbours cite for a constraint violation. A test asserting 2627
  would have been wrong.
- **The `Msg 1934` risk is dead.** SQL Server requires a specific SET-option state for *any* DML against a
  table carrying a filtered index, and this framework never sets SET options. Measured on
  `Microsoft.Data.SqlClient` defaults: `ANSI_NULLS=1 QUOTED_IDENTIFIER=1 ANSI_PADDING=1 ANSI_WARNINGS=1
  CONCAT_NULL_YIELDS_NULL=1 NUMERIC_ROUNDABORT=0`, `ARITHABORT=0` — and insert, update and delete all
  succeed anyway, because `ANSI_WARNINGS ON` implies the arithabort behaviour. This was the one finding that
  could have made filtered indexes unusable through this framework.
- **A predicate over a NON-key column builds on all three providers that have partial indexes** — so
  "unique among live rows" (`WHERE DeletedAt IS NULL`) is buildable, not theoretical. `ISoftDeletable` is
  `DateTime? DeletedAt` with *null means active*, and Symbio's `BaseEntity` gives it to **every** entity, so
  this is the near-term second caller, not a hypothetical one.
- **M4 fires on every provider, MySQL included, and it inverts the failure mode.** Omitting a
  `DeletedAt IS NULL` tail does not under-enforce — it **over**-enforces, refusing a document number
  legitimately reused after a soft delete. That is the outcome criterion 4 forbids, so "omit where
  unsupported" is not a blanket policy; it is safe for the `IS NOT NULL` polarity only (M1a), and there only
  because MySQL already treats NULLs as distinct.
- **MySQL can honour the unsupported polarity after all** (M6), via an 8.0.13+ functional key part. That
  option did not exist when the task was filed; it is deliberately **not** taken here — see § Out of scope.

⚠ **TASK-257 raised the stakes rather than lowering them.** Before it, an unlengthed `string` declared
`TEXT` on MSSql and `TEXT` is not a legal index key (Msg 1919) — so a unique index over such a column could
not be built there **at all**, and this gap was moot. Since TASK-257 an indexed column declares
`NVARCHAR(255)` and those indexes build, which is what turns "MSSQL treats NULLs as equal" into a live
concern for every nullable indexed column a consumer declares from now on.

## Acceptance criteria

- [x] Per-provider NULL-in-unique-index behaviour **measured live** on all four (MSSql 2022, PostgreSQL 16,
      MySQL 8.4, SQLite), with `BIRKO_REQUIRE_LIVE` set so a missing server fails instead of skipping. The
      claim above is the hypothesis, not the result. → § *Measured, 2026-08-22*; TimescaleDB 2 measured too
      (it inherits the PostgreSQL connector). The step-0 harness was a scratchpad `.csx`, so the durable
      form of this criterion is the live suites in the criteria below.
- [x] A decision, recorded on this task: **(a)** a narrow "exclude NULLs" flag, or **(b)** a general
      predicate string. (a) is portable across the three providers that need it and is a no-op on MySQL;
      (b) cannot be honoured on MySQL and needs an explicit policy for it. → **Refined (a): two
      resolved-column lists**, `WhereNotNull` / `WhereNull`. See § Resolved decisions in the plan; (b) is
      deferred with its injection problem named.
- [ ] The chosen shape threaded through all four sites: the attribute
      (`Attributes/Field.cs`), `IndexDefinition`, whatever populates it from the attribute
      (`DataBase_Table.LoadIndexes`), and `CreateIndexSql` — plus any provider override that reimplements
      the statement (`MSSqlConnector`, `MySQLConnector`), and **both** attribute forms
      (`CompositeIndex`, `IndexedField`).
- [ ] ⚠ **A provider that cannot honour the predicate must not silently emit the index without it.** That
      converts a declared constraint into a *different, stricter* constraint that rejects legitimate rows —
      worse than not creating it. Whatever the MySQL policy is, it is loud: either refuse at schema-ensure
      or record the failure the way TASK-354's `IndexCreationFailures` does. → **Per polarity**, on M1a/M4
      evidence: `WhereNotNull` is omitted (behaviour-preserving), `WhereNull` is refused.
- [ ] Live behavioural tests per provider, asserting **both** directions: two NULL rows are accepted, and
      two rows sharing a non-NULL value are rejected. A one-directional test passes against an index that
      was never created.
- [ ] Mutation-proven: drop the predicate from the emitted SQL and the MSSql test must go red.
- [ ] Existing declarations unaffected — the six entities carrying `CompositeIndex` today declare no
      predicate and their emitted DDL must be byte-identical.

## Out of scope

- Symbio's own decision about whether `CustomerAccount.ExternalId` should be unique at storage — that is
  Symbio TASK-481. This task supplies the capability; the consumer decides whether to use it.
- Foreign-key support. Also absent from this framework (`FOREIGN KEY` and `ON DELETE` occur nowhere in
  `Birko.Data.SQL` outside comments, and 0 of 146 tables in a real consumer database carry one) and also
  raised by the same Symbio freeze pass, but it is a much larger feature and is tracked separately by the
  consumer as Symbio TASK-540 pending its decision.
- Retrofitting predicates onto any existing declaration.
- **A general predicate string** (`WHERE IsActive = 1`) — deferred, not forgotten. It is the natural next
  ask and it is blocked on a real problem: caller text reaches `CREATE INDEX` by interpolation, cannot be
  parameterised, and `DataBase.ValidateIndexFieldIdentifier` validates *one bare identifier*, so an
  expression needs a validator this framework does not have (the SH-H023 sink family). The two lists chosen
  here do not block it — a `Where` string can arrive beside the `Where*` lists later.
- **Emulating the unsupported polarity on MySQL via a functional key part** — measured working (M6) and
  deliberately declined: it is a statement shape nothing else in the framework emits, the index stops
  serving plain `Number = 'A'` lookups (constraint kept, optimisation lost), it matches PostgreSQL/SQLite
  NULL semantics but not MSSql's when a key column is NULL, and `DropIndexSql` / `ListIndexesSql` would
  then meet an expression column. Recorded here with the measurement so the next author meets a decision
  rather than a gap.
- **The second index lane** — `Birko.Data.Patterns.IndexManagement.IndexDefinition` → `SqlIndexManager` /
  `IIndexBuilder` — carries no predicate in either direction, and its existing `Sparse` flag is already a
  silent no-op: `IIndexBuilder.Sparse()` is `=> this` in **all six** schema builders and
  `ToSqlIndexDefinition` never copies `Sparse`, the same lost-flag shape as TASK-245 (`Unique`, same
  method) and TASK-246 (`.Unique()`, `SqlIndexBuilder.Build()`). `WithProperty` is a second `=> this` on
  the same interface. → owned by **[[TASK-274]]**.

## Human test plan

- [ ] N/A — the verification is four live-server behavioural suites plus a mutation. Nothing here is
      exercised by a person driving a product.

## Implementation plan

### Resolved decisions (grilled 2026-08-22, after the live measurement above)

- **Scope** → both `[CompositeIndex]` and `[IndexedField]` (§ TASK-215: guard the whole verb family). Note
  `[IndexedField(…, IsUnique: true)]` has **zero** production declarations — tests only — so this costs one
  property, one marking site and one probe entity, and buys the absence of a store where one attribute form
  can exclude NULLs beside another that silently cannot.
- **Shape** → **two resolved-column lists**, not a bool and not a predicate string: `WhereNotNull` covers
  Symbio's nullable `ExternalId`, `WhereNull` covers unique-among-live-rows on every `ISoftDeletable`
  entity, and M3 proves a non-key predicate column builds. Column names resolve through the same `fields`
  map as `CompositeIndex.Properties`, so **no caller text reaches the DDL**.
- **Naming** → `WhereNotNull` / `WhereNull` — reads as the SQL it emits, one-to-one with the statement, and
  `Where` is the term PostgreSQL and SQLite use for a partial index. (`RequireNulls` was rejected as
  ambiguous: it can be read as a constraint on the data rather than a filter on the index.)
- **MySQL policy** → per polarity, on evidence: `WhereNotNull` is **omitted** (M1a — NULLs are already
  distinct there, so the emitted index means the same thing); `WhereNull` is **refused** (M4 — omitting it
  would over-enforce). Gated on one capability, not an inline type test.
- **Bad predicate column** → **throws at table load** in all three cases: unmapped name, `IsNotNull` column,
  or the same column in both lists. Fail-fast is free here (new API, zero declarations), unlike TASK-248
  and TASK-256 where the measured blast radius vetoed it.
- **Refusal mechanism** → no new machinery. `CreateIndexes` consults the capability and throws **before**
  `DoDdlCommand`, so the exception is not re-wrapped by `InitException`; schema-ensure's per-index
  `catch → RecordIndexCreationFailure` records it (TASK-204 degrade-and-report, untouched) while an
  explicit `CreateIndexes` call propagates. The emitter also throws if handed an unrenderable definition
  directly — § TASK-137's "a strategy asked to render the unrenderable throws".
- **Predicate lives on the index, not the column** → `IndexDefinition.Predicates`, a list of
  (column, `RequireNull`) resolved in `LoadIndexes`. **Not** a flag on `IndexColumn`: M3 proves a predicate
  column need not be a key column at all, so per-column would be structurally unable to express the
  soft-delete case.
- **Non-unique indexes accept the lists too** — a partial non-unique index is a legitimate optimisation and
  costs nothing to allow.

### Step 1 — attributes (`Birko.Data.SQL/Attributes/Field.cs`)

`string[] WhereNotNull` and `string[] WhereNull` on both `CompositeIndex` and `IndexedField`, defaulting to
empty (never null — `Properties` already normalises that way). Settable properties, so no existing positional
call site moves.

Correct the XML remarks on both: they currently state *as a decision* that "only a full (non-partial) unique
index is emitted — partial/filtered unique indexes are not supported (not portable across providers)". That
sentence is the record of the choice this task reverses; leaving it is worse than never having written it. The
replacement states the measured per-provider truth and the MySQL policy.

### Step 2 — `SQL/Tables/IndexDefinition.cs`

```csharp
public class IndexPredicate { public string ColumnName { get; set; } = null!; public bool RequireNull { get; set; } }
// on IndexDefinition:
public List<IndexPredicate> Predicates { get; } = new();
```

### Step 3 — `DataBase_Table.LoadIndexes`

Resolve both lists at **both** attribute resolution points — the per-property `IndexedField` loop and the
class-level `CompositeIndex` loop. TASK-248 had to fix exactly this pair, and reverting one of its two
markings failed 0 tests until TASK-257's suite declared one entity per attribute form; the mutation below
exists for that reason.

Per name: resolve to a field (unmapped → `TableAttributeException` naming type, index and property, matching
the existing `CompositeIndex` typo throw), reject `field.IsNotNull`, reject a name present in both lists,
then append an `IndexPredicate`. Also carry the cross-assembly shared-project fallback that both loops
already have for `Name`/`Properties`/`IsUnique` — an attribute compiled into another assembly is read
reflectively, so a new property that is only read through the direct cast works in the framework's own tests
and silently does nothing for the consumers this feature exists for.

### Step 4 — emitters

- `AbstractConnectorBase`: `public virtual bool SupportsPartialIndexes => true;` — one producer, in the
  family of `SupportsTransactionalDdl` / `FoldsUnquotedIdentifiers` / `IsMissingTableException`.
  `MySQLConnector` overrides it `false`.
- `AbstractConnectorBase.CreateIndexSql`: append ` WHERE <col> IS [NOT] NULL` joined by ` AND ` in
  `Predicates` order, columns **bare** (TASK-245: `CreateTable` emits column definitions bare, so
  PostgreSQL stores the folded name and a quoted one cannot resolve it).
- `MSSqlConnector.CreateIndexSql`: same tail appended to `create` **before** the `sys.indexes` guard is
  prefixed, so the conditional and unconditional forms both carry it; columns bracket-quoted, consistent
  with that override's own column list rather than with the base.
- `MySQLConnector.CreateIndexSql`: emits no tail. A `RequireNull` predicate never reaches it (step 5
  refuses first); if it does, it throws.

### Step 5 — the funnel (`AbstractConnector.CreateIndexes` + the async twin)

Before `DoDdlCommand`: if `!SupportsPartialIndexes` and any predicate has `RequireNull`, throw with a
message naming the index, the provider limitation (`ERROR 1064`, measured) and the M6 emulation as the
recorded alternative — § SH-H037's "a guard whose message only says no gets reached around". A
`WhereNotNull`-only definition passes through and the tail is dropped by the emitter.

⚠ Check `AsyncDataBaseStore.InitCoreAsync` before believing any revert here: it calls the **sync**
`Connector.CreateTable`, so `CreateIndexesAsync` has no store-level caller and reverting only the async site
failed 0 of 14 in TASK-245.

### Step 6 — tests

Offline, `Birko.Data.SQL.Tests` (the project that declares the emitter — TASK-257's close gate was pulled up
for testing a `Birko.Data.SQL` member only from provider suites):

- tail renders for one and two predicates, both polarities, in order, bare;
- no predicate → **byte-identical** statement (criterion 7), asserted as a string, for base + both overrides;
- `SupportsPartialIndexes` is `false` on MySQL and `true` on the other three — **assert the false side**, or
  the capability is indistinguishable from an unconditional emit;
- the three declaration errors throw, with the property name in the message;
- the cross-assembly reflective path reads both new lists.

Live, extending `DeclaredIndexLiveTests` (MSSql / MySQL / PostgreSQL) and
`ClassLevelCompositeIndexEndToEndTests` (SQLite), all with `BIRKO_REQUIRE_LIVE` set:

- **both directions** per provider: rows excluded by the predicate are accepted, duplicates among the
  included ones are rejected — asserting the measured codes (`2601` / `23505` / `1062` / `19-2067`);
- MSSql: DML through the store against the filtered-index table on driver defaults (pins M5, so a future
  SET-option change fails here rather than in production);
- MySQL: a `WhereNotNull` declaration builds and behaves; a `WhereNull` one is refused and **recorded**, and
  the entity's reads still work (TASK-204's degrade contract);
- existing `CompositeIndex` declarations keep their DDL.

Mutations, each isolating one claim:

- drop the tail from the base emitter → the MSSql excluded-rows test goes red;
- force `SupportsPartialIndexes` true on MySQL → MySQL goes red on `1064`, proving the false side is
  load-bearing;
- drop the predicate resolution at **each** of `LoadIndexes`' two points in turn → disjoint failures, one
  per attribute form. A mutation failing 0 tests means a missing probe entity, not a redundant fix.

### Step 7 — documentation, at close

Attribute remarks (step 1), a § Conventions entry stating the rule, the polarity asymmetry and why the
general predicate was deferred, and a Recent Updates entry. Three commits per § Task tracking: production
(`Birko.Data.SQL`, `Birko.Data.SQL.MSSql`, `Birko.Data.SQL.MySQL`), then the test repos, then this aggregator.
