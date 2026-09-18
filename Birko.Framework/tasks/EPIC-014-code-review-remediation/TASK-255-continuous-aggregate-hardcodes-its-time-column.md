---
id: TASK-255
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P2
assignee: ai
created: 2026-08-18
depends-on: []
blocks: []
related: [TASK-253, TASK-472]
findings: [CR-H070]
pr: "Birko.Data.SQL c2038f1 · Birko.Data.Migrations.TimescaleDB e371c3a · Birko.Data.SQL.Tests 93cbfef · Birko.Data.Migrations.TimescaleDB.Tests a7b3427"
github-issue: null
jira-key: null
affects: [Birko.Data.Migrations.TimescaleDB]
---

# `BuildContinuousAggregateSql` still hardcodes `time` — CR-H070 unfixed in the method next door

Found while planning **TASK-253** (identifier quoting and folding across the same file), which audited
all nine emitters in `Birko.Data.Migrations.TimescaleDB/TimescaleDBMigration.cs` and turned this up as a
**different defect class** in one of them.

## What is wrong

`TimescaleDBMigration.BuildContinuousAggregateSql` (line 139) emits:

```csharp
time_bucket('{timeBucket}', time) AS bucket{groupBySql},
```

The bucketing column is the literal `time`. It is not a parameter of the method, so a caller cannot
supply it, and there is no overload that can.

**This is CR-H070, in the method immediately below the one CR-H070 was filed against.** That finding is
recorded verbatim in this same file, on `BuildCompressionPolicySql`'s doc comment:

> *"Don't hardcode the order/segment columns (CR-H070: 'time'/'device_id' fail on any table without a
> literal device_id column and are wrong for most schemas)."*

The compression-policy emitter was fixed for it — `orderByColumn` / `segmentByColumn` became parameters
— and the continuous-aggregate emitter beside it was not. So the reasoning was written down, applied to
one method, and the neighbour kept the defect with the explanation sitting four lines above it.

## Blast radius — measure before fixing

Unmeasured, deliberately: TASK-253 was a planning pass, not a run. What is known statically:

- Birko entities are PascalCase by convention and their columns are emitted **bare**, so PostgreSQL
  stores them folded — a time column declared `Timestamp` is stored `timestamp`, `Ts` is stored `ts`.
  Neither is `time`. So a continuous aggregate over a framework-created table should fail on
  `42703 column "time" does not exist` for every entity that does not happen to have a column literally
  named `time`.
- `time` is **not** a reserved word in PostgreSQL as a column name, so it is a legal name and a
  hand-made table could carry it. That is the only shape this method currently works for.
- Unlike TASK-253's defects, this one is expected to be **loud**: `ExecuteScript` has no `try`/`catch`
  and `TimescaleDBMigrationRunner.ExecuteSingleMigration` adds none, so the migration should fail rather
  than silently skip. Confirm that — TASK-253's step 5 measures the same question for the sibling
  emitters and the answer should be reused, not re-derived.

Whether any consumer calls this at all is **unknown and worth checking first**: TASK-247's sweep of all
16 consumer repos found 0 uses of `ISchemaBuilder` and 0 migration-declared indexes, so the honest
possibility is that this is latent public surface rather than a firing defect. Say which it is — TASK-246
had to be corrected after the fact for claiming live impact it did not have.

## Acceptance criteria

- [x] `BuildContinuousAggregateSql` takes the time column as a parameter, following
      `BuildCompressionPolicySql`'s shape exactly (CR-H070's own remedy applied to its neighbour) rather
      than a new convention.
- [x] The parameter's default is chosen **from a measurement, not from symmetry**: `"time"` preserves
      today's behaviour for any caller that works, but no framework-created table can have that column,
      so state whether the default should be required instead. A default that cannot work on any Birko
      entity is a silent no-op wearing a parameter's name (§ Conventions, TASK-245).
- [x] The `GROUP BY` / SELECT-list interaction is re-checked with the new parameter — CR-H071's
      dangling-comma guard must still hold, and its two existing tests must still pass.
- [x] Verified against **live TimescaleDB** by asserting a row in
      `timescaledb_information.continuous_aggregates` for a PascalCase table whose time column is *not*
      named `time`, and by reading rows back out of the aggregate. Not "the call did not throw"
      (TASK-209).
- [x] Proven able to fail: restore the hardcoded `time` and watch the new live test go red while the
      existing `metrics`/`time` offline tests stay green — they are the discrimination control.
- [x] Blast radius recorded as a number: how many consumer repos call `CreateContinuousAggregate`, and
      whether this defect is firing or latent.

## Out of scope

- Identifier quoting, folding and literal escaping in this same method (`viewName`, `sourceTable`,
  `timeBucket`, `selectClause`, `groupByClause`) — **[[TASK-253]] owns those**, and it will have touched
  this method already. Sequence after it to avoid a conflicting edit, or rebase onto it.
- `time_bucket`'s other arguments (origin, offset, timezone overloads). Adding them is a capability, not
  a defect fix.
- The `selectClause` / `groupByClause` arguments remaining caller-trusted SQL fragments — **[[TASK-260]] owns
  replacing them with a structured surface**, and it `depends-on` this task. TASK-253 pins
  that boundary deliberately; widening it is a separate decision.
- `BuildCompressionPolicySql`'s own `orderByColumn = "time"` default, which is the identical unusable-default
  defect in the neighbouring method — **[[TASK-279]] owns it**, spawned from this task's grill. Pick it after
  this one so the shared CR-H070 doc comment is not edited twice.

- The two `code-review` findings raised at this task's close gate — both pre-existing, and both about code
  this task did not touch (the reviewer attributed them to this diff; they are committed in `9b7f943` and
  `b9566d9`):
  - `IsHypertable` / `GetChunkInterval` ignore the schema half of the qualified name TASK-262 taught the
    class to accept — **[[TASK-280]] owns it**.
  - `CREATE MATERIALIZED VIEW … WITH (timescaledb.continuous)` and `refresh_continuous_aggregate` cannot run
    inside the runner's default transaction — **[[TASK-281]] owns it**, raised P1 because it may mean the
    emitter has never worked through the runner at all. Note against this task: the live tests added here
    call `Exec(...)` outside the migration context, so they do not exercise that path and could not have
    caught it.

## Implementation plan

_Drafted 2026-08-24, then grilled. The grill changed four things: the criteria-1-vs-2 "conflict" dissolved,
the guard became a new sibling rather than a reuse, the commit count went 3 → 4, and three of the four
predicted revert counts were wrong (one of them a false claim that a property was untestable). Decisions are
recorded inline where they bite._

### Step 0 — measurements before any code. Four are done.

- **M1 · Blast radius — DONE. The defect is LATENT public surface, not firing.**
  - 0 production callers in the `Framework` tree; the only references are the declaration plus three files
    in `Birko.Data.Migrations.TimescaleDB.Tests`.
  - **0 of 16** consumer repos call `CreateContinuousAggregate` / `BuildContinuousAggregateSql`.
  - **1 of 16** (`Birko.Sandbox`) imports `Birko.Data.Migrations.TimescaleDB.projitems`, so the surface is
    *compiled* by one consumer but invoked by none.
  - Grep caveat honoured (this epic has twice been bitten by unqualified greps): the sweep matched on the
    *method names* and separately on the *projitems import*, not on an attribute spelling, so a
    fully-qualified declaration cannot hide from it. This is the number criterion 6 asks for.
- **M2 · The proof tests already exist — TASK-253 left them as defect pins. DONE.** The work is to
  **invert** two pins, not author them (§ Conventions, TASK-265/TASK-261: invert, never keep both, or the
  suite asserts two opposite things about one method):
  - offline `TimescaleDBMigrationSqlTests.ContinuousAggregate_StillHardcodesTheTimeColumn_TASK255` —
    asserts `time_bucket('1 day', time)`, documented as "*current* behaviour, not correct behaviour".
  - live `MigrationEmitterLiveTests.ContinuousAggregate_cannotBeBuiltWhenTheTimeColumnIsNotNamedTime_TASK255`
    — asserts `PostgresException` / `42703` for a `Ts` column, documented "*the day TASK-255 lands, this
    test is what changes*".
- **M4 · Identifier treatment — DONE, witnessed by the fixture rather than argued from convention.**
  `CreateBaseTable()` emits `CREATE TABLE "MigMetrics" (Ts timestamptz NOT NULL, …)` — **quoted table, bare
  columns**, the framework's own shape — so the column is stored folded as `ts`. A bare `Ts` therefore folds
  and resolves, and `QuoteIdentifier` would emit `"Ts"` against a stored `ts` and fail. **Bare confirmed.**
- **M6 · CR-H070's remedy, read from the commit rather than from the signature — DONE, and it inverts the
  obvious reading.** `531d816` ("fix(migrations): correct TimescaleDB DDL generation (CR-H069/H070/H071)")
  states: *"adds optional orderByColumn (default 'time')"*. The method had **no such parameter before**, so
  the default existed to keep then-existing calls compiling — a **source-compatibility artefact, not a
  judgement that `"time"` is a good value**. M1 says we have no callers to be compatible with. The same
  commit also edited the exact defect line (`time_bucket('{timeBucket}', time) AS bucket` →
  `… AS bucket{groupBySql},`) while fixing CR-H070 four methods above: the defect survived a commit that
  touched its own line and named its own finding.
- **M3 · Loudness — still to confirm, cheaply.** The live pin already shows a `PostgresException` escaping
  `Exec`, so the failure is loud. Read `ExecuteScript` / `TimescaleDBMigrationRunner.ExecuteSingleMigration`
  for a `catch` to close it; the criterion-4 catalogue assertion is required either way (TASK-209).
- **M5 · Positional-rebinding — confirm at compile time** by building the test project before updating its
  call sites (see step 2).

### Step 1 — the new parameter is emitted BARE, and guarded by a NEW sibling validator

`time_bucket('1 day', <col>)`'s second argument is a **column reference in real identifier position** inside
the `CREATE MATERIALIZED VIEW` body — none of the three treatments the neighbouring emitters use:

| Sink | Position | Treatment | Why not here |
|---|---|---|---|
| `create_hypertable`'s column | `name` inside a quoted literal, compared against `pg_attribute.attname` | `CatalogueNameLiteral` (pre-fold, never quote) | Not a literal here; the parser reads this as an identifier and folds it itself |
| `compress_orderby` | expression fragment inside a quoted literal | `SqlLiteral.EscapeLiteral` only | Not inside quotes here, so escaping contains nothing |
| `viewName` / `sourceTable` | real identifier, table position | `QualifiedIdentifier` (quoted) | § Conventions quotes **tables**, never columns |

**Bare**, per § Conventions and confirmed by M4. Bare also keeps this argument consistent with
`selectClause` / `groupByClause`, which are caller-written raw SQL naming the same columns in the same
statement.

**Unquoting removes accidental containment, so this sink needs a guard** (§ Conventions, TASK-245/249). The
argument is caller-derived free text and `TimescaleDBMigration` holds a table name with no entity type, so
metadata resolution is unavailable and this takes the sanctioned weaker tier: prove the string is a single
bare identifier. Its guarantee, stated honestly — it **cannot** fix a `[NamedField]` remapping; what it
guarantees is that every measured payload (each carrying a space, operator, parenthesis or statement
separator) is refused, and that a bare identifier naming no column is at worst a database error, i.e. *a
wrong answer that reports itself*.

**Guarded by a new `DataBase.ValidateColumnIdentifier`, NOT by reusing `ValidateIndexFieldIdentifier`.**
The mechanics of the latter are exactly right (it uses `_unqualifiedIdentifier`, so it rejects a `Metrics.Ts`
qualifier, which is correct here — one `FROM` table, so a qualifier carries no information). Its **message**
is not: it says *"Index field 'X' … interpolated bare into the CREATE INDEX statement … not valid in an index
column list"*, which would tell a migration author about indexes. That is precisely what § Conventions
(TASK-215) forbids — **"a refusal names the door THIS caller has"** — the entry about an async twin throwing
a message naming `DeleteAll()`. So: new method, **sharing `_unqualifiedIdentifier`**, so the one-regex
doctrine (*"one regex, so the two sinks cannot drift apart about what an acceptable identifier is"*) holds.

*Cost, measured rather than assumed:* a fourth commit in `Birko.Data.SQL` (its own git repo), ~15 lines. No
new coupling — `Birko.Data.Migrations.SQL/Context/SqlSchemaBuilder.cs:324` **already** calls
`Birko.Data.SQL.DataBase.ValidateIndexFieldIdentifier` across repos for the same purpose, and
`TimescaleDBMigration.cs` already carries `using Birko.Data.SQL;`.

**Documented limit (TASK-262 family), recorded not fixed:** a hand-created **quoted mixed-case** column is
unreachable through this emitter, since bare folds. With 0 callers an opt-out is speculative API.

### Step 2 — required, not defaulted; and the rebinding hazard is recorded

**Required, and the criteria do not actually conflict.** The task's criterion 1 ("follow
`BuildCompressionPolicySql`'s shape exactly") and criterion 2 ("choose the default from a measurement") name
**different siblings**, and the file already carries both conventions, split by *what kind of argument it is*:

| Argument | Signatures | Default |
|---|---|---|
| `timeColumnName` — the time dimension | `CreateHypertable`, `CreateHypertableWithSpace` (+ both `Build…`) | **required** |
| `orderByColumn` — an *ordering expression* (`ts DESC` is legitimate) | `AddCompressionPolicy` (+ `Build…`) | `= "time"` |

The new parameter is a time-dimension column name — the first kind, required in four signatures. Criterion 1
is satisfied on the part that is about *shape* (a parameter on both the `protected virtual` and the
`internal static`, not a new convention); criterion 2 is answered by the file's own convention for the same
*kind* of argument. Reinforced by M6: the sibling's default is a compatibility artefact, and M1 says there is
nothing to stay compatible with. Plus: omission becomes a **compile error** rather than a `42703` at
migration time.

```csharp
protected virtual void CreateContinuousAggregate(IMigrationContext context, string viewName,
    string sourceTable, string timeBucket, string timeColumn, string selectClause, string groupByClause = "")

internal static string BuildContinuousAggregateSql(AbstractConnector connector, string viewName,
    string sourceTable, string timeBucket, string timeColumn, string selectClause, string groupByClause = "")
```

**⚠ Hazard — RECORDED, deliberately not fenced.** Every parameter is `string`, so a pre-existing
**5-argument** call fails to compile (good), but a pre-existing **6-argument** call *silently rebinds* —
`selectClause`→`timeColumn`, `groupByClause`→`selectClause`, `groupByClause` falling back to `""`. It
compiles and emits a wrong statement, and **no parameter ordering fixes it**. Acceptable only because M1
measured 0 non-test callers and all test call sites are updated in the same change. Fencing it (a wrapper
type for the column, or an `[Obsolete]` throwing overload pinning the old arity) was considered and
rejected: permanent API surface defending against a caller that does not exist — TASK-262's
speculative-API argument. **Say this in the commit body**, because a future reader who does have consumers
must not copy the manoeuvre.

### Step 3 — production edits

**`Birko.Data.SQL/SQL/DataBase_RuleField.cs`** (commit 1)
- Add `public static string ValidateColumnIdentifier(string?)` beside its two siblings, sharing
  `_unqualifiedIdentifier`; message names a column reference interpolated bare into a statement, and states
  that a `Table.` qualifier is not accepted. Doc-comment it as the fourth sink in the
  interpolated-identifier family.

**`Birko.Data.Migrations.TimescaleDB/TimescaleDBMigration.cs`** (commit 2)
1. Add `timeColumn` to both signatures as above (lines ~260 and ~289).
2. Line ~296: `time_bucket('{SqlLiteral.EscapeLiteral(timeBucket)}', {DataBase.ValidateColumnIdentifier(timeColumn)})`.
3. **Rewrite the doc-comment `<para>` that currently records the defect** ("*The bucketing column is still
   the hardcoded literal `time`* … TASK-255 owns it") into the new contract: a required parameter, emitted
   bare because it is a column reference in identifier position, guarded because it is caller-derived, plus
   the quoted-mixed-case limit. A stale comment still claiming the defect is unfixed is worse than none.
4. Check the **class-level `<remarks>`** (~100-108) and `BuildCompressionPolicySql`'s doc comment, where
   CR-H070 is recorded verbatim — the finding is now fixed in *both* methods, so the note must say so.
   (The sibling's *default* is a separate defect → spawned, see below; do not silently fix it here.)
5. **CR-H071 is untouched by construction, and re-asserted anyway.** `groupBySql` is computed before the
   interpolation and the new parameter sits *inside* `time_bucket(...)`, before `AS bucket` — so the comma
   logic in the SELECT list and the `GROUP BY` is byte-identical.

### Step 4 — tests (commit 3)

**Pins — pass either way; the discrimination control criterion 5 asks for.** All need a mechanical extra
argument, which is exactly why they prove nothing on their own:
`ContinuousAggregate_QuotesTheViewAndTheSourceTable`, `…_EmptyGroupBy_HasNoDanglingComma` (CR-H071),
`…_WithGroupBy_IncludesColumnsInSelectAndGroupBy` (CR-H071), `…_LeavesExpressionClausesIntact` (TASK-260's
boundary), `…_ContainsABreakoutInTheViewAndSourceTable`, `…_ContainsABreakoutInTheTimeBucket`,
`RefreshContinuousAggregate_TreatsTheViewAsARegclass_UnlikeTheCreate`.

**Inverted — not deleted:**
- `ContinuousAggregate_StillHardcodesTheTimeColumn_TASK255` → `…_TakesTheTimeColumnAsAParameter`, asserting
  `time_bucket('1 day', Ts)`.
- `ContinuousAggregate_cannotBeBuiltWhenTheTimeColumnIsNotNamedTime_TASK255` →
  `…_isCreatedWhenTheTimeColumnIsNotNamedTime`, asserting the
  `timescaledb_information.continuous_aggregates` row.
- `ContinuousAggregate_isCreatedForPascalCaseNames_onlyWithATimeColumn` → drop `onlyWith`, pass `"time"`
  explicitly; stays green as the pin that a literally-named `time` column still works.

**New (4):**
- **live** — rows read back *out of* the aggregate for a PascalCase table whose time column is `Ts`
  (criterion 4's second half; a catalogue row alone is "it appeared in a view", and TASK-209 is explicit that
  a non-throwing DDL call proves nothing here). Insert rows spanning two buckets, refresh, assert values.
- **injection** — a literal breakout in `timeColumn` is **refused**. Note this asserts a *throw*, unlike all
  16 siblings, which assert the payload survives as inert escaped text.
- **offline** — a `Metrics.Ts` qualifier is refused (TASK-249's corollary).
- **reflection** — `timeColumn` has no default:
  `GetMethod(…, NonPublic|Static).GetParameters().Single(p => p.Name == "timeColumn").HasDefaultValue
  .Should().BeFalse()`. Per § Conventions (TASK-117): *"'I didn't add it' is construction, not evidence, and
  the next person breaks it silently."* Shared `.projitems` compile into the test assembly, so non-public
  reflection works.

**⚠ The injection suite's class-level doctrine must be rewritten in this commit.** It currently asserts
*"Every one of these arguments ends up inside a single-quoted literal or as a quoted identifier"* — the new
argument is contained by **refusal** instead, a third category. Left alone, that paragraph asserts a
guarantee the file no longer makes.

**Reverts / mutations — corrected at the grill; every one now reds ≥ 1, so no zero-red mutation remains:**

| # | Mutation | Predicted |
|---|---|---|
| R1 | Keep the parameter, restore the hardcoded `time` | **3 red** — inverted offline pin, inverted live test (42703), new rows-back test. **Both CR-H071 tests stay green** — the control |
| R2 | Drop the identifier guard | **2 red** — injection breakout **and** qualifier rejection (the first draft said 1, forgetting the second) |
| R3 | `QuoteIdentifier` instead of bare | **3 red** — both live tests **and** the inverted offline pin, which would see `"Ts"` (the first draft said 2) |
| R4 | Give `timeColumn` a `"time"` default | **1 red** — the reflection pin. *The first draft predicted 0 and called required-ness "not test-assertable". That was false; `ParameterInfo.HasDefaultValue` settles it, and TASK-117's rule says it must be asserted.* |

### Step 5 — verification run

`dotnet test --nologo` on `Birko.Data.Migrations.TimescaleDB.Tests` (48 baseline) with **`BIRKO_REQUIRE_LIVE=1`**
and a live TimescaleDB container up, plus `Birko.Data.TimescaleDB.Tests` and `Birko.Data.SQL.Tests` (the
guard's own repo). **Start the container first** — TASK-259 recorded falling into exactly this trap:
`BIRKO_REQUIRE_LIVE` with no server reports gated suites as *failures*, and narrowing the flag to make them
pass is how a run stops meaning anything. Report count, 0 failed, 0 skipped, and new-test count.

### Step 6 — FOUR commits, production before aggregator

1. `Framework/Birko.Data.SQL` — `fix(CR-H070): a column identifier sink gets its own refusal message`
2. `Framework/Birko.Data.Migrations.TimescaleDB` — `fix(CR-H070): the continuous aggregate takes its time column`
3. `Framework.Tests/Birko.Data.Migrations.TimescaleDB.Tests` (+ `Birko.Data.SQL.Tests` if the guard gets its
   own unit test there) — `test(CR-H070): invert the two TASK-255 defect pins`
4. `Framework/Birko.Framework` — `tasks(TASK-255): …` plus the § Conventions entry, a `### Recent Updates`
   note, and the regenerated dashboard.

Production first so the SHA can go in `pr:`. Stage explicitly, never `git add -A`. **No `Co-Authored-By`
trailer** — note `531d816` carries one; the convention post-dates it, so do not copy it from neighbouring
history.

### Spawned / recorded

1. **`BuildCompressionPolicySql`'s `orderByColumn = "time"` default → spawned as [[TASK-279]].** Same
   defect, and measured never-exercised: the live suite's `Probe.Compression(c, t, after, orderBy)` passes
   it explicitly and there are no other call sites. Outside criteria 1-6, so a spawn, not a widening.
2. **The quoted-mixed-case-column limit** (step 1) — recorded on the method; spawn only if a real caller
   appears, per TASK-262 on speculative opt-outs.

**No split signal in the core:** one method plus doc comments, one small guard, two pins inverted, four new
tests. That is one task.

### Results — measured 2026-08-24

Verified against **live TimescaleDB 2.29.2 / PostgreSQL 16.15** (`BIRKO_REQUIRE_LIVE=1`, container
`birko-ts-259` on port 15433 — the same image TASK-259/261/262 measured against):

| Suite | Result |
|---|---|
| `Birko.Data.Migrations.TimescaleDB.Tests` | **61 passed, 0 failed, 0 skipped** (56 → 61, +5) |
| `Birko.Data.SQL.Tests` | **653 passed, 0 failed, 0 skipped** (+20, the new guard suite) |

No new nullable warning in either project (the CS8765s in `Birko.Data.SQL.Tests` are pre-existing in
`TestDbCommand.cs`, `LimitOffsetEmissionTests.cs` and `RetryWhenOwnedTests.cs`; my files add none).

**Mutations — all four run, and three of the four grilled predictions were still wrong:**

| # | Mutation | Predicted | Measured |
|---|---|---|---|
| R1 | Restore hardcoded `time`, guard kept | 3 red | **3 red** ✓ — rendering pin + both live tests; **CR-H071 pair green**, and the literal-`time` live test green (the discrimination control) |
| R2 | Drop the guard | 2 red | **3 red** — the `[Theory]`'s two payloads count separately |
| R3 | `QuoteIdentifier` instead of bare | 3 red | **3 red** ✓ — and its two *live* reds are what witness the bare choice: `"Ts"` genuinely does not resolve against the stored `ts` |
| R4 | Give `timeColumn` a `"time"` default | 1 red | **does not compile** — `CS1737`, because the parameter precedes the required `selectClause` |
| R5 | Point `ValidateColumnIdentifier` at `ValidateIndexFieldIdentifier` | — | **1 red** — the message test only, with `It_agrees_with_the_index_guard…` still green: the two guards agree on *what* they accept and differ only in *what they say*, which is option A's whole claim |

**R4 is the finding worth keeping.** Required-ness is enforced by the **compiler**, not by a test — a
default is not expressible at that parameter position at all. So `ContinuousAggregate_TimeColumnHasNoDefault`
is **defensive, not witnessed** (§ TASK-261): it guards a future reordering that would make a default legal
again, and it is recorded as such rather than being claimed as R4's prover.

**M5, measured at the first build.** Of the 8 pre-existing call sites, **6 failed loudly** (`CS7036`,
5-argument) and **2 rebound silently** (6-argument: `…_WithGroupBy_…` and `…_LeavesExpressionClausesIntact`).
So the hazard in step 2 is real rather than theoretical. It was *partly* self-fencing — `timeColumn` then
received `"avg(value) AS avg_value"`, which `ValidateColumnIdentifier` refuses — but that is luck, holding
only while the displaced select clause is not itself a bare identifier. Recorded, not relied on.

**Blast radius, restated as the number criterion 6 asks for: LATENT.** 0 framework production callers,
**0 of 16** consumer repos calling it, **1 of 16** (`Birko.Sandbox`) merely importing the `.projitems`. No
consumer migration is currently broken by this defect; the fix removes a trap rather than repairing damage.

## Human test plan

- [x] N/A — mechanical; the proof is a row in `timescaledb_information.continuous_aggregates` over a
      table whose time column is not named `time`, plus rows read back from the aggregate.
