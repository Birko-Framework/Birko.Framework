---
id: TASK-249
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P1
assignee: unassigned
created: 2026-08-18
depends-on: []
blocks: []
related: [TASK-245, TASK-246, TASK-247, TASK-248]
findings: []
pr: "Birko.Data.SQL aaf5940 | Birko.Data.SQL.MySQL 633372e | Birko.Data.Migrations.SQL 4d62981"
github-issue: null
jira-key: null
affects: [Birko.Data.SQL, Birko.Data.SQL.MySQL, Birko.Data.Migrations.SQL]
---

# Four close-gate findings on TASK-245 — including a second injection sink its own rule pointed at

## Context

[[TASK-245]]'s close-gate `code-review` returned **after** its nine commits had landed, and its findings were
real. Three are defects in TASK-245's own shipped work — one of them the security hole that task's *own*
recorded rule told it to look for, applied to only one of two callers.

Filed as its own task rather than reopening TASK-245: that task's acceptance criteria were met and its
commits are in, and the acceptance list stays useful only if it is not retrofitted. The fixes were made
immediately rather than scheduled, because finding 1 is a live injection introduced by the previous commit.

## The findings

### 1 — HIGH. The bare-column change opened a second injection sink (`SqlSchemaBuilder.cs:291`)

TASK-245 made index **columns** be emitted bare (required: a quoted column cannot resolve the case-folded one
that bare-column `CREATE TABLE` creates on PostgreSQL) and guarded the caller-derived sink it found —
`SqlIndexManager.ToSqlIndexDefinition`. It missed the other one.

`SqlIndexBuilder.WithField(string name, …)` takes free text from a migration and stores it verbatim;
`Build()`'s **connector path** puts it straight into `Tables.IndexColumn.ColumnName` and calls
`_connector.CreateIndexes(...)`, which reaches the now-bare emitter. That route never touches
`ToSqlIndexDefinition`, so the guard did not cover it. `QuoteIdentifier` had been incidentally containing the
payload before.

```csharp
context.Schema.CreateIndex("Docs", "ix_rank")
              .WithField("Rank); CREATE TABLE Pwned (x INTEGER); --")
              .Build();
// emitted, and executed:
// CREATE INDEX IF NOT EXISTS "ix_rank" ON "Docs" (Rank); CREATE TABLE Pwned (x INTEGER); --)
```

The SH-H023 shape, with the exact payload `IndexIdentifierInjectionTests` already proved refused on the
*sibling* sink. Neither [[TASK-246]] (the lost `Unique` flag on this same method) nor [[TASK-247]] (its
raw-SQL *fallback*) covers it.

**Fixed** in `WithField` rather than `Build()`, so it fails at the declaration site and covers both of
`Build()`'s routes, through the same `DataBase.ValidateIndexFieldIdentifier` the index manager uses — so the
two sinks cannot drift about what an acceptable column name is.

### 2 — MEDIUM. `IIndexManager` was left non-uniform on MySQL (`SqlIndexManager.cs:65`)

`CreateAsync` executes through the manager's own connection and deliberately bypasses
`AbstractConnector.CreateIndexes`, so it did not inherit that funnel's 1061 tolerance. TASK-245 made this
*worse*, not better: before, MySQL failed for every index (1064); after, it succeeded for a new index and
threw `IndexManagementException` for an already-present one, while SQLite/PostgreSQL (native `IF NOT EXISTS`)
and MSSql (a synthesised guard) reported success. Which also contradicted TASK-245's own claim that the
change "makes MySQL agree with" the other three — the manager path did not.

**Fixed on both verbs, not one.** `CreateAsync` tolerates already-exists; `DropAsync` tolerates already-absent
via a new `IsIndexMissingException` (base false, MySQL 1091), because MySQL accepts no `IF EXISTS` on
`DROP INDEX` while every other provider's emitter carries it. Fixing create and leaving drop would have
shipped a manager whose create tolerates "already there" beside a drop that throws for "already gone", on one
provider only — the "guard the whole verb family or none of it" rule.

The connector's own `DropIndexes` deliberately does **not** consult the new predicate: a caller naming a
specific index should fail loudly, and the migrations drop step relies on that. Asserted by its own test, so
the two doors cannot be "unified" from symmetry.

### 3 — LOW. The new guard accepted a qualifier, and its test pinned that (`DataBase_RuleField.cs:173`)

`ValidateIndexFieldIdentifier` reused `_bareIdentifier`, whose pattern allows an optional `Table.` prefix —
correct for the WHERE-clause sink it was written for, wrong here. A `CREATE INDEX` column list takes no
qualifier on any supported provider, so `Fields = [{ Name = "Docs.Status" }]` passed validation and emitted
`(Docs.Status)`, a syntax error rather than a resolvable column. The guard would have waved the payload's
harmless cousin through to break the statement anyway — and it also cuts against the invariant recorded in
TASK-211, that a qualifier is only ever emitted where a bare alias introduces it, which index DDL has none of.

**Worse: `A_plain_identifier_is_accepted` pinned `"Docs.Status"` as accepted**, so the suite would have
preserved it. Sharing one regex was the right instinct; this sink needed the unqualified branch. Now
`_unqualifiedIdentifier`, same `\A…\z` anchoring, and the test asserts refusal.

### 4 — LOW. A comment asserted an invariant the same commit reversed (`AbstractConnector_Create.cs:45`)

"Note the public `CreateIndexes(...)` below is UNCHANGED and still throws" — fifty lines above a
`CreateIndexes` that now defaults to `throwIfExists: false` and swallows 1061, with a test pinning that as
intended. A reader auditing the TASK-204 contract from this file got the pre-change answer. Exactly the
unqualified-comment problem TASK-245 called out in `MySqlIndexManager`'s old wording, committed in the same
change that criticised it.

## Verification

**1,100 tests green across 14 suites**, all four providers live (MySQL 8.4, PostgreSQL 16, SQL Server 2022,
on-disk SQLite) with `BIRKO_REQUIRE_LIVE` set. 14 new tests.

| revert | result | proves |
|---|---|---|
| **G** drop the `WithField` validation | **4 of 39** fail (Migrations) | finding 1's guard is load-bearing on the real migration path |
| **H** fall back to the qualified regex | **2 of 543** fail | finding 3 — a qualifier is refused, and the corrected test discriminates |
| **I** drop the manager's create tolerance | **1 of 18** fail (MySQL live) | finding 2, create half |
| **J** drop the manager's drop tolerance | **1 of 18** fail (MySQL live) | finding 2, drop half — the verb family is whole |

## Things worth carrying

- **"Enumerate that sink's callers by provenance" is only as good as the enumeration.** TASK-245 wrote that
  rule *and* shipped a violation of it in the same commit, because it found one caller and stopped. When a
  fix removes quoting from an interpolated identifier, grep for every construction of the object that carries
  the identifier — here `Tables.IndexColumn` — not only the translator you happen to be editing.
- **A late review is still a review.** The finding arrived after nine commits had landed; the value was in
  reporting it anyway rather than treating the merge as closing the question. Three of the four are defects in
  code committed an hour earlier.
- **A guard's own test can enshrine the guard's bug.** Finding 3 was pinned as correct behaviour by a test
  written in the same pass. Reusing a validator is right; reusing it without checking that *every branch of
  its pattern* makes sense for the new sink is how a check ends up accepting what it exists to refuse.
- **Uniformity claims need the layer above checked too.** TASK-245 made `CreateIndexes` uniform and recorded
  that it "makes MySQL agree with the others". One layer up, `IIndexManager` still didn't — and that path
  bypasses the funnel *by design*, which is exactly why it needed its own answer rather than inheriting one.

## Out of scope

- [[TASK-246]] / [[TASK-247]] / [[TASK-248]] remain as filed; none of them covers any of the four findings
  above, which is why this task exists rather than folding into them.
- The reviewer's "also checked, no defect found" list (override survey across all three trees, the retry and
  borrowed-transaction interaction with the 1061 swallow, cancellation not being swallowed by the `when`
  filters, MSSql's early return, the 1170/1062 boundary claims) is recorded here rather than re-verified.
