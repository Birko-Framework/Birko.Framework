---
id: TASK-308
parent: STORY-051
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P0
assignee: ai
picked-by: fix-next
created: 2026-09-08
depends-on: []
blocks: []
# findings: ids this task remediates, from a review/audit/spec-harvest pass (CR-* SEC-* SH-* VC-*)
findings: [SH-H021, SH-H022, SH-H024, SH-H025, SH-H026, SH-H027, SH-H028]
pr: 505c31c
github-issue: null
jira-key: null
---

# Triage the 7 remaining high spec-harvest findings in `filter-expression-translation`

## Context

Filed by `/tasks intake --epic EPIC-014 --story STORY-051` on 2026-09-08. [[STORY-051]] had **31 task
files covering 18 of its 57 findings**, and the other **39 had no task at all** — so they were
invisible to `/tasks pick`, to the `Next up` snapshot and to [[fix-next]]. *A checklist line is filed,
not scheduled.* This is the same decomposition [[STORY-053]] received on 2026-08-09 for the medium
findings, applied a month later to the tier that outranks it: **high** means silent data loss,
cross-tenant leakage, auth bypass, or a destructive operation on the wrong rows.

This task owns the **7** open findings in the `filter-expression-translation` area.

| Finding | Claim | Named location |
|---|---|---|
| `SH-H021` | An untranslatable AND/OR operand is read as constant TRUE, so an OR predicate loses its WHERE clause entirely | `Birko.Data.SQL/SQL/DataBase.cs:916` |
| `SH-H022` | ReturnSingleSubCondition overwrites the parent's IsNot, so `!(a && trueConst)` renders as `a` — the opposite rows | `Birko.Data.SQL/SQL/DataBase.cs:948` |
| `SH-H024` | An unrecognised parameter-bound method call in an UPDATE SET value is reflectively invoked with null args and its result bound as a constant | `Birko.Data.SQL/SQL/DataBase.cs:239` |
| `SH-H025` | A value that will not convert to double yields an unbounded range query (ElasticSearch) | `Birko.Data.ElasticSearch/ElasticSearch/ElasticSearch.cs:246` |
| `SH-H026` | An unhandled expression node silently removes the whole WHERE clause (SQL) | `Birko.Data.SQL/SQL/DataBase.cs:818` |
| `SH-H027` | CombineBool silently drops an untranslatable AND/OR operand (ElasticSearch) | `Birko.Data.ElasticSearch/ElasticSearch/ElasticSearch.cs:209` |
| `SH-H028` | ElasticSearch String.Contains passes the raw value into a QueryStringQuery — Lucene query-syntax injection | `Birko.Data.ElasticSearch/ElasticSearch/ElasticSearch.cs:555` |

Detailed in [`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md)
§ High severity -> `### area: filter-expression-translation`, lines 172-222.

**The contract under review** is specced in [`docs/specs/filter-expression-translation.md`](../../../docs/specs/filter-expression-translation.md),
harvested from 28 source files — `../Birko.Data.Core/Expressions/ExpressionNormalizer.cs`, `../Birko.Data.Core/Expressions/ExpressionParameterReplacer.cs`, `../Birko.Data.Core/Expressions/PredicateScope.cs`, and more. Every one points into a **sibling repo**, so a
fix here normally lands as three commits in three repos (production, regression suite, this file) per
CLAUDE.md § Integration model.

**These are unverified harvester claims, not confirmed defects.** Confirm each against the code before
fixing. The prior to carry in comes from the 15 high findings that *were* checked by hand at harvest
time: **13 CONFIRMED** (one of them re-verified **wider** than filed), **2 CONFIRMED-NARROWER**,
**0 refuted**. So expect most to hold and a meaningful minority to need their scope corrected — and
note that "wider" has happened, so a claim is not a ceiling. Refuting on the record is a valid close; a
finding silently dropped is one the next sweep re-raises and someone re-litigates from scratch.

**Measured consumer reach, 2026-09-08** (so the fix is priced on what it protects, not on the claim's
wording): **LIVE.** `Birko.Data.SQL` is imported by **6** consumer aggregators (excluding the Sandbox smoke harness, which imports everything), and `DataBase.cs` is on the path of every SQL read those consumers make.

⚠ Latent is **not** a reason to skip or downweight a finding — the framework's recent history is
largely defects that stayed latent until a consumer selected the backend, and § TASK-219/256 record
that such a window *"closes the moment one does"*. It is a reason not to overstate urgency in a report.

**Why P0:** every SQL read builds its WHERE through `DataBase.cs`, so a predicate that silently becomes match-all or loses its clause is the same class as [[TASK-116]] (degraded leaf → match-ALL) and [[TASK-110]] (identifier injection), both P0.

⚠ **Do not re-derive what [[TASK-109]], [[TASK-116]] and [[TASK-137]] already settled.** Those three
established the framework's answer to a degraded predicate: a leaf that cannot be translated is *tracked*
rather than silently replaced by a constant, and a filter that reduces to every row is **refused**
(`PredicateScope`, `WholeTableWriteException`). `SH-H021`/`SH-H026`/`SH-H027` are the **read**-path
siblings of that family, which those tasks explicitly left open — TASK-109 says so in as many words.
Check what those fixes already cover before assuming a finding still holds; a partial overlap is the
likeliest outcome, which makes **confirmed-narrower** the verdict to expect here.

`SH-H028` (Lucene query-syntax injection through `String.Contains`) is a different species from its six
neighbours — an injection sink, not a mistranslation. It is the one finding in this area whose fix
belongs to the identifier/containment family in § Conventions rather than to the predicate family.

**Ordering constraint — the spec currently documents these defects as shipped behaviour.** The
harvest specced what the code *does*, defects included, which is exactly what let it find them. So a
behavioural fix leaves `docs/specs/filter-expression-translation.md` lying until `/specs regen filter-expression-translation` runs, and **that spec
diff is the fix's evidence**.

## Acceptance criteria

- [x] All 7 findings are marked **confirmed**, **confirmed-narrower**, **confirmed-wider** or **refuted** against
      the code, with the verdict and its evidence (`file:line` + the mechanism, not just the rule)
      written back into `SPEC-HARVEST-FINDINGS-2026-07-30.md`. Every `Verdict:` line names the specific
      code it traced
- [x] Confirmed findings are fixed with a regression test, or explicitly waived with a recorded reason.
      Findings sharing a root cause are fixed **together**, not one edit each
- [x] Each regression test is **red-verified**: reverting the fix fails it. Report the split as numbers,
      and name any test that passes either way as a contract pin rather than as evidence
- [x] ⚠ For a claim of *silent* loss, corruption or leakage, the assertion is the **observed
      state** — rows counted, the value read back, the tenant that could see it — never that no
      exception was thrown. § Conventions records several defects that a "did not throw" assertion
      hid, including one in this epic that hid a live failure for weeks
- [x] Any behavioural fix is followed by `/specs regen filter-expression-translation`, with the spec diff reviewed as the
      change's evidence
- [x] A confirmed finding too large for this task is spawned via `/tasks spawn` — never left as a
      ticked box with the work undone, and never as an `## Out of scope` sentence describing work
- [x] [[STORY-051]]'s **Progress** line and its task table reflect this area's closed count

## Out of scope

- The other 32 open high findings — they belong to the other 14 per-area tasks under
  [[STORY-051]].
- The 18 high findings already covered by [[STORY-051]]'s existing 31 task files. If triage shows one of
  those fixes did **not** hold, that is a regression: file it fresh and say so, per `/tasks intake`
  § *Re-running a pass*.
- Medium-severity findings in this area — [[TASK-153]] owns those.
- Low-severity findings in this area — [[TASK-189]] owns them. Where a fix closes findings across
  tiers, do it once and cross-reference; do not split one edit across two tasks.
- **Test gaps.** Test coverage was explicitly out of scope for the harvest sweep, so a missing test is
  not a finding here — only a test a confirmed fix needs.

## Human test plan

- [x] **N/A — fully covered by automated tests, and a human would add nothing.** Resolved at close as the
      task instructed, rather than defaulted. All seven findings are library contracts with no UI surface,
      and every one of them is a *silent* wrong answer whose evidence is a number: rows counted after a
      read or a delete, a value read back from a column, or a rendered NEST query object. A person
      eyeballing any of those would be reading the same assertion less precisely — and the specific thing
      a human is good at here (noticing that a query "looks wrong") is exactly what failed for the life of
      these defects, since the whole ES suite stayed green through all three of its fixes.

## Implementation plan

_Populated by `/tasks plan TASK-308` — leave empty until then._

## Measured verdicts — 7 of 7 confirmed, 0 refuted

| Finding | Verdict | The measurement |
|---|---|---|
| `SH-H021` | **confirmed-narrower** | `(x.Payload is string) \|\| x.Amount == 10` rendered **no WHERE**, READ **3 of 3 rows**. The `&&` twin rendered `WHERE Amount = @p` — the conjunct silently gone. ⚠ `Delete` already threw `WholeTableWriteException` (3 of 3 rows left), so the destructive half was closed by SH-H002 + TASK-137 |
| `SH-H022` | **confirmed** | `!(x.Amount == 10 && trueFlag)` → `WHERE Amount = @p` against the control's `WHERE NOT (Amount = @p)`. READ **1 row [10]** where [20,30] was asked. `DeleteAsync` **threw nothing** and left [20,30] — destroyed the complement |
| `SH-H024` | **confirmed** | `SET Name = string.Concat(r.Name, "-", r.Name)` on the `Amount == 10` row **stored `"-"`** — `Concat(null,"-",null)` — no exception, no log entry |
| `SH-H025` | **confirmed** | `x.Date > cutoff` → `NumericRange(gt=NULL, gte=NULL, lt=NULL, lte=NULL)`, and it survived **both** `ParseFilterQuery` and `ParseRequiredFilterQuery` with no throw |
| `SH-H026` | **confirmed-narrower** | top-level `x.Payload is string` → no WHERE, READ **3 of 3**; `x => pred(x)` the same. ⚠ `Delete` already refused, same as SH-H021 |
| `SH-H027` | **confirmed** | `untranslatable && Count==5` → `Bool(must=1)`; `\|\|` → `Bool(should=1)`. Both passed `ParseRequiredFilterQuery` with no throw |
| `SH-H028` | **confirmed** | `Contains("secretField:*")` → `QueryString(query=<<secretField:*>>)`; `* OR Count:5` and `unbalanced(` likewise verbatim |

Full verdicts with their evidence are written into
[`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md).

**The prior held.** This story recorded 13 confirmed / 2 narrower / 0 refuted out of 15 hand-checked; this
area came in at 5 / 2 / 0. And the two narrowings were both the *same* correction the task file predicted:
`SH-H021` and `SH-H026` each claimed a whole-table delete, and that half was already closed — what survived
is the **read**-path wrong answer TASK-109 explicitly left open.

## Outcome

**5 root causes, not 7**, and the grouping is the substance of the fix:

1. **`SH-H021` + `SH-H026` — an expression node the parser has no branch for.**
   `ParseConditionExpression` dispatches on lambda / unary / binary / method-call / member nodes in three
   independent `if` groups and ends in `return Array.Empty<Condition>()`, so a `TypeBinaryExpression`,
   `InvocationExpression` or `NewExpression` produced no conditions and no complaint —
   which `AddWhere` renders as no `WHERE`, i.e. `_ => true`. `RequireTranslatableNode` refuses at the
   **top of the recursion**, which is what makes one guard cover a node at the root (`SH-H026`) and as an
   operand (`SH-H021`).
2. **`SH-H022` — `ReturnSingleSubCondition` overwrote the enclosing negation.** Now
   `parent.IsNot ^ surviving.IsNot`.
3. **`SH-H024` — a parameter-bound call in a SET value was reflectively invoked.** Refused at the call
   site; `EvaluateExpression`'s method-call arm also gated, as the *defensive* half.
4. **`SH-H025` + `SH-H027` — a failed sub-translation became a query that means something else.** A null
   range bound is an unconstrained range; a dropped operand is a different query. Both now refuse, and the
   range gains `DateRangeQuery` so the capability survives.
5. **`SH-H028` — a value in Lucene statement position.** `Contains` moves off `QueryStringQuery` onto
   `WildcardQuery`, whose value has no grammar beyond `*` / `?` / `\`.

### Step 6 — proven able to fail

Eight mutations, each reverted. Counts are per suite.

| Mutation | Result | Fix-dependent tests |
|---|---|---|
| **M1** remove `RequireTranslatableNode` | SQL **1 of 695**, SqLite **7 of 379** | `UntranslatablePredicate_IsRefusedByTheParser_NotRenderedAsNothing`; the 4 `An_untranslatable_*` / `An_invocation_expression_*` tests; the 3 inverted `DestructiveFilterEndToEnd` ones |
| **M2** `IsNot` assigned instead of XOR'd | SQL **4 of 695**, SqLite **3 of 379** | all 4 `A_negation_around_a_collapsed_*`; `A_negated_group_*_keeps_its_negation_on_a_read`, `..._deletes_the_rows_it_names`, `A_doubly_negated_group_cancels_*` |
| **M3** remove the SET-value refusal | SqLite **1 of 379** | `An_unrecognised_parameter_bound_call_in_a_SET_value_is_refused_not_invoked` |
| **M4** remove `EvaluateExpression`'s gate | **0 of 1,236** — see below | *(none)* |
| **M5** `CombineBool` drops a null operand again | ES **4 of 162** | `An_untranslatable_conjunct_*`, `An_untranslatable_disjunct_*`, `The_destructive_by_query_path_refuses_a_half_translated_boolean` (×2 rows) |
| **M6** remove the `DateTime` range arm | ES **6 of 162** | `A_DateTime_ordering_comparison_*`, `Every_ordering_operator_carries_a_bound_for_a_DateTime` (×4), `A_DateTimeOffset_is_normalised_*` |
| **M7** `Contains` back to `QueryStringQuery` | ES **8 of 162** | `Contains_no_longer_hands_the_value_to_the_query_grammar`, `Contains_escapes_exactly_*` (×7) |
| **M8** `EndsWith` unescaped again | ES **1 of 162** | `EndsWith_escapes_on_the_same_terms` |

⚠ **M4 reds nothing, and that is reported as a result rather than smoothed over.** The
`EvaluateExpression` gate is **defensive, not witnessed** (§ TASK-261): M3's call-site refusal fires
first, so the fabricating arm is unreachable today — confirmed by running M4 against all three suites
(**0 of 1,236**) and by running **M3+M4 together**, which reds exactly the one test M3 does alone. It is
kept, and labelled in the code, because without it the method holds two contradictory contracts across 19
call sites — one arm refusing a parameter-bound tree while the other fabricates a value from one — and
that inconsistency *is* SH-H024's root cause. The call-site refusal only closes the door that happens to
exist today.

**Contract pins, not evidence:** the numeric range path unchanged; `StartsWith` unchanged; a plain
negation; an un-negated collapse; an ordinary predicate, `x => true`, a collection `Contains` and a
`StartsWith` on the SQL side; the null-filter refusal on a destructive write; a translated and a
parameter-free call in a SET value; a fully translatable boolean keeping both clauses; an
empty-collection operand still composing; and the two premise pins (`string` declares no ordering
operators; `Trim()` really is untranslatable).

### Judgement calls, and why the stricter or simpler option was rejected

- **The node guard is a permissive whitelist, not an exhaustive one.** `ConstantExpression` and
  `ParameterExpression` are accepted although neither is known to reach it, because a false refusal breaks
  working code and is worse than the hole — `PredicateScope`'s own asymmetry. Noted in the code so the
  next reader knows adding a kind is safe and removing one is not.
- **Not an edit to `IsConstantBoolCondition`.** That was the smaller diff and it is wrong: measured,
  `(x.A == 1 || true) && x.B == 2` leaves the identical "nothing was parsed" state and `true` is the
  **correct** answer there. Merging the two cases would have traded a silent match-all for a silent
  narrowing.
- **`NotSupportedException`, not a new type and not `WholeTableWriteException`.** It matches
  `ElasticSearch.ParseFilterQuery` for the same predicate, so one `catch` selects an untranslatable filter
  on either backend — and there is precedent inside `DataBase.cs` itself, where `RenderValueFragment`
  already refuses an untranslatable value operand with that type. `WholeTableWriteException` would have
  been wrong on a read, where nothing about a table is being written.
- **`SH-H025` refuses only what it cannot express, rather than everything non-numeric.** A blanket refusal
  was the minimal reading of the finding and would have made every `DateTime` comparison untranslatable —
  § TASK-281's rule: when a fix appears to cripple a feature, check whether the provider offers another
  mechanism first. NEST does.
- **No `TermRangeQuery` arm.** I wrote one, then measured that `string` declares no ordering operators, so
  it was unreachable code advertising a capability nobody can call. Removed, and the premise is pinned
  rather than the absence.
- **`EndsWith` escaped although no finding named it.** § Conventions' *guard the whole verb family or none
  of it*: it built a wildcard pattern from caller text too. `StartsWith` needed nothing and is asserted
  unchanged, because a `PrefixQuery` has no pattern syntax and escaping there would corrupt a legitimate
  prefix.

### Corrections to my own work, recorded because each was caught by running rather than reading

- **The refusal message interpolated `expr.ToString()`**, which renders a captured closure's *values*
  into a message that travels into logs. Narrowed to the node kind and CLR type at the security pass.
- **`A_translated_call_in_a_SET_value_still_works` used `ToUpper()`** and failed — which is how
  [[TASK-331]] was found. `ToUpper` binds no parameter, so `Update`'s `values.Any()` gate skipped the
  whole statement: the test would have passed for a reason unrelated to the guard. Switched to `Replace`,
  and the no-op pinned as a defect.
- **Two `NegatedGroupCollapseTests` assertions were wrong about where `!=` lives.** There is no
  `ConditionType.NotEqual`: `!=` is `Type=Equal` with `IsNot` on the *comparison leaf*, which the
  comparison branch nests under the parent — so the survivor handed to the collapse is a wrapper whose own
  `IsNot` is false. Measured by dumping the tree; the correction is recorded in the test, because the wrong
  reading is what would make a reviewer think XOR is the wrong operator.
- **The `TimeSpan` refusal test first used `x.Date - x.Date > window`**, which routes to
  `BuildScriptComparison` and is refused there for an unrelated reason — it would have tested a different
  guard and passed for the wrong reason.
- **A new pin in `DestructiveFilterGuardTests` asserted an empty `NOT IN` renders nothing** and failed:
  that fixture's `Widget` is deliberately unmapped, so an unresolvable column takes the value-fragment
  path. Left to `EmptyNotInReductionTests`, with the reason in place so the gap does not read as an
  oversight — and it is what surfaced the pre-existing `NotSupportedException` precedent.

### Flagged, not fixed

- **[[TASK-331]] (P1)** — an expression-valued `UPDATE` that binds no parameter issues no statement at
  all, silently, because `Update` opens with `if (values.Any())` and the expression path puts only bound
  constants in `values`. Different layer, different root cause, so it is spawned rather than folded in, and
  pinned by a test that asserts the defect.
- **`ParseEndsWith`'s unescaped value was in scope and is fixed**; nothing else adjacent was left.

## Progress log

- step 2 — picked by explicit user override ("ok do 308"), which matches where the blast-radius ranking
  already put it: it was named as the next pick at [[TASK-329]]'s close, losing key 1 only because a
  silent whole-table rewrite outranks predicate mistranslation. Now that TASK-329 is closed it is the
  pool's top candidate on key 1 (three of its seven claims are whole-table destruction on the **read**
  path, one is an injection sink), key 2 (`DataBase.cs` is on every SQL read for 6 consumer aggregators)
  and key 3 (every claim is a *silent* wrong answer — no exception anywhere in the seven).
- step 3 — verified all 7 by measurement, not reading: a throwaway probe rendering conditions + SQL and
  counting rows on on-disk SQLite, and a second rendering NEST queries offline. **7 confirmed, 2 of them
  narrower, 0 refuted.** Both narrowings are the destructive half already closed by SH-H002 + TASK-137,
  exactly as the task file predicted. Verdicts written into the harvest doc.
- step 4 — layer: local, both repos. 5 root causes across `Birko.Data.SQL` (4 findings) and
  `Birko.Data.ElasticSearch` (3).
- step 5 — fixes in `Birko.Data.SQL/SQL/DataBase.cs` and
  `Birko.Data.ElasticSearch/ElasticSearch/ElasticSearch.cs`; tests in
  `Birko.Data.SQL.Tests/DataBase/NegatedGroupCollapseTests.cs` (8 new) + 2 inverted/retargeted in
  `DestructiveFilterGuardTests`, `Birko.Data.SQL.SqLite.Tests/PredicateMistranslationEndToEndTests.cs`
  (16 new) + 3 inverted in `DestructiveFilterEndToEndTests`, and
  `Birko.Data.ElasticSearch.Tests/PredicateTranslationRefusalTests.cs` (26 new). Both probes deleted.
  ⚠ 5 existing tests asserted the wide behaviour and were **inverted rather than restored** (§ TASK-211):
  all five used an `InvocationExpression` as their untranslatable example and asserted
  `WholeTableWriteException`; the rows they assert are unchanged, only the type moved, and 0 consumer
  references to that type were measured. Eleven offline suites: **1,638 tests, 0 failed**, 51 new.
- step 6 — 8 mutations, table in § Outcome. M4 reds **0 of 1,236** and is recorded as
  defensive-not-witnessed rather than presented as evidence.
- step 7 — respecced `filter-expression-translation`: 2 requirements that documented these defects **as
  shipped behaviour** rewritten (*Silent widening of untranslatable SQL predicates* → *An expression node
  the SQL parser cannot claim is refused*; *Dropped ElasticSearch sub-clauses* → *An ElasticSearch boolean
  translates both operands or neither*), 4 new requirements added (the negated-group collapse, the SET
  value, the ordering comparison, substring matching), 44 → 48 requirements. It is the **only** area whose
  globs reach either changed file — checked rather than assumed.
- step 8 — close gate. `verify-birko-conventions` step 0a-c: no `*Core` override violations, no inline
  `Birko.*` paths, no new project; **check 0b applies** — the untranslatable-node refusal is a new
  cross-cutting rule and is recorded in § Conventions in the same change; check 9 satisfied by the new
  § Recent Updates entry. Nullable checks clean (0 warnings in all three changed suites).
  `verify-intent`: 7 of 7 criteria met. `code-review`: two findings on my own diff, both fixed — the
  message leaking closure values, and the permissive whitelist needing its edge documented.
  `security-review` **applies** on two counts and both were acted on: `SH-H028` is an injection sink
  (closed by moving to a query type with no grammar rather than by escaping a grammar), and the new
  refusal message was narrowed so it cannot carry captured values into a log.
- step 9 — out-of-scope sweep: **4 boundary, 1 spawned ([[TASK-331]]), 0 declined.** The four name owners
  (TASK-153, TASK-189, the other 14 area tasks, the harvest's test-coverage exclusion).
- step 10 — closed `done`. Production: `Birko.Data.SQL` **505c31c** (`pr:`), `Birko.Data.ElasticSearch`
  ac13bba. Tests: `Birko.Data.SQL.Tests` d46cc3d, `Birko.Data.SQL.SqLite.Tests` 040f3d9,
  `Birko.Data.ElasticSearch.Tests` d8a6321. Plus this aggregator commit.

