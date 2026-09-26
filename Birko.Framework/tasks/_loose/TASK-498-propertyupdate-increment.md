---
id: TASK-498
parent: null
feature: null
status: done
priority: P1
assignee: ai
created: 2026-09-26
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# `PropertyUpdate<T>` can only SET a constant — add an atomic `Increment`

Requested by consumer Symbio, whose TASK-774 (public redirect resolve for a headless storefront) needs
`Redirect.HitCount = HitCount + 1` as **one** statement on an anonymous GET. Decided 2026-09-26: this
framework task lands **first**, and Symbio TASK-774 consumes it.

## Context

`Birko.Data.Stores/PropertyUpdate.cs` holds `List<(LambdaExpression Property, object? Value)> Assignments`,
filled only by `Set(x => x.Prop, value)`. Every native translator turns that into a constant assignment:

| Provider | Today | Where |
|---|---|---|
| SQL (every dialect) | `UPDATE t SET col = @SETcol …` | `Birko.Data.SQL/Stores/AsyncDataBaseBulkStore.cs:241`, `DataBaseBulkStore.cs:182` → `SQL/Connectors/AbstractAsyncConnector_Update.cs:123`, `AbstractConnector_Update.cs:117` |
| MongoDB | `Builders<T>.Update.Set` combined → `UpdateManyAsync` | `Birko.Data.MongoDB/Stores/AsyncMongoDBStore.cs:437`, `MongoDBStore.cs:307` |
| Elasticsearch | painless `ctx._source.f = params.p` | `Birko.Data.ElasticSearch/Stores/ElasticSearchStoreHelper.cs:89` |
| everything else | `PropertyUpdate.ApplyTo` (reflection) inside read-modify-save | `PropertyUpdate.cs:36` |

So a counter can be written only as read-modify-write (a lost update under concurrency) or as a
compare-and-set that drops contended increments. Neither is a counter.

**The SQL connector already has the hook:** `UpdateAsync(..., isExpressionValues: true, ...)` emits the
`fields` values verbatim as SET fragments and binds `values` by key (`AbstractAsyncConnector_Update.cs:136-151`).
So the store can build `col = @SETcol` for a Set and `col = col + @SETcol` for an Increment itself, and no
dialect connector has to change. **Watch the quoting:** the non-expression branch writes the bare field name
today. The expression fragments must go through the dialect's `QuoteIdentifier`, or a reserved-word column
breaks (Symbio `CLAUDE-rules-sql.md` § reserved words).

## Acceptance criteria

- [x] `PropertyUpdate<T>.Increment<TProperty>(Expression<Func<T, TProperty>>, TProperty delta)` exists, constrained
      to numeric types (`INumber<TProperty>`, or an explicit list if the target frameworks cannot use generic math;
      decide in the plan and say why). Nullable properties do not compile (owner decision 2026-09-26). It chains with `Set` in one `PropertyUpdate`.
- [x] Assignments carry their kind (`Set` | `Increment`), and the storage shape changes so that **a translator that
      has not been updated fails to compile.** It must not silently write `delta` as a constant: an unhandled
      Increment treated as a Set turns `+1` into `= 1`, and that is the one failure this task must not ship.
- [x] SQL: `Set` + `Increment` in one `PropertyUpdate` → **one** `UPDATE` statement with `col = col + @p`,
      table quoted, columns bare per rule 17 (owner decision 2026-09-26). Asserted on real SQLite, plus every other dialect whose live suite runs here (report the
      ones that were skipped, with `BIRKO_REQUIRE_LIVE` in mind, rather than counting them as passed).
- [x] MongoDB: `Builders<T>.Update.Inc`. Elasticsearch: `ctx._source.f += params.p`.
- [x] `ApplyTo` fallback adds instead of assigning. Its XML-doc states that the fallback is not atomic.
- [x] Every store decorator that touches `PropertyUpdate` still works: timestamp/audit/soft-delete/tenant/sluggable
      bulk wrappers, event-sourcing, SQL caching. `LocalizedPropertyUpdateHelper` refuses `Increment` on a localized
      field, loudly, on every culture (owner decision 2026-09-26).
- [x] A concurrency test: N parallel `Increment(1)` calls on one row end at exactly N (real SQLite).
- [x] CHANGELOG entry in `Birko.Framework/CHANGELOG.md` (breaking change to `Assignments`) + CLAUDE.md § Recent Updates, one commit (monorepo rule).
- [x] `Decrement(prop, delta)` is a pure alias for `Increment(prop, checked(-delta))` — no new assignment kind.
      Constrained to signed numbers (`ISignedNumber<TProperty>`) so an unsigned property does not compile (negating
      a `uint` wraps to `MaxValue` and would add ~4 billion). Added 2026-09-26 at the owner's request.
- [x] `float`, `double` and `decimal` deltas work on SQL (real SQLite asserted for `double` and `decimal`), and the
      behaviour per provider is documented where a stored representation prevents a native add (MongoDB's default
      string `decimal`). Added 2026-09-26 at the owner's request.

## Human test plan

N/A — a library API with no UI; every criterion is asserted by automated tests (real SQLite end-to-end,
translator shape tests, decorator regressions), and the atomicity guard was proven able to fail by mutation.
The live PostgreSQL/MySQL/MSSql/MongoDB twins are automated too; what they still owe is a run on CI's
`live-tests` job, not a human.

## Progress log

- 2026-09-26 — Implemented per plan. Whole `Birko.Framework.slnx` builds (0 errors; no new nullable warnings).
- Prove-it-can-fail: SQL translator mutated to emit `col = @p` for an increment → all 7 SQLite end-to-end tests
  red, the 50-caller concurrency test ending at **1**. Restored.
- Measured (rule 54): SQLite `10.10m + 0.20m` = `10.299999999999999m` under both `REAL` and `NUMERIC(18,2)`;
  a Set of `10.30m` round-trips exactly. Pinned in the test and documented — the plan's prediction was wrong.
- Suites (all green): Data 212, SQL 699, SqLite 386, MongoDB 104, ElasticSearch 164, Localization 124,
  Patterns 68, Tenant 80, EventSourcing 7, SQL.Caching 27, InMemory 74, BackgroundJobs.{MongoDB,ElasticSearch} 6+6.
- **Live suites SKIPPED, not passed** — no `BIRKO_*` server on this machine: PostgreSQL (4), MySQL (4), MSSql (4),
  MongoDB (3) `PropertyUpdateIncrementLiveTests`. Gate verified both ways: early return + SKIPPED message without
  env, all 15 fail under `BIRKO_REQUIRE_LIVE=1`. The PostgreSQL bare-column fold and the MySQL/MSSql exact
  decimals are therefore proven only by CI's `live-tests` job.
- CLAUDE.md § Recent Updates is 11.2 KB after this entry → roll the oldest via `/roll-birko-changelog` at close.
- `docs/specs/bulk-filter-operations.md` (§ PropertyUpdate, the tuple shape) is now stale → `/specs regen` at close.
- Close gate (4 axes, run by separate reviewers). Fixed in this task: a CS8603 in a new test (the blocker);
  `ApplyTo` silently skipping a non-property selector (now throws); nested-member increments (refused — Mongo would
  have `$inc`ed the leaf name at the top level); `char`/unsigned/`Half` etc. admitted by `INumber` (narrowed to
  short/int/long/float/double/decimal, rule 29); unchecked fallback overflow (now `checked`); the `@SET` name had two
  producers (now `AbstractConnector.SetParameterName`); a Mongo test that froze the class map before registration
  (rule 61, reproduced order-dependent); a localizable Set beside an increment silently losing atomicity on a
  non-default culture (refused); the ES "atomic" claim (corrected, TASK-502); an untested `float` (tested); a false
  doc pointer; the pattern registered in CLAUDE.md § Conventions. Spawned: TASK-500, 501, 502, 503. Security: pass.
- Recent Updates rolled: the 2026-09-19 entry moved to CHANGELOG.md; entries now 6.5 KB.
- 2026-09-26, after close — live suites run on local Docker (the workflow's images): PostgreSQL 4/4, MySQL 4/4,
  MSSql 4/4 green, proving the bare-column fold and exact declared-precision decimals. MongoDB went red on one test,
  and that **falsified the addendum's premise** (rule 54): MongoDB.Bson 3.12 stores `decimal` as **Decimal128** by
  default, not String — measured with a probe. The refusal stays (it is right for an explicit string
  representation); the live test, translator doc, README, CHANGELOG and spec wording were corrected, and the twin
  now also proves a plain `decimal` increments exactly.

## Out of scope

- Consumer adoption. Symbio's `IRepository` surface and the `Redirect.HitCount` counter are Symbio TASK-774.
- Other arithmetic (multiply, min/max, set-if-greater). Add them when someone needs one.
- Stores with no bulk filter-update path today (they keep whatever they do now).

## Implementation plan

Drafted 2026-09-26 (Plan agent + owner additions: `Decrement` alias, float/decimal).

⚠ Acceptance criteria question (a) — CHANGELOG: there is no per-project `CHANGELOG.md`; the only one is
`Birko.Framework/CHANGELOG.md`, and the repo is now a monorepo with one commit per fix. Read as: one breaking-change
entry there (CLAUDE-maintenance.md § Breaking changes) + a CLAUDE.md § Recent Updates entry, one commit.

⚠ Acceptance criteria question (b) — quoting: "identifiers quoted" contradicts rule 17 (*quote table identifiers;
never quote column identifiers*). `CreateTable` emits columns bare, so on PostgreSQL a quoted `"HitCount"` would
miss the folded `hitcount` column. Rule 17 also measures that a framework-created table cannot carry a reserved-word
column. Plan: table quoted (connector already does), columns bare, resolved from metadata via `GetFieldFromLambda`
(rule 15). Symbio's `CLAUDE-rules-sql.md` governs Symbio's hand-written SQL, not framework DDL.

⚠ Acceptance criteria question (c) — nullable: every Data project targets net10.0, so `INumber<TProperty>` is used.
Side effect: `int?`/`decimal?` do not satisfy it, so incrementing a nullable property does not compile. Intended —
`NULL + 1` is NULL in SQL, an error in Mongo, a throw in painless.

⚠ Acceptance criteria question (d) — localization: refuse `Increment` on a localizable field on **every** culture,
not only non-default, so the same call cannot pass or fail by request culture.

### Storage shape
Replace `List<(LambdaExpression Property, object? Value)>` with a closed hierarchy, no shared `Value`, no `Deconstruct`:
- `abstract PropertyAssignment` (private protected ctor) — `Property`, `Match<TResult>(Func<SetAssignment,TResult>, Func<IncrementAssignment,TResult>)`.
- `sealed SetAssignment` — `Value`. `sealed IncrementAssignment` — `Delta`, internal typed `AddTo` closure.
- `internal IReadOnlyList<PropertyAssignment> Assignments`.

`foreach (var (p, v) …)` stops compiling; `Match` (not an enum + common `Value`, not a subtype `switch` whose
non-exhaustiveness is only CS8509 *warning*) means no translator can reach a delta without naming the increment branch.
Rule 55: a second assignment to a member already incremented (or incrementing one already set) throws
`InvalidOperationException`; Set+Set keeps today's behaviour.

### Steps
1. `Birko.Data.Stores/PropertyUpdate.cs` (+ `PropertyAssignment.cs` in the projitems): classes, `Increment<TProperty>
   where TProperty : INumber<TProperty>`, `Decrement<TProperty> where TProperty : ISignedNumber<TProperty>, INumber<TProperty>`
   → `Increment(p, checked(-delta))`. `ApplyTo` adds for Increment. XML-doc: fallback is read-modify-write, **not atomic**;
   negative delta decrements.
2. SQL — new internal `Birko.Data.SQL/Stores/PropertyUpdateSqlTranslator.cs`, shared by `AsyncDataBaseBulkStore.cs:241`
   and `DataBaseBulkStore.cs:182`; always `isExpressionValues: true`. Set → `col = @SETkey`, Increment →
   `col = col + @SETkey`. No connector/dialect change. SQLite `decimal` is `NUMERIC(p,s)` (numeric affinity), so
   `col + @p` stays numeric.
3. MongoDB — internal `MongoPropertyUpdateTranslator.Build<T>` shared by both stores; Set → `Update.Set`, Increment →
   `Update.Inc`. Default-serialized `decimal` (string) makes `$inc` fail at the server — loud; documented.
4. Elasticsearch — `ElasticSearchStoreHelper.BuildUpdateScript` via `Match`: `=` vs `+=`.
5. Localization — `LocalizedPropertyUpdateHelper.RefuseIncrementOnLocalizableField`, called first in
   `Update(filter, PropertyUpdate)` of both localized bulk wrappers; `NotSupportedException` naming the field.
6. Event-sourcing, base bulk stores, decorators, test doubles: `ApplyTo`/pass-through only — compile unchanged.
7. Docs: `Birko.Data.Stores` + `Birko.Data.SQL` CLAUDE.md/README, `docs/`, CHANGELOG breaking-change entry (old/new
   `Assignments` shape, migration: use `Match`), CLAUDE.md § Recent Updates.
8. One commit, staged explicitly, no Co-Authored-By trailer.

### Tests → criteria
- C1 API / Decrement / float-decimal: `tests/Birko.Data.Tests/Stores/PropertyUpdateTests.cs` — chaining, conflict
  throws, int/long/double/decimal, Decrement = negative Increment, `Decrement(int.MinValue)` throws `OverflowException`.
- C2 shape: whole-solution build; grep §readers for tuple deconstruction.
- C3 SQL: `tests/Birko.Data.SQL.Tests/PropertyUpdateSqlTranslatorTests.cs` (exact fragments, no quotes on columns);
  `tests/Birko.Data.SQL.SqLite.Tests/PropertyUpdateIncrementEndToEndTests.cs` (Set+Increment correct, other rows
  untouched, one `Update` call via counting connector, sync twin, double + decimal, Decrement); live twins for
  PostgreSQL/MySQL/MSSql gated on `BIRKO_*_HOST` + `BIRKO_REQUIRE_LIVE` — skipped ones reported as skipped.
- C4: Mongo translator render test (`$inc` + `$set`); ES `BuildUpdateScript` test (no ES live suite — say so).
- C5: `ApplyTo` adds (10 + 5 = 15); InMemory `Update(filter, inc)`.
- C6: Patterns decorators (Timestamp/Audit/SoftDelete/Sluggable), Tenant, EventSourcing, SQL.Caching, Localization
  (refuse on default + non-default culture, sync + async; non-localizable passes).
- C7: 50 parallel `Increment(1)` on SQLite → 50; control through `Update(filter, Action<T>)` shows lost updates
  (proves the test can fail).

### Addendum (Decrement, numerics per provider)
- **Cast bypass:** `x => (int)x.NullableCount` / `x => (long)x.IntProp` is a `UnaryExpression` that
  `GetFieldFromLambda` unwraps, so the constraint alone does not refuse nullables. `Increment`/`Decrement` check at
  runtime that the member's `PropertyType == typeof(TProperty)` and throw `ArgumentException`. Tested.
- **MongoDB decimal:** `MongoSerialization.cs` sets no decimal representation, so the driver default (String) applies
  and `$inc` would fail at the server. Rule 41: the translator refuses up front — member serializer is
  `IRepresentationConfigurable` with `BsonType.String` → `NotSupportedException` naming the field and
  `[BsonRepresentation(BsonType.Decimal128)]`. Live test: Decimal128 works, default is refused before sending.
- **SQLite decimal:** `REAL` (or `NUMERIC(p,s)` with declared precision) — already double precision; measure
  `10.10m + 0.20m` under both and record the result (rule 54), document, do not fix here.
- **MySQL/MSSql bare `DECIMAL`** is (10,0)/(18,0) — pre-existing fraction loss on Set too; live tests declare precision.
- **ES:** AutoMap maps decimal → `double`; painless `+=` runs in double. Pre-existing; documented.
- Legacy NULL in a non-nullable column stays NULL after `col + 1` — document only.

### Noted, not in scope (spawned at close)
- Timestamp/Audit decorators mutate the caller's `PropertyUpdate` (rule 27 smell) → deferred to TASK-500.
- ES writes `null` as `string.Empty` in `BuildUpdateScript` → deferred to TASK-501.
- ES `UpdateByQuery` response discarded, so a contended increment is silently lost (close-gate intent review) → deferred to TASK-502.
- MongoDB native update writes under the C# name, ignoring `[BsonElement]` / `_id` (close-gate correctness review) → deferred to TASK-503.
