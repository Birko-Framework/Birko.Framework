---
id: TASK-109
parent: STORY-051
feature: FEATURE-014
status: done
priority: P0
assignee: ai
picked-by: fix-next
created: 2026-07-30
depends-on: []
blocks: [TASK-137]
related: [TASK-138]
# One fix, four production repos + three test repos (polyrepo — see CLAUDE.md § Integration model).
# None of these resolve under `git show` from this aggregator; that is also why the spec's
# shaped-by evidence pass cannot run here.
pr: >-
  Birko.Data.SQL@d8c2f40, Birko.Data.Stores@3cd8b2a, Birko.Data.InMemory@4f680b7,
  Birko.Data.MongoDB@88f96ee, Birko.Data.SQL.Tests@349c7b3,
  Birko.Data.SQL.SqLite.Tests@35fc122, Birko.Data.InMemory.Tests@86df89c
github-issue: null
jira-key: null
findings: [SH-H002, SH-M023]
---

# A null or untranslatable filter renders `DELETE FROM "T"` — the whole table

## Context

`../Birko.Data.SQL/Stores/DataBaseBulkStore.cs:156` — **CONFIRMED**.

`Delete(filter)` forwards `filter as LambdaExpression` to `Connector.Delete` with **no null check and no
translatability check**. `AbstractConnector_Delete.cs:30` builds `"DELETE FROM " + QuoteIdentifier(name)`
and appends the `WHERE` **only when conditions exist**. So zero conditions is a whole-table delete.

Zero conditions happens two ways, and that is what makes this high rather than merely sloppy:
`DataBase.ParseConditionExpression`'s fall-through returns `Array.Empty<Condition>()` (`DataBase.cs:818`)
both for a **null filter** and for **every predicate shape it cannot parse** — e.g. the
`InvocationExpression` produced by `x => pred(x)`. A filter that is silently dropped is indistinguishable
from no filter at all.

Same shape at `DataBaseBulkStore.cs:129` (an `UPDATE` with no `WHERE`) and at
`AsyncDataBaseBulkStore.cs:174/211/213`.

**`SH-M023` is the portable-store twin and belongs here.** `AbstractBulkStore.cs:99` declares
`Delete(filter)`'s parameter non-nullable but never guards it; `Delete(null!)` materialises the entire table
and hands it to `DeleteCore`. Same at `AbstractAsyncBulkStore.cs:146`, and at `76`/`117` for
`Update(filter, Action)` / `UpdateAsync`. One decision covers both layers.

## The fix shape is already in this repo

`Birko.Data.ElasticSearch` solved exactly this under CR-H047 (2026-07-27) and its split is the precedent to
follow: **three outcomes stay distinct, and only one is an error.**

| Outcome | Meaning | Behaviour |
|---|---|---|
| No filter | read/act on everything, deliberately | allowed **only** where a null filter is a documented API (reads) |
| Matches nothing | a legitimate translation | render an always-false predicate — never omit the `WHERE` |
| Cannot be expressed | translator limitation | **throw** |

ES has two helpers for this — `ParseFilterQuery` (optional filter) and `ParseRequiredFilterQuery` (the
destructive paths, where a null filter throws). SQL needs the same seam so the destructive paths cannot
inherit the permissive one. Note the empty-`IN` work from the same date already established that an
untranslatable operand must not silently collapse: `1 = 0` for an empty `IN`, `1 = 1` for an empty
`NOT IN`.

## Acceptance criteria

> **Criteria 1, 3 and 5 were corrected on 2026-08-03, before any code, after the grill established that the
> original mechanism could not work** — `ParseConditionExpression` has four legitimate empty-result paths, so
> untranslatability cannot be inferred from an empty condition set. The *intent* of every original criterion
> is preserved and in places strengthened; see `## Implementation plan › Resolved decisions`. Correcting a
> target before the work is legitimate; rewriting one afterwards to fit the result is not.

- [x] **No destructive SQL statement is ever issued without a `WHERE` clause** — enforced at the four
      connector funnels on the *rendered* clause, not on the condition collection (a non-empty collection can
      still render nothing). *(was: "`ParseConditionExpression` distinguishes no-filter from
      could-not-translate" — unsound, see the note above)*
- [x] SQL `Delete(filter)` / `Update(filter, PropertyUpdate)` / `Update(filter, Action)` **throw** on a null
      filter, sync and async, on all four providers
- [x] An untranslatable-but-non-null predicate (e.g. `x => pred(x)` via `InvocationExpression`) is **refused
      on the destructive paths** instead of deleting everything. The refusal does **not** need to name
      untranslatability as its cause — null, untranslatable and reduces-to-everything all get the same
      answer, differing only in message. *(was: "throws" with an implied per-cause distinction)*
- [x] A predicate that legitimately matches nothing deletes/updates **zero** rows and still emits a `WHERE`
- [x] `AbstractBulkStore` / `AbstractAsyncBulkStore` guard the null filter on `Delete` and both `Update`
      overloads (SH-M023), so the portable path fails the same way
- [x] **A deliberate all-rows delete/update remains possible on every layer** — `DeleteAll()` /
      `UpdateAll(updates)` (+ async) on the SQL stores and both portable bases, with `Delete(x => true)` kept
      working as a synonym. The emitted SQL is the clean `DELETE FROM "T"` — **no `1 = 1` is introduced
      anywhere**, since that pattern is indistinguishable from `' OR 1=1--` in a query log. *(new — the guard
      would otherwise remove a capability, which is how guards get reverted)*
- [x] Reads are unchanged — a null filter on `Read(filter, …)` still means read-everything, which is a
      documented API; no read path gains a `WHERE` it did not have
- [x] The all-rows naming asymmetry is recorded in `CLAUDE.md § Conventions` (read-all is the parameterless
      overload; destructive all-rows is the louder `*All` name) so it is not re-litigated from symmetry
- [x] Regression tests in `Birko.Data.SQL.Tests` (statement text) **and**
      `Birko.Data.SQL.SqLite.Tests` (end-to-end: rows survive), plus `Birko.Data.Stores` coverage for the
      portable guard
- [x] `/specs regen` for `bulk-filter-operations` and `store-crud-contract`, spec diffs reviewed

## Out of scope

- Widening what the parser *can* translate. This task makes an untranslatable filter loud, it does not make
  more shapes translatable. `SH-H021`/`SH-H026` (an untranslatable operand read as constant TRUE, and an
  unhandled node removing the whole `WHERE`) are the same family in the *read* path and are still
  unverified — separate tasks under `filter-expression-translation`.
- `SH-M024` (an unrecognised method call in an `UPDATE SET` value reflectively invoked with null args).
- Deferred to [[TASK-137]] — the pre-existing empty-`NOT IN` → `1 = 1` rendering, which is a false
  injection signal by the same argument that rejected `1 = 1` here. **Blocked on this task**: dropping an
  always-true term can leave a `DELETE` with no `WHERE`, which must reach this task's deliberate-all-rows
  path rather than its refusal.
- Deferred to [[TASK-138]] — `ReadAsync()` with no arguments does not compile (CS0121 between the read-all
  and filtered overloads). A read-path change, so excluded by criterion 6.
- Spawned to [[TASK-141]] at close (2026-08-06) — **MongoDB's four repeated guards have no test.** Found by
  the close-gate review, not by a failure: criterion 9 named `Birko.Data.SQL.Tests`,
  `Birko.Data.SQL.SqLite.Tests` and the portable base, so this is adjacent scope rather than an unmet
  criterion. ElasticSearch's 4 (CR-H047) and InMemory's 2 (`86df89c`) are covered; MongoDB's 4 rest on
  inspection alone, and the InMemory half of this very sweep was *discovered* by a failing test.
- **Converting the 10 public `Delete`/`Update` overrides to `protected *Core`** — the actually-correct fix,
  since the convention exists so the base can enforce invariants. Changes behaviour in three backends and
  needs its own decision; the contained repeat was chosen deliberately (see `## Outcome`). **Now filed as
  [[TASK-143]]** (2026-08-08).

## Implementation plan

*Drafted 2026-08-03, then grilled — the grill overturned the first draft's central mechanism. The
`## Resolved decisions` block at the end of this section is the authoritative record.*

### What the code actually looks like (verified 2026-08-03)

Four facts, each of which changed the plan:

1. **"Empty conditions" cannot be used to infer untranslatability — there are FOUR legitimate
   "means everything" paths, not one.** `ParseConditionExpression` (`SQL/DataBase.cs:314-818`) returns an
   empty set for: a constant-`true` body (`:330`); a parameter-free binary that evaluates true, e.g.
   `x => 1 == 1` (`:494`); an OR with a constant-true side, e.g. `x => true || x.A == 1` (`:448`, `:455`);
   and both-sides-constant OR-true (`:442`). It *also* returns empty as the **normal success path** for
   operand-level recursion, because many branches mutate the `parent` `Condition` in place and fall through
   to the same bottom `return` (`:754`, `:818`). So "non-trivial body + zero conditions ⇒ throw" would throw
   on predicates that work correctly today. **The first draft's step 1 was unsound.**
2. **Every destructive statement funnels through exactly one method per verb**, which is where the defect
   physically manifests: `AbstractConnector_Delete.cs:27` (private, 3 public overloads feed it),
   `AbstractConnector_Update.cs:117` (15 overloads feed it), and the async twins
   `AbstractAsyncConnector_Delete.cs:29` / `AbstractAsyncConnector_Update.cs:123`. **No provider overrides
   any of them**, so one edit covers all four providers, and `CachedAsyncDataBaseBulkStore.cs:160` forwards
   to `base.DeleteAsync`, so the caching decorator inherits the guard for free.
3. **The two halves share no base class.** SQL's stores are a parallel hierarchy (`DataBaseStore` →
   `DataBaseBulkStore`, implementing `IBulkStore<T>` directly) and do **not** derive from
   `AbstractBulkStore`. "One decision covers both layers" is one *policy*, two *edits*.
4. **The read-then-loop overloads never emit conditionless SQL.** `Update(filter, Action<T>)` reads, mutates
   in memory, then issues one `UPDATE … WHERE Guid = …` per row — every statement has a `WHERE`, so the
   connector guard is blind to it even though the damage ("mutated every row") is identical. The whole
   portable half is this shape. These need a null guard at the store boundary, not a translator change.

### Two guards, for two distinct failure modes

| Guard | Where | Catches |
|---|---|---|
| No destructive statement without a `WHERE` | the 4 connector funnels | null filter, untranslatable predicate — anything rendering no `WHERE` |
| A filter-based destructive overload requires a filter | store boundaries (SQL + both portable bases) | the read-then-loop paths that emit no conditionless SQL |

### "All rows" stays possible — expressed at the call site, not in the SQL

**`WHERE 1 = 1` was considered and rejected.** It is the signature of `' OR 1=1--`; emitting it in normal
query logs trains operators to ignore the pattern they should be alarmed by. So a deliberate all-rows
operation emits **clean SQL with no `WHERE`** — `DELETE FROM "T"` — and the *permission* to emit it comes
from how the call was made:

```
DELETE FROM "T" WHERE "Status" = @p0   ← normal
DELETE FROM "T"                        ← deliberate all-rows; reachable only 2 explicit ways
                                         (everything else throws before reaching the connector)
```

- **`DeleteAll()` / `UpdateAll(PropertyUpdate<T>)`** (+ async) — the named form; greppable in review.
- **`Delete(x => true)` keeps working** as its synonym. This is a **one-node** check, not a whitelist of the
  four shapes in fact 1: `ExpressionNormalizer` already funcletizes every parameter-free-true form
  (`x => 1 == 1`, a captured `flag`) down to a single `ConstantExpression(true)`, so the store boundary tests
  one node type after normalization. `x => true || x.A == 1` therefore throws — acceptable, and the message
  names `DeleteAll()`.
- **Naming is deliberately asymmetric.** Read-all is the existing parameterless overload (`Read()`
  `AbstractBulkStore.cs:43`, `ReadAsync(ct)` `AbstractAsyncBulkStore.cs:72`); destructive all-rows gets the
  *longer, louder* name. `Delete()` parameterless would sit one keystroke from `Delete(items)`. Record the
  convention in `CLAUDE.md § Conventions` so it is not "fixed" later by someone reasoning from symmetry.

### Steps

1. **`AddRequiredWhere` in `SQL/Connectors/AbstractConnectorBase.cs`**, beside `AddWhere` (`:378`). It calls
   `ConditionDefinition`, and **throws when the rendered SQL is empty** rather than when the collection is
   empty — `ConditionDefinition` returns `string.Empty` for a null *or* empty enumerable (`:318-322`) and
   builds from `BuildSingleCondition`, which can yield `""` for a malformed condition, so a non-empty
   collection can still render no `WHERE`. Guarding the rendered text closes both. Reads keep calling
   `AddWhere` untouched — that is why it is a second method, not a change to the existing one.
2. **Route the 4 funnels through it**, with an explicit opt-out parameter (`internal`/`protected`, default
   *off*) for the all-rows path: `AbstractConnector_Delete.cs:27`, `AbstractConnector_Update.cs:117`,
   `AbstractAsyncConnector_Delete.cs:29`, `AbstractAsyncConnector_Update.cs:123`.
3. **SQL store boundary** — `Stores/DataBaseBulkStore.cs` (`Update(filter, Action)` `:102`,
   `Update(filter, PropertyUpdate)` `:113`, `Delete(filter)` `:152`) and
   `Stores/AsyncDataBaseBulkStore.cs` (`:141`, `:155`, `:204`): null filter → throw; normalized body is
   `ConstantExpression(true)` → all-rows path; otherwise the normal path, where step 1 now enforces the
   `WHERE`.
4. **Portable bases** — `Birko.Data.Stores/AbstractBulkStore.cs` (`:68`, `:74`, `:97`) and
   `AbstractAsyncBulkStore.cs` (`:104`, `:113`, `:143`): same null guard, same constant-true mapping. Guard
   clauses per § Code Style.
5. **`DeleteAll()` / `UpdateAll(updates)` (+ async) on both hierarchies** — decided in the grill: the
   portable path must be able to express the operation, or the guard gets reverted later for taking a
   capability away. Lands on 8 inheriting backends (CosmosDB, ElasticSearch, InMemory, InfluxDB, JSON,
   MongoDB, RavenDB, XML).
   - **Open sub-decision for implementation time:** whether these go on `IBulkStore<T>`/`IAsyncBulkStore<T>`.
     Adding a plain member breaks any consumer implementing the interface directly. Recommendation: add them
     *with a default interface implementation* delegating to the constant-true predicate, so external
     implementors keep compiling and polymorphic callers still get the operation.
6. **Criterion 4 is probably already satisfied — verify before changing anything.** `_ => false` already
   renders `1 = 0` (`:334-339`, `MakeFalseCondition` `:927`). If it holds, this step is a test, not a change.
7. **Tests.** `Birko.Data.SQL.Tests` for statement text (no destructive statement reaches a command without a
   `WHERE`; `DeleteAll` emits the bare form; **no new `1 = 1` anywhere**); `Birko.Data.SQL.SqLite.Tests`
   end-to-end (rows *survive* a rejected delete — the consequence, not the SQL); portable-guard tests in
   `Birko.Data.InMemory.Tests` (the canonical test double; there is no `Birko.Data.Stores.Tests`). Include
   the `x => pred(x)` `InvocationExpression` case verbatim — it is the shape that proves criterion 3.
8. **Re-run the 8 backends' suites.** Step 4 changes their behaviour. Any existing test passing `null` to
   these overloads flips from "silently affects everything" to "throws": each is either asserting the defect
   (fix the test) or a real API expectation (a decision — surface it, don't relax the guard). Known callers
   of `Delete(x => true)` are 4 test sites (`StoreWrapperBuilderTests:317,382`,
   `InstrumentedBulkStoreWrapperTests:83,86,92`) — all decorator tests using it as a throwaway filter, and
   **no production caller anywhere in the family**.
9. **Prove the guards can fail** — revert-and-split per [[populate-tests]]; report the numbers and name which
   tests are fix-dependent versus contract pins. Expect real pins: reads and `_ => false` must not move.
10. **`/specs regen`** for `bulk-filter-operations` and `store-crud-contract`. Expect the TASK-113 shape —
    these specs likely document the conditionless statement as shipped behaviour, so requirement *titles*
    may change with the code.

### Risks

- **The blast radius is the verification, not the change.** Nine store families inherit step 4–5; the code is
  a handful of guard clauses plus two methods. Budget step 8.
- **Deliberately NOT widening the parser** (per `## Out of scope`). Every shape that works today keeps
  working; the only behaviour change is that a shape which *silently did the wrong thing* now throws.
- **Reads are untouched** (criterion 6) — no new `WHERE` on read paths, no `ParseConditionExpression`
  signature change, so the ~15 read call sites and `SqlExpressionParityTests`' `constTrue` case are unaffected.

### Split assessment

**Keep as one task.** The two halves are one policy; landing them separately leaves a window where the
portable and SQL layers disagree about what a null filter means. If step 8 turns up widespread breakage
across the 8 backends, *that* is the spawn candidate.

### Spawned while planning (both filed 2026-08-03)

- **[[TASK-137]]** — empty `NOT IN` already renders `1 = 1` (`InConditionStrategy.cs:33`, shipped
  2026-07-27), a false injection signal by the same argument that rejected `1 = 1` here. Filed under
  STORY-051, `depends-on: [TASK-109]` — its fix (drop the always-true term) can leave a `DELETE` with no
  `WHERE`, which must land on **this** task's deliberate-all-rows path, not its refusal path. Step 7's
  "no new `1 = 1`" assertion is scoped to newly-emitted SQL; the pre-existing one is TASK-137's to remove.
- **[[TASK-138]]** — `ReadAsync()` with no arguments does not compile (CS0121 between the read-all and
  filtered overloads); worked around six times in TASK-113's suite. Filed to `_loose` (an API-ergonomics
  papercut, not a remediation finding, so it carries no `feature:` link). Read-path change, excluded here
  by criterion 6. Its file records that the `ReadAll()` **symmetry** argument was rejected and that the
  ambiguity is the only justification, so the closed decision is not silently reopened.

### Resolved decisions

- Guard placement → **two guards**: required-`WHERE` at the 4 connector funnels + null guard at the store
  boundaries (a parser-level classification is unsound — four legitimate empty-result paths exist)
- Untranslatable-vs-null distinction → **not classified**; both refuse at the same place, differing only in
  the message (the decision is identical, so machinery to tell them apart buys nothing)
- Guard predicate → **on the rendered `WHERE` string**, not on `conditions.Any()` (a non-empty collection can
  still render nothing)
- All-rows idiom → **clean `DELETE FROM "T"` with no `WHERE`**, authorised by the call site
- `WHERE 1 = 1` → **rejected** — indistinguishable from `' OR 1=1--` in a log; trains operators to ignore a
  real attack signature
- All-rows API → **`DeleteAll()` / `UpdateAll(updates)`** (+ async), on the SQL stores **and** both portable
  bases (8 backends), so no layer loses the capability
- `Delete(x => true)` → **keeps working** as a synonym; one-node check after `ExpressionNormalizer`, so no
  shape whitelist
- `x => true || x.A == 1` → **throws**, message naming `DeleteAll()`
- Naming symmetry → **deliberately asymmetric**: read-all is the existing parameterless `Read()`/`ReadAsync(ct)`,
  destructive all-rows gets the louder `*All` name; recorded in `CLAUDE.md § Conventions`
- `ReadAll()` alias → **not added** (would be a second name for an existing behaviour)
- Interface membership for `DeleteAll`/`UpdateAll` → **deferred to implementation**: recommend a default
  interface implementation so external implementors don't break
- Criterion 4 (`_ => false` still emits a `WHERE`) → **verify first**; likely already true, so a test not a change
- Task split → **stays one task**

## Outcome

**What the fix is.** A SQL `DELETE` or `UPDATE` that would carry no `WHERE` clause is now refused with
`WholeTableWriteException` instead of silently rewriting the whole table. The three inputs that used to
collapse into that statement — a null filter, a predicate the parser has no branch for, and a predicate
that reduces to "everything" without being an explicit constant — all reach the refusal. Deliberate
whole-table writes stay possible through `DeleteAll()` / `UpdateAll(updates)` (+ async) on the SQL stores
and both portable bases, with `Delete(x => true)` kept working as a synonym; the SQL they emit is clean
(`DELETE FROM "T"`, no `1 = 1`). Reads are untouched — a null filter on a read still means read-everything.

**Where it lives.** Two guards for two failure modes, because one could not cover both: the four connector
funnels (every public overload of each verb feeds exactly one method, and no provider overrides them, so
one edit covers all four SQL providers), plus a filter-required check at the store boundaries for the
read-then-loop overloads, which issue one `WHERE Guid = …` per row and so are invisible to a
statement-level guard even though "mutated every row" is the identical damage.

**Step 6 — prove the guard can fail: 12 of 40 failed** with the guard call sites removed but the API
surface kept, so the split isolates the guard rather than the scaffolding.

- Fix-dependent (12): `Birko.Data.SQL.SqLite.Tests` **6** end-to-end, `Birko.Data.InMemory.Tests` **6**.
- **`Birko.Data.SQL.Tests` failed 0 — and that is the informative half.** Those 14 unit tests exercise
  `AddRequiredWhere` directly, so they pin the *helper's* behaviour and **not** that the funnels call it.
  They are **contract pins, not evidence**; the wiring evidence is the SqLite end-to-end suite alone.
  Recorded in that test file's own header so a future reader cannot mistake one for the other.
- Re-verified at close: 362 + 113 + 53 = **528 green** across the three suites.

**Judgement calls, and the stricter option rejected in each.**

- **`WHERE 1 = 1` as the all-rows marker — rejected.** It is the signature of `' OR 1=1--`; emitting it in
  normal query logs trains operators to ignore the pattern that should alarm them. Permission to write
  every row comes from *how the call was made*, not from a marker in the SQL. (This is also what spawned
  [[TASK-137]] against the pre-existing empty-`NOT IN` → `1 = 1`.)
- **`IsExplicitAllRows` as a one-node test, not a whitelist of always-true shapes — deliberate, and the
  narrower behaviour is the stricter one.** `x => true || x.A == 1` reduces to "everything" in the parser
  and is *refused*. Enumerating the parser's reduce-to-everything sites was tried and rejected: a
  whitelist rots the moment a fifth site is added, and its failure mode is a refused destructive operation
  on working code.
- **Refuse before the transaction wrapper, not inside it.** `DoCommandWithTransaction` funnels callback
  exceptions through `InitException`, which re-wraps them in a bare `Exception` — so the tidier
  refuse-at-render-time-only design would surface a request-shaped problem as something no
  `catch (WholeTableWriteException)` could select. `AddRequiredWhere` stays as the backstop for the exotic
  case (a non-empty collection that renders nothing), so both are present on purpose.
- **`ArgumentNullException` for the null filter, not `WholeTableWriteException`.** The caller passed null
  for a non-nullable parameter; that is an argument error, and the message names the `*All` door. The
  guard runs *before* the `Connector == null` early return, so a store with no connector still refuses
  rather than silently doing nothing.
- **Repeating the guard in overriding backends rather than converting them to `*Core` — contained fix
  chosen over the correct one.** 10 stores override the *public* `Delete`/`Update` and so bypass the base
  guard entirely. ElasticSearch's 4 were already covered by `ParseRequiredFilterQuery` (CR-H047); InMemory's
  2 and MongoDB's 4 now repeat the check. These overrides stand against the family convention (override
  `protected *Core`, not the public CRUD methods, *precisely* so the base can enforce invariants) — but
  converting them is a behavioural change to six stores and is not this task's business.

**Flagged, not fixed.**

- **The spec map under-covered the fix's primary site.** The four destructive funnels,
  `WholeTableWriteException` and both MongoDB stores were reachable by **no** glob in **any** area, so a
  regen could not have seen this behaviour change at all. Added to `bulk-filter-operations`' globs with the
  reason recorded in `.map.yml`. This is the second instance of the same silent under-coverage (the first
  was TASK-110, noted in the same file) — the pattern is worth a systematic sweep, which needs a decision
  about how the map is audited rather than another glob. **Now filed as [[TASK-142]]** (2026-08-08).
- **The `shaped-by` evidence pass cannot run from this aggregator at all** — every source glob points into
  a sibling repo, so no `pr:` sha resolves under `git show` here (verified: `d8c2f40` is "unknown
  revision"). Both regenerated specs are stamped `shaped-by-derived: false` *with the reason*, so the
  false is not read as "nobody tried". True of every area in this repo's spec tree.
- **The two "no connector means the write is silently dropped" behaviours are unchanged** and still
  specced. A null `Connector` swallowing a destructive call is its own smell, but it predates this task and
  narrowing it would change behaviour no finding asked about.

**A process failure worth carrying.** Mid-verification, `git checkout -- .` across the four production
repos to undo the step-6 experiment reverted *every* tracked change, not just the guard-stripping — about
an hour of work. Recovery was mechanical **only because the test files live in different repos and
survived**: the 40 tests were the executable specification, and the rebuild was verified complete by them
going green at identical counts. Commit before a revert experiment; prefer targeted `git restore <paths>`
or a stash over `checkout -- .`. The polyrepo split, usually an overhead, is what made this recoverable.

## Progress log

- 2026-08-03 — **plan drafted, then grilled.** The grill overturned the draft's central mechanism; see
  `## Implementation plan › Resolved decisions`. Criteria 1/3/5 corrected before any code, two criteria added.
- 2026-08-03 — **step 6 (verify criterion 4 first) — already held.** `x => false` renders an always-false
  `WHERE`; empty `IN` → `1 = 0` was already pinned at `InConditionStrategyTests.cs:121,136`. So it is a
  contract pin, not a change, exactly as the plan predicted.
- 2026-08-03 — **steps 1–5 implemented and committed.** Production: `Birko.Data.SQL` **d8c2f40** (the
  4-funnel `WHERE` guard, `WholeTableWriteException`, `IsExplicitAllRows`, `DeleteAll`/`UpdateAll` on both SQL
  stores), `Birko.Data.Stores` **3cd8b2a** (portable null guard + `*All` doors),
  `Birko.Data.InMemory` **4f680b7** and `Birko.Data.MongoDB` **88f96ee** (the override sweep below). Tests:
  `Birko.Data.SQL.Tests` **349c7b3**, `Birko.Data.SQL.SqLite.Tests` **35fc122**,
  `Birko.Data.InMemory.Tests` **86df89c** — 40 new tests.
- 2026-08-03 — **step 8 (the 9-backend sweep): 16 suites, 1068 tests, 0 failures.** The feared breakage did
  not materialise — no existing test relied on `Delete(null)`, and the 4 existing `Delete(x => true)` call
  sites keep working because that idiom was deliberately kept as a synonym.
- 2026-08-03 — **the sweep found a real gap, measured not guessed.** 6 of the new portable tests failed
  against a *correct* base class: `AbstractInMemoryStore` overrides the **public** `Delete(filter)` and so
  bypassed the guard. Measured across the family: **10 such overrides in 3 backends** — ElasticSearch's 4
  were already covered by `ParseRequiredFilterQuery` (CR-H047), leaving InMemory's 2 and MongoDB's 4, both now
  guarded. This is spawn.md's *"wider population, same fix"* shape, so it was swept rather than asked about.
  Note the convention it sits against: `CLAUDE.md` says stores override `protected *Core`, **not** the public
  CRUD methods, precisely so the base can enforce invariants — these overrides predate the guard and violate
  that. Repeating the guard is the contained fix; converting them to `*Core` is not this task's business.
- 2026-08-03 — **step 9 (prove the guards can fail): 12 of 40 failed** with the guard *call sites* removed but
  the API surface kept (so the split isolates the guard, not the scaffolding). SqLite end-to-end **6**,
  InMemory **6**. **`Birko.Data.SQL.Tests` failed 0** — and that is the informative half: those 14 unit tests
  exercise `AddRequiredWhere` directly, so they pin the helper's behaviour and **not** that the funnels call
  it. The wiring evidence is the SqLite end-to-end suite alone. Recorded in that file's own header so a
  future reader does not mistake it for proof.
- 2026-08-03 — **⚠ I destroyed the implementation mid-verification and rebuilt it.** Restoring after the
  step-9 experiment, `git checkout -- .` across the four production repos reverted *every* tracked change,
  not just the guard-stripping — about an hour of work. Recovery was mechanical only because the **test files
  live in different repos** and survived: the 40 tests are the executable specification, and the rebuild was
  verified complete by them going green again at identical counts. Two lessons, both structural: commit
  before a revert experiment, and prefer targeted `git restore <paths>` or a stash over `checkout -- .`.
- 2026-08-06 — **resumed after a session reset.** Reconciled the log against git: all 7 commits are HEAD in
  their repos and every tree is clean, so the rebuild did land. Re-ran the three suites: **528 green**
  (362 + 113 + 53). Steps 3–6 confirmed complete; 7 and 8 were outstanding.
- 2026-08-06 — **criterion 8 was NOT met and is now closed.** The `*All` naming asymmetry existed only in
  `Birko.Data.Stores@3cd8b2a`'s commit body, never in `CLAUDE.md § Conventions` where the criterion put it.
  Added there with the full rule (why `Delete()` must never be parameterless, the two guards, the
  `x => true` synonym, and no `1 = 1`). Every other criterion verified against the code before ticking.
- 2026-08-06 — **step 7 — respecced `bulk-filter-operations` + `store-crud-contract`.** Requirements
  changed: *"A filter that translates to no conditions produces an unfiltered statement"* → **"A
  destructive statement that would carry no WHERE is refused, not issued"** (a requirement whose own
  **title** asserted the defect); added *"Every-row destructive writes are reachable only through an
  explicit door"* and *"A backend overriding the public destructive methods repeats the guard"*; retitled
  and extended the in-memory delete requirement; tightened the two "no connector" scenarios, which the
  guard's ordering made imprecise rather than wrong. Diff reviewed: every change traces to an acceptance
  row, **no unexplained behavioural change, so no findings spawned**. Map gap found and fixed first — the
  guard's primary site was reachable by no glob (see `## Outcome › Flagged`).
- 2026-08-06 — **step 8 — `## Outcome` written** (fix, step-6 split with names, five judgement calls with
  the rejected stricter option, three flags, and the `checkout -- .` process failure), then handed to
  `/tasks close`.

## Human test plan

N/A — covered by automated tests. The SQLite-backed end-to-end assertions cover the real consequence
(rows still present after a rejected delete), which is the only part a human could otherwise check.
