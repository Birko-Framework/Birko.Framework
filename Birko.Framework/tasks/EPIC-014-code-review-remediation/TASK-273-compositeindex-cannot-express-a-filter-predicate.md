---
id: TASK-273
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
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

⚠ **Verify that per-provider NULL behaviour on the live servers before building anything.** It is stated
above from standard SQL semantics, not measured, and this repository's rule is that a provider claim is
worth what its live test says. All four suites gate on `BIRKO_*_HOST` and skip silently, so set
`BIRKO_REQUIRE_LIVE`.

⚠ **TASK-257 raised the stakes rather than lowering them.** Before it, an unlengthed `string` declared
`TEXT` on MSSql and `TEXT` is not a legal index key (Msg 1919) — so a unique index over such a column could
not be built there **at all**, and this gap was moot. Since TASK-257 an indexed column declares
`NVARCHAR(255)` and those indexes build, which is what turns "MSSQL treats NULLs as equal" into a live
concern for every nullable indexed column a consumer declares from now on.

⚠ **The portability objection is smaller than it looks — check it before scoping the feature wide.**
MySQL is the one provider of the four with **no partial/filtered index support**, which reads at first like
a blocker. But MySQL is also one of the three that already treat NULLs as distinct, so for *this* use it
does not need the predicate: an unfiltered unique index there already admits many NULLs. The predicate is
needed only where filtered indexes exist (MSSql, PostgreSQL, SQLite). A **general-purpose** predicate
(`WHERE IsActive = 1`) is a different, wider feature and is genuinely unimplementable on MySQL — decide
which of the two is being built and say so, rather than discovering it at the fourth provider.

## Acceptance criteria

- [ ] Per-provider NULL-in-unique-index behaviour **measured live** on all four (MSSql 2022, PostgreSQL 16,
      MySQL 8.4, SQLite), with `BIRKO_REQUIRE_LIVE` set so a missing server fails instead of skipping. The
      claim above is the hypothesis, not the result.
- [ ] A decision, recorded on this task: **(a)** a narrow "exclude NULLs" flag, or **(b)** a general
      predicate string. (a) is portable across the three providers that need it and is a no-op on MySQL;
      (b) cannot be honoured on MySQL and needs an explicit policy for it.
- [ ] The chosen shape threaded through all four sites: the attribute
      (`Attributes/Field.cs`), `IndexDefinition`, whatever populates it from the attribute
      (`DataBase_Table.LoadIndexes`), and `CreateIndexSql` — plus any provider override that reimplements
      the statement.
- [ ] ⚠ **A provider that cannot honour the predicate must not silently emit the index without it.** That
      converts a declared constraint into a *different, stricter* constraint that rejects legitimate rows —
      worse than not creating it. Whatever the MySQL policy is, it is loud: either refuse at schema-ensure
      or record the failure the way TASK-354's `IndexCreationFailures` does.
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

## Human test plan

- [ ] N/A — the verification is four live-server behavioural suites plus a mutation. Nothing here is
      exercised by a person driving a product.

## Implementation plan

⚠ **Acceptance criteria question — criterion 3 names one lane, and the framework has two.** The four sites
listed (`Attributes/Field.cs`, `Tables.IndexDefinition`, `LoadIndexes`, `CreateIndexSql`) are the
**attribute → schema-ensure** lane. There is a second, parallel index lane — `Birko.Data.Patterns`'
`IndexManagement.IndexDefinition` → `SqlIndexManager` / `IIndexBuilder` — and it **already has a word for
this**: `IndexDefinition.Sparse` ("skips documents without the indexed field"). Measured: every
implementation of `IIndexBuilder.Sparse()` is `=> this` — all six backends, `SqlSchemaBuilder.cs:333`
included — and `SqlIndexManager.ToSqlIndexDefinition` copies `Name`/`Unique`/columns and **drops `Sparse`**,
the identical lost-flag shape as TASK-245 (`Unique` dropped in that same method) and TASK-246 (`.Unique()`
dropped in `SqlIndexBuilder.Build()`). Recommendation: keep that lane **out** of this task and spawn it — but
the decision is the human's, because it changes what "threaded through all four sites" means.

⚠ **Acceptance criteria question — criterion 3 says `CompositeIndex`; `IndexedField` has the same hole.**
`IndexedField` also carries `IsUnique` (`Attributes/Field.cs:100-113`), so a single nullable column marked
`[IndexedField("ux", IsUnique = true)]` is broken on MSSql for exactly the same reason. § TASK-215's *guard
the whole verb family or none of it* says both attribute forms take the flag; both are resolved in
`LoadIndexes`, at the two marking points TASK-248 had to fix in tandem. The plan below assumes **both**.

### Step 0 — measure, before touching a line (criterion 1)

The Context's per-provider NULL claim is standard-SQL reasoning. Measure it first: two of the branches below
are chosen by the result, and TASK-257's step 0 falsified two of the four premises it was about to cite.

Per provider (MSSql 2022, PostgreSQL 16, MySQL 8.4, on-disk SQLite), on a scratch table
`(TenantGuid, ExternalId NULL)`:

1. Plain `CREATE UNIQUE INDEX ux ON T (TenantGuid, ExternalId)` → insert two rows with `ExternalId NULL`,
   same tenant. **Record accepted vs the error code**, never the message.
2. Insert two rows sharing a non-NULL `ExternalId` → confirm rejection everywhere. A one-directional result
   cannot tell a working index from an absent one.
3. Filtered/partial spelling `… (TenantGuid, ExternalId) WHERE ExternalId IS NOT NULL` → does it parse, and
   does it then admit many NULLs while still rejecting a duplicate non-NULL? Expect yes on MSSql,
   PostgreSQL, SQLite; expect a syntax error on MySQL — **record MySQL's code**, since the emitter's MySQL
   branch is justified by it.
4. ⚠ **MSSql only, and this is the risk that can sink the approach.** SQL Server requires a specific
   SET-option state for **any DML against a table carrying a filtered index** (`Msg 1934` otherwise), and
   this framework never sets SET options — the TASK-257 `ANSI_WARNINGS` lesson one layer over. Insert,
   update and delete **through the real store** against a table with the filtered index in place, on
   `Microsoft.Data.SqlClient` defaults. If that fails, the filtered index is unusable through this framework
   and the task's shape changes; find out now, not at criterion 5.
5. SQLite: confirm the bundled engine takes a partial index (needs ≥ 3.8.0 — almost certainly fine, but it
   is one query and it is the provider every consumer test actually runs on).

Write the measured table into the task's `## Context` in place of the hypothesis, with server versions.

### Step 1 — settle the shape and record it (criterion 2)

**Recommendation: (a), a narrow generated "exclude NULLs" flag. Not (b), a caller-supplied predicate.**
Three reasons, in the order that decides it here:

- **(b) is a new uncontainable DDL injection sink.** A predicate string reaches `CREATE INDEX` by
  interpolation and cannot be parameterised. `DataBase.ValidateIndexFieldIdentifier` (the shared
  `_bareIdentifier` check, TASK-245/249) accepts *one bare identifier* — it cannot validate `IsActive = 1`,
  so (b) needs a whole expression validator or it is SH-H023 with a fresh coat. (a) generates the tail from
  **resolved column metadata**, so no caller text reaches the statement at all.
- **(a) is honourable on every provider; (b) is not.** For the exclude-NULLs case MySQL needs no predicate —
  it already treats NULLs as distinct — so omitting the tail there is *behaviour-preserving*, which is what
  lets criterion 4 be discharged by a documented, tested no-op instead of a refusal. Under (b) MySQL must
  refuse, and every consumer declaring a predicate loses MySQL.
- **(a) composes with existing metadata for free** — `[NamedField]` / `ModelMap` remaps are already resolved
  in `LoadIndexes`, so the predicate names the mapped column without a second resolution.

Cost of (a), stated rather than discovered: `WHERE IsActive = 1` stays impossible. That is a genuinely wider
feature, and (a) does not block it — the flag and a future predicate can coexist on the definition.

**Naming — decide deliberately, do not reuse `Sparse` by reflex.** Mongo's `Sparse` on a *compound* index
includes a document if **any** key is present; the tail proposed here requires **every** nullable key to be
non-null. Same intent, different rule, so one word would name two behaviours. Proposed: `ExcludeNulls` on the
attributes, rendered `WHERE <col> IS NOT NULL AND …`.

### Step 2 — thread it (criterion 3)

1. `Attributes/Field.cs` — `bool ExcludeNulls { get; set; }` on **both** `CompositeIndex` and `IndexedField`.
   Settable property beside `IsUnique` on the former; on the latter an optional ctor arg **appended last**
   plus a settable property, so no existing positional call site moves. Correct the XML remarks on both:
   they currently *state as a decision* that "only a full (non-partial) unique index is emitted —
   partial/filtered unique indexes are not supported (not portable across providers)". That sentence is the
   record of the choice this task reverses; leaving it is worse than never having written it.
2. `Tables/IndexDefinition.cs` — `bool RequireNotNull` on **`IndexColumn`**, not a bool on the index. The
   connector must render `WHERE a IS NOT NULL AND b IS NOT NULL` in column order with no metadata of its own,
   and *which* key columns are nullable is knowable only where the fields are (`LoadIndexes`). Per-column
   keeps the one-producer split clean — resolution in `LoadIndexes`, rendering in the connector — and it is
   what a later `IIndexManager` lane would populate too.
3. `DataBase_Table.LoadIndexes` — at **both** resolution points (the per-property loop and the composite loop;
   TASK-248 had to fix exactly this pair, and reverting one of them failed 0 tests until TASK-257's suite
   declared one entity per attribute form). When the declaration sets `ExcludeNulls`, set `RequireNotNull` on
   each key column whose resolved field is nullable. Two sub-decisions to make explicit in code and comment:
   a **non**-nullable key column is never flagged (the tail would be dead weight and would change the DDL of
   a perfectly good index), and `ExcludeNulls` on an index whose keys are all non-nullable emits **no tail**
   rather than throwing — a vacuous declaration, not a contradiction.
4. `AbstractConnectorBase.CreateIndexSql` — append ` WHERE <bare col> IS NOT NULL AND …` for the flagged
   columns, **bare**, per § Conventions and for TASK-245's reason (PostgreSQL stores the folded name).
5. `MSSqlConnector.CreateIndexSql` — append to the `create` string **before** the `sys.indexes` guard is
   prefixed, so both the conditional and unconditional forms carry it. Columns stay bracket-quoted here (an
   already-documented deliberate divergence), so the tail's columns are quoted here and bare in the base.
   That asymmetry is *already* the rule for the column list; keep the tail consistent with its neighbours
   rather than with the other provider.
6. `MySQLConnector.CreateIndexSql` — **never** emits the tail. Gate it on a new
   `AbstractConnectorBase.SupportsPartialIndexes` (`true` by default, `false` on MySQL alone) in the family of
   `SupportsTransactionalDdl` / `FoldsUnquotedIdentifiers` / `IsMissingTableException`: one producer,
   consulted, never re-derived at a call site. Not an inline `if (this is MySQLConnector)`.

### Step 3 — the loud-failure rule (criterion 4)

Under shape (a), MySQL's omission is **behaviour-preserving for the only semantic the flag has** — which is
what discharges this criterion, but that is an argument, and TASK-258 is this epic's standing reminder that an
argument is not a measurement. So it is discharged by step 0 plus a **live MySQL test** (two NULL rows
accepted through the store, duplicate non-NULL rejected), not by the reasoning above.
`SupportsPartialIndexes` is the seam where a future general predicate must **refuse** rather than omit; say so
in its remarks, because the next author arrives with (b).

### Step 4 — tests

Offline, in `Birko.Data.SQL.Tests` (the project that declares the emitter — TASK-257's close gate was pulled
up for testing a `Birko.Data.SQL` member only from provider suites):

- the tail renders for one and for two nullable key columns, in column order, bare;
- no flag → **byte-identical** statement (criterion 7), asserted as a string, for the base and each override;
- `ExcludeNulls` with no nullable key column → no tail;
- `SupportsPartialIndexes` reads `false` on MySQL and `true` on the other three — **assert the false side**,
  or the capability is indistinguishable from an unconditional emit and can be deleted with nothing noticing.

Live, per provider — extend `DeclaredIndexLiveTests` in the MSSql / MySQL / PostgreSQL suites and
`ClassLevelCompositeIndexEndToEndTests` for SQLite; all gate on `BIRKO_*_HOST`, so run the set with
`BIRKO_REQUIRE_LIVE` set (a silent skip is the failure mode this epic keeps re-learning):

- both directions on every provider: two NULL rows accepted **and** two rows sharing a non-NULL rejected;
- MSSql: DML through the store against the filtered-index table on SqlClient defaults (step 0.4's risk),
  pinned so a future SET-option change fails here rather than in production;
- the existing `CompositeIndex` declarations in the tree keep their DDL unchanged.

Mutations (criterion 6), each isolating one claim:

- drop the tail from the base emitter → the MSSql two-NULL-rows test must go red;
- force `SupportsPartialIndexes` true on MySQL → the MySQL suite must go red on the measured syntax error,
  which is what proves the capability's `false` side is load-bearing;
- drop the `RequireNotNull` marking at **each** of `LoadIndexes`' two resolution points in turn → disjoint
  failures, one per attribute form. A mutation that fails 0 tests means the suite is missing an entity, not
  that the fix is redundant.

### Step 5 — documentation, at close

Attribute remarks (see 2.1), a § Conventions entry in `CLAUDE.md` stating the rule and why (a) beat (b), and a
Recent Updates entry. Three commits per § Task tracking: production (`Birko.Data.SQL` + the two provider
repos), tests, then this aggregator.

### Out-of-plan discoveries to spawn rather than absorb

- **The Patterns/migrations index lane drops `Sparse` the way TASK-245 dropped `Unique`** —
  `IIndexBuilder.Sparse()` is `=> this` in all six schema builders and `ToSqlIndexDefinition` never copies it,
  so a migration asking for a sparse index silently gets a full one. Same family, different lane; its own task.
- **`IIndexBuilder.WithProperty(key, value)` is `=> this` everywhere too** — a second silent no-op on the same
  interface. Fold into the above.

