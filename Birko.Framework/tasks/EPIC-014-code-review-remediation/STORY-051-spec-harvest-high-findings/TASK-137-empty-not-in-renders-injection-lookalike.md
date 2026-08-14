---
id: TASK-137
parent: STORY-051
feature: FEATURE-014
status: done
priority: P2
assignee: ai
picked-by: fix-next
created: 2026-08-03
depends-on: [TASK-109]
blocks: []
related: [TASK-109, TASK-208, TASK-212, TASK-213]
pr: f41b3a4
github-issue: null
jira-key: null
findings: []
---

# An empty `NOT IN` renders `1 = 1` — indistinguishable from `' OR 1=1--` in a query log

## Context

Found while planning [[TASK-109]] (2026-08-03), which proposed rendering every "matches everything"
predicate as `WHERE 1 = 1` and had that design **rejected on this exact ground**. The rejection then applies
to what already ships.

`../Birko.Data.SQL/SQL/Connectors/Strategies/InConditionStrategy.cs:33` renders an empty value set as a
constant with the same set semantics:

```csharp
if (IsEmpty(condition.Values))
    return condition.IsNot ? "1 = 1" : "1 = 0";
```

Both are **semantically correct** and the reasoning for them is sound and written out in the file: `Col IN ()`
is a syntax error on PostgreSQL and MSSQL, `1 = 0` / `1 = 1` are valid on every supported dialect, need no
parameters, and compose inside AND/OR chains exactly as a real `IN` would. Shipped 2026-07-27; this task does
not dispute any of that.

The problem is **operational, not semantic**: `1 = 1` is the signature of the most recognisable SQL-injection
payload (`' OR 1=1--`). Emitting it during normal operation puts a false positive into query logs, alert
rules and audit trails — and the cost is not the noise, it is that operators learn to scroll past the pattern
they are supposed to react to. A defence that trains people to ignore it is worse than no signal.

Reachable today whenever a caller passes an empty collection to a negated `Contains` —
`ids.NotContains(x.Field)` shapes, i.e. `!ids.Contains(x.Field)` with `ids` empty, which the empty-`IN` work
of 2026-07-27 deliberately made a *legitimate* translation rather than an error.

### RESCOPED 2026-08-14 (step 3) — this is a destructive-guard **bypass**, not only a log-hygiene defect

The mechanism above is confirmed exactly as filed (`InConditionStrategy.cs:33` emits `1 = 1`). What the
finding did not know is what that constant *does* one layer up. Measured end-to-end against real on-disk
SQLite, seeded with 3 rows, `ids` empty:

| call | today | expected |
|---|---|---|
| `store.DeleteAsync(x => !ids.Contains(x.Amount))` | **no exception; 0 of 3 rows remain** | `WholeTableWriteException` |
| `store.UpdateAsync(x => !ids.Contains(x.Amount), Set(Name,"WIPED"))` | **no exception; 3 of 3 rewritten** | `WholeTableWriteException` |

`AddRequiredWhere`'s entire contract (SH-H002 / [[TASK-109]]) is *"nothing rendered → refuse"*. `1 = 1`
manufactures a non-empty `WHERE` that constrains nothing, so **the guard is satisfied by a tautology** and an
ordinary filter empties the table silently, reporting success. That is § Conventions' "any write whose scope
can silently become everything" — the third instance in the family after SH-H002 (SQL) and SH-H006 (Redis).

So the severity is not operational. `1 = 1` in the log is the *symptom*; the defect is that the pattern
whose whole purpose is to look always-true **is** always-true, to the guard as much as to the database.

**Acceptance criterion 2 was inverted, and is corrected below.** As filed it required the sole-condition
destructive case to reach TASK-109's deliberate-all-rows path — which would preserve the bypass exactly.
Its stated reason ("otherwise `!emptyIds.Contains(x.Field)` on a delete starts throwing, which would be a
regression, not a fix") rests on a false premise: today it does **not** throw, it wipes the table. Starting
to throw *is* the fix. And TASK-109's own rule already settles the direction — `DataBase.cs:319-327` says
only a **one-node** normalized constant is the explicit door and anything else that merely *reduces* to
everything is refused with a message naming the explicit API, which is why `x => true || x.Count > 5` is
refused today (`DestructiveFilterGuardTests:215`). An empty `NOT IN` is that same shape and gets that same
answer.

**Where the refusal has to fire.** `AddRequiredWhere` runs inside `DoCommandWithTransaction`, whose
`InitException` re-wraps every callback exception in a bare `Exception` that no
`catch (WholeTableWriteException)` can select — the documented reason SH-H002 added the pre-check. So the
reduction must be visible to `WouldTargetEveryRow`, before the transaction opens, not only at render time.

### Measured facts the fix is built on (all from the same reproduction)

- The expression parser always yields **one** top-level condition; a flat multi-element list is only
  reachable from hand-built conditions. Operator precedence is expressed by **nesting**, and every group's
  children share that group's separator — so there is no mixed unparenthesized chain to reduce.
- An empty `NOT IN` arrives as a **leaf**: `Type=In`, `IsNot=true`, `Values=[]`.
- **Negated groups are reachable and must flip.** `!(x.Amount > 20 || !ids.Contains(x.Amount))` parses to a
  group with `IsNot=true, IsOr=true` and correctly returns **0** rows today: `NOT (A OR TRUE)` is always
  false. Reducing the group to always-true and then dropping it would return 3. The flip renders the existing
  always-false constant `1 = 0`.
- Read semantics that must not move (seeded 10/20/30, `ids` empty): bare `!empty` → 3;
  `>10 && !empty` → 2; `>20 || !empty` → 3; `!(>20 || !empty)` → 0; `!(>20 && !empty)` → 2;
  `>20 || name=="r1" && !empty` → 2; `>10 && name!=null && !empty` → 2; empty `IN` → 0.

## Approach

The semantics to preserve: **empty `NOT IN` matches every row.** Three candidate renderings, and the third is
the one worth pursuing:

1. **`WHERE TRUE`** — ruled out already, and for the same reason `1 = 1` was chosen: valid on
   PostgreSQL/MySQL/SQLite, a **syntax error in T-SQL**. Portability is why the constant idiom exists.
2. **A different always-true constant** (`0 = 0`, `'a' = 'a'`) — cosmetic reshuffling. Any always-true
   comparison reads like a tautology probe; this trades one lookalike for a less familiar one.
3. **Drop the term instead of rendering it.** `A AND TRUE ≡ A`, so an always-true condition inside an AND
   chain can be *removed* rather than emitted, and a chain that reduces to nothing means no `WHERE` at all.
   This produces the cleanest SQL — the query says what it means — and introduces no constant.

**Option 3 interacts directly with [[TASK-109]] and must land after it.** "The chain reduced to nothing, so
emit no `WHERE`" is precisely the shape TASK-109 refuses on destructive paths. The two must agree: a
*reduced-away* always-true term on a `DELETE` has to reach TASK-109's deliberate-all-rows path (the same one
`x => true` maps to), not its refusal path — otherwise `!emptyIds.Contains(x.Field)` on a delete starts
throwing, which would be a regression, not a fix.

> **⚠ The paragraph immediately above is WRONG and was not followed — see § RESCOPED.** Option 3 was the right
> choice, but its destructive-path half is inverted: a reduced-away always-true term must reach TASK-109's
> **refusal**, not its all-rows path. The premise ("otherwise … starts throwing, which would be a regression")
> is false — measured, the unfixed code does not throw, it deletes every row and reports success, so throwing
> is the fix rather than a regression. Left in place rather than rewritten, because what the approach got
> wrong is the lesson: the shipped behaviour has to be measured before a remedy is costed against it.

Note the asymmetry: `1 = 0` (empty `IN`, matches nothing) is **not** a problem and should stay. It has no
injection connotation, and dropping an always-false term is not sound anyway — `A AND FALSE` is `FALSE`, not
`A`. Only the always-true side is in scope.

## Acceptance criteria

- [x] An empty `NOT IN` no longer emits `1 = 1`; the term is dropped from its AND chain and the emitted SQL
      is what the remaining conditions say
- [x] **(corrected — see § RESCOPED)** An empty `NOT IN` as the **sole** condition emits no `WHERE` on reads,
      and on `DELETE` / `UPDATE` is **refused** with `WholeTableWriteException` naming `DeleteAll()` /
      `UpdateAll()` — the same answer `x => true || x.Count > 5` already gets. It must **not** reach
      [[TASK-109]]'s deliberate-all-rows path: that path is for a one-node explicit constant, and routing a
      reduced-away tautology into it is the bypass this task closes
- [x] The refusal fires in the **pre-check** (`WouldTargetEveryRow`), before `DoCommandWithTransaction` opens
      a transaction, so the caller receives `WholeTableWriteException` and not the bare `Exception` that
      `InitException` would re-wrap it in
- [x] `DeleteAll()` / `UpdateAll()` / an explicit `x => true` are **unaffected** — the opt-out is checked
      first, so the new refusal is a guard with a door and not a wall (§ SH-H037)
- [x] The "means everything" verdict has **one producer**, shared by the pre-check and the renderer — not a
      structural predicate in one place and a rendering rule in the other (§ Conventions, "one producer")
- [x] Set semantics are unchanged and asserted against the compiled-delegate oracle: empty `NOT IN` matches
      every row, empty `IN` matches none
- [x] `1 = 0` for an empty `IN` is **unchanged** — an always-false term cannot be dropped, and it carries no
      injection connotation
- [x] Always-true terms inside `OR` chains are handled correctly too — `A OR TRUE` is `TRUE`, so the chain
      collapses rather than dropping the term (dropping it would silently narrow the result)
- [x] A **negated** group that reduces to always-true renders always-**false** (`NOT (A OR TRUE)` is `FALSE`),
      via the existing `1 = 0` constant — measured reachable, and returns 0 rows today
- [x] `grep -rn "1 = 1"` over the emitted-SQL paths returns nothing outside tests
- [x] Regression tests in `Birko.Data.SQL.Tests` (statement text) and `Birko.Data.SQL.SqLite.Tests`
      (end-to-end row sets), including a `DELETE` with an empty `NOT IN` — the case where this task and
      TASK-109 meet
- [x] `/specs regen` for `filter-expression-translation` and `bulk-filter-operations`, spec diffs reviewed

## Out of scope

- The empty-`IN` → `1 = 0` rendering (correct, and keeping it is a criterion above).
- Widening what the parser can translate — [[TASK-109]] `## Out of scope` applies here unchanged.
- Auditing consumers' log-alerting rules. This task removes the false signal at the source; what anyone
  greps for is their own configuration.

## Human test plan

N/A — covered by automated tests. Statement text is assertable directly and the SQLite-backed suites cover
the row-set consequence.

## Outcome

An empty `NOT IN` (`x => !ids.Contains(x.Col)` with `ids` empty) used to render `WHERE 1 = 1`. Because that is
a **non-empty** `WHERE` that constrains nothing, it satisfied the whole-table write guard TASK-109 had just
installed — so an ordinary filter reached a whole-table `DELETE`/`UPDATE` and reported success. Measured
against real SQLite on a 3-row table: the delete left **0 of 3** rows and threw nothing; the update rewrote
**3 of 3**. An always-true term is now **reduced away** instead of rendered: `A AND TRUE` is `A`, an
always-true `OR` chain collapses to no `WHERE` at all, and `WouldTargetEveryRow` shares that same reduction —
so the destructive paths refuse with `WholeTableWriteException` before a transaction opens, exactly as
`x => true || x.A == 1` already did. No `1 = 1` is emitted anywhere in the framework's SQL.

### Step-6 split — 29 of 54 (surgical reintroduction)

Not a full revert: most new tests name `IsAlwaysTrueCondition` / `AlwaysFalseSql`, so a plain revert would
have hidden them behind a build error and reported confidence for tests that never ran (the TASK-204 trap).
The defect was reintroduced at two points with every signature intact — the leaf verdict forced to `false`,
and the strategy returning `1 = 1` again.

**Re-derived after the suite grew.** The first measurement was 29 of 45, taken before the 9
`SqlExpressionParityTests` cases were added for the compiled-delegate-oracle criterion. All 9 pass either
way, so the failure count is unchanged and the denominator is not — carrying the old ratio forward would
have overstated the check by 9 tests. (The TASK-111 rule: a split expires the moment the suite changes.)

| project | new-or-changed | failed | pins |
|---|---|---|---|
| `Birko.Data.SQL.Tests` | 27 | **24** | 3 |
| `Birko.Data.SQL.SqLite.Tests` — `EmptyNotInEndToEndTests` | 18 | **5** | 13 |
| `Birko.Data.SQL.SqLite.Tests` — `SqlExpressionParityTests` | 9 | 0 | 9 |

**Fix-dependent (29).** All 21 rendering/guard cases in `EmptyNotInReductionTests` except the three pins
below; the 3 changed cases in `InConditionStrategyTests`
(`BuildSql_EmptyValues_WithIsNot_Throws_…`, `BuildSql_NullValues_WithIsNot_Throws_…`,
`BuildSql_NeverEmitsTheInjectionLookalike_ForAnyEmptySetShape`); and 5 in `EmptyNotInEndToEndTests`
(`Delete_WithASoleEmptyNotIn_IsRefusedAndLeavesEveryRow`,
`Update_WithASoleEmptyNotIn_IsRefusedAndRewritesNothing`, `Delete_WithACollapsedOrChain_IsAlsoRefused`,
`TheRefusalIsWholeTableWriteException_NotTheBareException…`, `TheParserProducesTheEmptyNotInLeafShape`).

**Contract pins — recorded as pins, NOT as evidence (25).**
`AnEmptyIn_StillRendersTheAlwaysFalseConstant`, `ANonEmptyNotIn_IsUnaffected`,
`IsAlwaysTrueCondition_AndTheRenderer_CannotDisagree`; and in the end-to-end suite
`DeleteAll_StillEmptiesTheTable`, `AnExplicitTruePredicate_StillDeletesEveryRow`,
`ABoundedDeleteBesideAnAlwaysTrueTerm_StillDeletesItsRows`,
`ADeleteThatMatchesNothing_StillDeletesNothingRatherThanBeingRefused`,
`Read_WithANonEmptyNotIn_IsUnaffected`, and the **8** `Read_ReturnsTheSameRowsAsBeforeTheReduction` theory
cases.

Plus the **9** `SqlExpressionParityTests` cases (`emptyNotIn`, `emptyIn`, `emptyNotInAnd`, `emptyNotInOr`,
`emptyNotInNotOr`, `emptyNotInNotAnd`, `emptyNotInNested`, `emptyNotInTwice`, `emptyBothKinds`).

Three of those pins deserve naming, because two are designed pins and one is a limitation:

- **The 9 parity cases are the strongest thing here and still cannot witness the fix.** They compare SQL
  results against `expr.Compile()` — real C# semantics — over a real database, which is what the
  "compiled-delegate oracle" acceptance criterion asked for, and the reduction rewrites the emitted SQL for
  every one of them. They pass either way because the old rendering was *semantically* correct: `1 = 1` really
  is always-true. **That is the precise shape of this defect** — reads were never wrong, so no read-side
  assertion can detect it; only the destructive guard could, and it was the thing being fooled.

- **The 8 read cases are pins by design.** Their whole purpose is that the fix must NOT move them: every one
  of the eight values (3, 2, 3, 0, 2, 2, 2, 0) was measured against the *unfixed* code first, and the
  reduction rewrites the SQL for all of them. They are the oracle, not the proof.
- **`IsAlwaysTrueCondition_AndTheRenderer_CannotDisagree` passes either way, and that is honest rather than
  useless.** It asserts an internal-consistency property, and the pre-fix code was internally consistent too
  — the verdict said "not always-true" and the renderer emitted `1 = 1`. That agreement is precisely why the
  bypass was invisible. The test guards a future divergence; it cannot witness this fix.

### Judgement calls, and the stricter option rejected

- **The task's acceptance criterion 2 was inverted, and following it would have preserved the bypass.** It
  required the sole-condition destructive case to reach TASK-109's *deliberate-all-rows* path, arguing that
  refusing "would be a regression, not a fix". The premise was false — today it does not throw, it wipes the
  table — so the criterion was corrected before any code was written (§ RESCOPED). TASK-109's own rule
  settles the direction: only a **one-node** normalized constant is the explicit door, which is why
  `x => true || x.Count > 5` is already refused. **Fifth-plus instance in this epic of a prescribed remedy
  needing re-costing** (TASK-111, TASK-112, TASK-117, TASK-129, TASK-207).
- **The refusal fires in the pre-check, not only at render time — and that placement is the fix, not a
  detail.** `AddRequiredWhere` runs inside `DoCommandWithTransaction`, whose `InitException` re-wraps every
  callback exception in a bare `Exception` that no `catch (WholeTableWriteException)` can select. Guarding
  only the rendered clause would have turned a silent whole-table delete into an unhandled 500. Pinned by
  `TheRefusalIsWholeTableWriteException_NotTheBareException…`.
- **One producer of the "means everything" verdict, rejecting a structural pre-check plus a separate
  rendering rule.** Two implementations is how a guard ends up agreeing with itself and disagreeing with the
  emitted SQL — which is the shape of the defect being fixed. `WouldTargetEveryRow` and both
  `ConditionDefinition` overloads call the same `IsAlwaysTrueCondition` / `IsAlwaysTrueChain`.
- **The strategy throws rather than returning a sentinel or an empty string.** Rejected: a sentinel launders
  an unrenderable term as SQL, and `""` would be joined between its neighbours' separators into
  `A AND  AND B` — the silent version of the same bug. The production path provably never reaches the throw
  (the assembler skips such terms first), and no reachable always-true constant survives anywhere.
- **A negated group flips to always-FALSE and is RENDERED, not dropped.** Measured reachable off the parser
  (`!(A || !empty.Contains(x))` → `IsNot=true, IsOr=true`, correctly 0 rows today). Dropping it would have
  inverted the filter from "matches nothing" to "matches every row" — a wrong answer, strictly worse than the
  constant being removed. Reuses the existing `1 = 0`, so the empty-`IN` contract is untouched.
- **A bug found in my own fix, before it shipped.** The flat multi-condition list drops an always-true term,
  and dropping one that *opened* an OR run would have rendered `A OR TRUE AND B` as `A AND B` — the
  intersection instead of the union, a silent narrowing introduced by the fix. The dropped term now hands its
  `OR` to the next survivor (`FlatList_ADroppedOrJoinedTermHandsItsOrToTheNextSurvivor`). Found by reasoning
  through the reduction, not by a failing test — the flat list is only reachable from hand-built conditions,
  since the parser always yields a single nested root.
- **Blast radius measured, not assumed.** The change touches the core condition renderer and the destructive
  guard, so it was cleared against **23** SQL-touching suites before being called done.

### Can the new guard be walked past? — enumerated, with one honest residue

Asked explicitly, because a scope guard's real test is whether a caller can widen the scope back (the
TASK-117 lesson), not whether it fires on the reported input. Every route to a conditionless or
scope-less destructive statement:

| route | outcome |
|---|---|
| `DeleteAll()` / `UpdateAll(updates)` | allowed — the named door, unchanged, executed by a test |
| `x => true` (incl. `1 == 1`, a captured true flag) | allowed — the one-node explicit synonym, unchanged |
| null filter | `ArgumentNullException` at the store boundary (unchanged) |
| empty condition collection | refused by `WouldTargetEveryRow` (unchanged) |
| **non-empty collection that reduces to always-true** | **now refused** — this task |
| a condition that renders empty for another reason (malformed) | refused by the `AddRequiredWhere` backstop |
| a term whose parameters were already bound before a collapse | cannot happen — every reduction is decided **before** `BuildSingleCondition` runs, so no orphaned `DbParameter` is ever added (checked at all four sites) |

**The residue, stated rather than papered over:** a *hand-built* `Condition` can still express a tautology
the reduction does not model — e.g. `Condition.CreateValue("1", 1, Equal)` renders `1 = @p` with `@p = 1`,
which is non-empty, is not an empty `NOT IN`, and so passes both the pre-check and the backstop. This is
**not reachable from a filter expression**: `x => 1 == 1` normalizes to a single `ConstantExpression(true)`
and takes either the refusal or the explicit-door path. It requires a caller to deliberately construct a
tautology by hand, which is indistinguishable in intent from calling `DeleteAll()`. Left alone deliberately —
modelling arbitrary tautologies is a theorem-prover's job, and the parser's own always-**false** encoding
(`Name="1"`, `Values=[0]`) uses the same trick, so narrowing it would need that path redesigned first. Worth
knowing before anyone records this guard as total.

### Flagged and not fixed

- **[[TASK-212]] (spawned, P1) — MongoDB has the same two doors and half the guard.**
  `MongoDBStore.Delete(filter)` / `Update(filter, …)` and the async twins call `RequireFilter`, which refuses
  only a **null** filter, then hand the predicate straight to `DeleteMany`. The shape this task spent its
  scope on — a non-null filter that matches everything — is unguarded there, and
  `WholeTableWriteException` is not referenced anywhere in `Birko.Data.MongoDB`. Filed with the mechanism
  marked **unverified**, because the translation belongs to the MongoDB driver and nothing in this repo
  settles it; the task's first acceptance criterion is to measure it and its last is that a refutation is a
  valid close. ElasticSearch renders the same predicate as `must_not MatchNone` (match-everything) and is
  named as a third instance in TASK-212's § Out of scope rather than folded in.
- **[[TASK-213]] (spawned, P1) — a COMPUTED operand inside `Contains` is silently rewritten into a different
  predicate.** Found by adding this task's shapes to the compiled-delegate oracle suite: the added
  `emptyNotInTwice` case failed, and the cause turned out to be pre-existing and unrelated. `x.Amount + 1` /
  `x.Score ?? 0` as the `Contains` operand is **discarded** and replaced by a fabricated subcondition, so
  `SomeIds.Contains(x.Amount + 1)` over a **non-empty** set answers 1 row where the truth is 0 — a wrong
  answer in the positive direction, with nothing to do with emptiness or with this fix (verified: the same
  rows answer identically before and after it, because the reduction inspects `SubConditions` first and so
  never fires on the malformed shape). **The parity case was rewritten over a plain column rather than
  asserted**: encoding the broken behaviour to keep the suite green would have blessed it (the TASK-111
  precedent), and the comment in the test says so and names TASK-213.
- **A spec coverage gap, reported rather than patched.** `bulk-filter-operations` describes
  `WouldTargetEveryRow` and `AddRequiredWhere` in detail, and **no glob in that area reaches
  `AbstractConnectorBase.cs`**, where both live. This fix's change to the guard is visible in that area's
  diff only through the funnels that call it, so a future fix confined to that file would produce a clean
  diff there. Recorded in the spec's § Regen provenance; `.map.yml` is human-owned and was not edited. Third
  instance of this shape in the same area (after the TASK-110 and TASK-109 notes already in the map) — which
  is an argument for deciding [[TASK-208]] rather than routing around it a fourth time.
- **`WholeTableWriteException` lives in `Birko.Data.SQL/Exceptions/`** while § Conventions states the rule it
  serves in backend-neutral terms. Noted in TASK-212 § Out of scope as needing its own decision (a public-API
  relocation), not done here.

## Progress log

- step 2 — picked; ranked above the 45 per-area triage batches under STORY-053/054 because each of those
  demands "all N findings confirmed or refuted" (9–36 unverified harvester claims apiece) and so cannot
  finish inside one session, while this is a single confirmed mechanism at one call site with its blocking
  dependency TASK-109 already `done`. Its severity is the weaker key: operational (a false injection
  signature in query logs), not a wrong answer.
- step 3 — verified: **rescoped, severity upgraded**. The filed mechanism holds exactly, but measured on real
  SQLite the `1 = 1` satisfies `AddRequiredWhere` and so **bypasses the SH-H002 destructive guard**: DELETE
  left 0 of 3 rows and UPDATE rewrote 3 of 3, both silently. Acceptance criterion 2 was inverted (it required
  the bypass be preserved) and is corrected, with four criteria added: pre-check placement, opt-out
  unaffected, one producer, negated-group flip. Had the pick been ranked on this, it would have led the pool
  on key 1 rather than trailed it on key 4 — the ranking was right about the pick and wrong about why.
  Full evidence in § RESCOPED. Scratch reproduction not committed.
- step 4 — layer: **local**. Both the emission site (`InConditionStrategy`) and the assembler/guard
  (`AbstractConnectorBase`) are in `Birko.Data.SQL`, which is the framework layer consumers sit on top of.
  Nothing upstream is implicated.
- step 5 — fix in `../Birko.Data.SQL/SQL/Connectors/AbstractConnectorBase.cs` (new
  `IsAlwaysTrueCondition` / `IsAlwaysTrueChain` / `AlwaysFalseSql`; both `ConditionDefinition` overloads and
  `AppendSubConditionsTo` reduce; `WouldTargetEveryRow` shares the verdict) and
  `.../Strategies/InConditionStrategy.cs` (the always-true half no longer renders). Tests in
  `Birko.Data.SQL.Tests/EmptyNotInReductionTests.cs` (new, 24 cases),
  `Birko.Data.SQL.Tests/Strategies/InConditionStrategyTests.cs` (3 changed) and
  `Birko.Data.SQL.SqLite.Tests/EmptyNotInEndToEndTests.cs` (new, 18 cases). Suites green: **484/484** and
  **164/164**. Blast radius cleared against **23** SQL-touching suites (Providers, Views, ViewModel, Caching,
  SqLite.View, Data.Core, Repositories, Aggregates, View.Migrations, Data.Migrations.SQL, BackgroundJobs.SQL,
  Workflow.SQL, Data.Tenant, all 9 Data.Sync.*, all 5 Models.*.SQL) — all pass.
- step 8 — **`tasks/README.md` dashboard regen deliberately NOT run.** The working tree carries an
  uncommitted `/tasks intake` run from another session (45 new triage tasks under STORY-053/054/055, plus
  three STORY bodies and `decisions.md`), and the dashboard was already stale with respect to it before this
  task started. Regenerating would have folded 45 unrelated tasks into this task's commit, and the file must
  not be hand-edited. **Action for whoever commits that intake run: run `/tasks triage`** — it will pick up
  both their work and TASK-137/212/213 in one pass. Nothing else in this close depends on it.
- step 7 — respecced `filter-expression-translation` (the *SQL IN translation and empty sets* requirement
  rewritten; one scenario title replaced because it asserted the old rendering — *"Empty NOT IN is always
  true"* → *"has no rendering and is reduced away"* — plus 8 new scenarios for the AND/OR/negation algebra)
  and `bulk-filter-operations` (the guard requirement now states that a non-empty collection can mean
  everything and shares the renderer's reduction; 4 new scenarios). Diff reviewed: 129 insertions, 10
  deletions, one title change, nothing unintended. Both stamped at `34fa9b2`. Spawned [[TASK-212]] and
  [[TASK-213]]; recorded the `AbstractConnectorBase.cs` coverage gap in `bulk-filter-operations` § Regen
  provenance without editing the human-owned `.map.yml`.
- step 6 (re-derived) — split re-measured after step 7's parity cases grew the suite: **29 of 54** (was 29 of
  45; all 9 parity cases are pins). Fix restored, both suites green at 484/484 and 173/173.
- step 6 — surgical reintroduction (leaf verdict forced `false` + strategy returns `1 = 1` again; every
  signature intact, since a full revert would not compile against tests that name the new API — the TASK-204
  trap): **29 of 45 failed**. Split by project: `Birko.Data.SQL.Tests` **24 of 27**,
  `Birko.Data.SQL.SqLite.Tests` **5 of 18**. Fix-dependent and contract pins named in § Outcome. Fix restored
  and both suites re-verified green.
