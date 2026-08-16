---
id: TASK-218
parent: STORY-051
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P1
assignee: ai
picked-by: fix-next
created: 2026-08-16
depends-on: []
blocks: []
related: [TASK-214, TASK-212]
pr: [Birko.Data.Core@aaa9b3e, Birko.Data.MongoDB@77d9aba, Birko.Data.Core.Tests@2a0d108, Birko.Data.MongoDB.Tests@946c0f5]
github-issue: null
jira-key: null
findings: []
---

# An `IN` filter over a C# **array** does not translate on MongoDB — `NotSupportedException`

## Context

Found by [[TASK-214]] (2026-08-16). Not that task's root cause — filed rather than folded in, because the
mechanism is expression binding, not serialization.

`MongoFilterMatrixLiveTests` is gated on `BIRKO_MONGO_HOST` and, per TASK-214's own write-up, **had never
run in this environment**. TASK-214 fixed the serialization defect that stopped every write, which let the
suite reach its assertion for the first time. It reports **26 of 27** filter shapes matching the
compiled-delegate oracle. The one divergence:

```
inClosure      -> THROW NotSupportedException: Specified method is not supported.
```

for the shape `x => amounts.Contains(x.Amount)` where `amounts` is `int[]`.

**Characterised offline** (no server needed — `Builders<M>.Filter.Where(e).Render(...)`, driver 3.2.0,
.NET 10):

| written as | renders |
|---|---|
| `int[] arr; x => arr.Contains(x.Amount)` | **`NotSupportedException: Specified method is not supported.`** |
| `List<int> list; x => list.Contains(x.Amount)` | `{ "Amount" : { "$in" : [1, 5] } }` |
| `IEnumerable<int> seq; x => seq.Contains(x.Amount)` | `{ "Amount" : { "$in" : [1, 5] } }` |
| `x => Enumerable.Contains(arr, x.Amount)` | `{ "Amount" : { "$in" : [1, 5] } }` |

Only the *instance-style call on an array* fails, and the explicit `Enumerable.Contains(arr, …)` form over
the **same array** succeeds — so it is not the array, it is which method the compiler binds. On .NET 9+ an
array's `.Contains(x)` binds to `MemoryExtensions.Contains(ReadOnlySpan<T>, T)` rather than to
`Enumerable.Contains`, and the driver's LINQ translator does not know that method.

**This framework already knows about this binding.** `Birko.Data.Expressions.PredicateScope` documents and
handles it explicitly — "On .NET 9+ an ARRAY `set.Contains(x.Col)` binds to
`MemoryExtensions.Contains(ReadOnlySpan<T>, T, IEqualityComparer<T>?)` … so the collection arrives wrapped
in an implicit ReadOnlySpan conversion" — because `RequireBoundedFilter` had to see through it. So the
unbounded-filter guard is **not** affected. What is affected is the *translation* on backends that forward
the raw expression to a driver.

## Why this is worth a task rather than a note

- It is a **silent-at-compile-time, loud-at-runtime** trap with a working look-alike one keystroke away:
  `new[] { … }` fails, `new List<int> { … }` succeeds, and nothing warns.
- The obvious workaround (`.ToList()`) is invisible unless you already know, and the exception message —
  *"Specified method is not supported"* — names no method.
- MongoDB has **no hand-rolled parser**; the expression goes straight to the driver. Backends that *do*
  parse (SQL, ElasticSearch) may or may not recognise the span binding — **unmeasured, and that is the
  point of the first acceptance row.** Do not assume it is MongoDB-only.

## Measurement (2026-08-16, step 3) — MongoDB-only, so the fix is per-backend

The first acceptance row, answered before any code was written. The same four spellings through each
backend's own translator, driver 3.2.0 / .NET 10:

| spelling | SQL `ParseConditionExpression` | ElasticSearch `ParseFilterQuery` | MongoDB driver |
|---|---|---|---|
| `int[] arr; x => arr.Contains(x.C)` | `C IN (1,5)` | `terms=(1,5)` | **`NotSupportedException`** |
| `List<int>` | `C IN (1,5)` | `terms=(1,5)` | `$in: [1,5]` |
| `IEnumerable<int>` | `C IN (1,5)` | `terms=(1,5)` | `$in: [1,5]` |
| `Enumerable.Contains(arr, x.C)` | `C IN (1,5)` | `terms=(1,5)` | `$in: [1,5]` |

Binding confirmed in the same run: `int[]` binds `System.MemoryExtensions.Contains` with 2 arguments
typed (ReadOnlySpan, Int32); `List<int>` binds the instance method with 1.

**Only MongoDB is affected**, and the reason is structural rather than incidental: SQL and ElasticSearch
walk the tree and *evaluate* the operand themselves, so the unwrapped array evaluates fine — SQL has in
fact already been bitten by the sibling 3-argument overload and carries `IsNonOperandArgument` for it.
MongoDB forwards the raw expression to a translator that simply does not know the method.

So per the task's own rule — *"a defect present in one backend argues for a per-backend fix"* — the helper
lives in `Birko.Data.Core/Expressions/SpanContains.cs`, available to every store, and is **wired only in
MongoDB**.

## Approach

1. **Measure the other backends before choosing a fix.** Run the same four shapes through the SQL
   connector's `ParseConditionExpression` and ElasticSearch's translator. A defect present in one backend
   argues for a per-backend fix; present in several argues for normalising the expression once, upstream of
   all of them.
2. Candidate fixes, cheapest first:
   - **Document it** in `Birko.Data.MongoDB/CLAUDE.md` and let it throw. Honest, but leaves a trap.
   - **Normalise the expression** — rewrite a `MemoryExtensions.Contains(ReadOnlySpan<T>, T)` node to the
     equivalent `Enumerable.Contains(IEnumerable<T>, T)` before handing it to the driver. `PredicateScope`
     already unwraps the implicit span conversion, so the detection half exists and should be **reused,
     not re-derived** (the one-producer rule in CLAUDE.md § Conventions).
   - Reject shape-by-shape special-casing in each backend — that is how this family of defects spreads.
3. Whichever is chosen, the matrix suite is the regression: it already covers the shape and already fails.

## Acceptance criteria

- [x] The four `Contains` bindings are measured against SQL and ElasticSearch as well as MongoDB, and the
      result recorded here — this decides whether the fix is per-backend or shared. **Measured; see
      § Measurement. Only MongoDB is affected → per-backend wiring of a shared helper**
- [x] `x => arr.Contains(x.Member)` over an array either translates correctly on every backend that
      forwards expressions, or throws an exception that **names the binding and the workaround** rather
      than `Specified method is not supported` — it now **translates correctly**, so no message was needed
- [x] `MongoFilterMatrixLiveTests` reports 27 of 27, or its `inClosure` row is replaced by an assertion
      that pins the documented refusal — not deleted. **27 of 27**; the row is untouched
- [x] A **non-gated** test pins the chosen behaviour; the render check needs no server (see the table above)
      — `Birko.Data.Core.Tests.SpanContainsTests`, 11 tests, no gate. A gated live test was added *as well*
      (`MongoArrayContainsLiveTests`), because a correct render is not proof the query selects the right rows
- [x] Red-verified with the split as numbers; contract pins named as pins — see step 6 in the progress log

## Out of scope

- `PredicateScope` / `RequireBoundedFilter`. It already handles the span binding and was re-read to confirm
  it; changing it is not part of this.
- [[TASK-214]]'s serialization fix, which is what made this reachable.

## Human test plan

- [x] With a live MongoDB, run `Birko.Data.MongoDB.Tests` and confirm the matrix suite's report — the value
      of this suite is that it prints all 27 shapes, so a fix that breaks a different shape is visible in
      the same output. **Done** (`docker run --rm -p 27017:27017 mongo:7`): 27 of 27, and the full report
      was read rather than just the pass/fail — every other shape still `OK`.

## Outcome

**What was broken.** On .NET 9+ an array's `set.Contains(x.Col)` binds to `MemoryExtensions.Contains`,
which the MongoDB driver's LINQ translator does not know. `x => arr.Contains(x.Amount)` threw
`NotSupportedException: Specified method is not supported` — a message naming no method — while the
`List<int>` spelling one keystroke away worked. Nothing warned at compile time.

**The fix.** `Birko.Data.Expressions.SpanContains.Rewrite` turns every
`MemoryExtensions.Contains(ReadOnlySpan<T>, T)` node into `Enumerable.Contains(IEnumerable<T>, T)`, which
every translator understands. Wired at the **nine methods where a caller filter arrives** in the two
MongoDB stores — not at the ~30 sites where a filter reaches the driver — so the whole-collection guard,
`RequireFilter` and the driver all observe one shape, and a future hand-off site is correct without being
told.

**One producer for the span unwrap.** `PredicateScope` had its own private `UnwrapSpanConversion`; it now
calls `SpanContains.UnwrapSpanConversion`. The guard and the rewrite cannot disagree about what a
span-bound `Contains` looks like — which matters because this is the third distinct defect from the same
overload change, and the previous two were each fixed in isolation.

**Judgement calls, and the stricter option rejected in each case.**

- **A real comparer is left alone.** `Enumerable.Contains(source, item)` cannot honour one, and the
  3-argument `Enumerable` overload is no more translatable than the span one — so rewriting would silently
  change the predicate's meaning. The broader rewrite was available and refused: a wrong answer is worse
  than the throw.
- **Wired in MongoDB only, though the helper is public and general.** Applying it in SQL and ElasticSearch
  too would have been "safer" by symmetry, and is exactly what this framework's convention forbids without
  measuring — both were already correct, and a rewrite that fires where nothing is broken is how a
  normalisation quietly changes a working translation.
- **Entry-point wiring rather than a conversion helper at each hand-off.** A `Filter()` helper reads well
  but would have needed ~30 call sites converted, each an opportunity to miss one; the nine arrival points
  are the actual funnel.
- **`Rewrite` returns the same instance when nothing matched**, asserted by reference identity, so the
  pre-pass is free on the overwhelming majority of filters. Without that assertion "it's a no-op" is a
  claim rather than a fact.

**Flagged, not fixed.**

- **CosmosDB and RavenDB also forward raw expressions and were NOT measured** — they need live services
  this environment does not have. They are plausibly affected the same way, and the helper is public and
  ready; wiring is one line per entry point. Deliberately not done blind, by the same rule that kept it
  out of SQL and ElasticSearch. Worth its own task when either service is available.
- **`ExpressionNormalizer`'s doc comment says native-LINQ backends "honour the raw constructs already"**,
  which this task shows is too strong. Corrected in the spec's Purpose; the code comment still reads that
  way and remains true for the constructs it actually covers (ternary / coalesce / arithmetic), so it was
  not widened here.

## Implementation plan

_Populated by `/tasks plan TASK-218` — leave empty until then._

## Progress log

- step 2 — picked; user-directed. It was the ranked runner-up anyway: TASK-200 outranked it on paper but is a consumer-repo DECISION whose headline defect was measured not to reproduce (0 of 184 write call sites pass ActionMeta, so nothing reaches the outbox), so it is not fix-next work.
- step 3 — verified. The finding holds exactly as filed. Measurement across all three translators recorded in the Measurement section: MongoDB-only, so a shared helper wired per backend.
- step 4 — layer: local. The helper belongs in Birko.Data.Core/Expressions beside PredicateScope, which shares its unwrap; the wiring belongs in Birko.Data.MongoDB.
- step 5 — fix in Birko.Data.Core/{Expressions/SpanContains.cs (new), Expressions/PredicateScope.cs (delegates its unwrap), .projitems} + Birko.Data.MongoDB/Stores/{MongoDBStore.cs, AsyncMongoDBStore.cs} (9 entry points); tests in Birko.Data.Core.Tests/SpanContainsTests.cs (new, 11, non-gated) and Birko.Data.MongoDB.Tests/Stores/MongoArrayContainsLiveTests.cs (new, gated).
- step 6 — two isolating reverts. Revert A (unwire from the MongoDB stores, keep the helper): MongoDB.Tests 2 of 85 failed, BOTH fix-dependent = An_array_backed_IN_filter_selects_the_right_documents and FilterShapes_MatchCompiledDelegateOracle; Core.Tests 59/59 GREEN, correctly, because those tests call the helper directly and so pin the helper rather than the wiring. Revert B (gut Rewrite to return its input, keep the wiring): MongoDB.Tests the same 2 of 85, Core.Tests 2 of 59 = The_span_bound_Contains_is_rewritten_to_Enumerable_Contains and An_enum_set_uses_the_three_argument_overload_and_is_still_rewritten. Contract pins, passing either way: An_array_Contains_really_does_bind_MemoryExtensions (pins the RUNTIME premise, not the fix), A_real_comparer_is_left_alone, A_predicate_with_no_span_Contains_is_returned_unchanged, A_null_predicate_stays_null, the four-spelling equivalence theory, and all 500 SQL + 129 ElasticSearch tests — those last two sets being the evidence the fix did not need to touch them. Fixed state: 85/85 + 59/59 + 500/500 + 129/129 + 69/69.
- step 7 — respecced filter-expression-translation (new requirement "A span-bound array Contains is rewritten before a driver sees it", 6 scenarios; Purpose corrected, since handing a tree to a driver is not the same as the driver understanding it) and bulk-filter-operations (regen note; the MongoDB stores are in that area's globs because that is where the wiring landed). No .map.yml change needed: Birko.Data.Core/Expressions/*.cs already globs the new file.
- step 8 — closed done; aaa9b3e + 77d9aba (production, two repos) / 2a0d108 + 946c0f5 (tests, two repos). Merge gate: builds warning-clean; register-on-introduce applied to the aggregator CLAUDE.md (§ Conventions + § Recent Updates) — the new cross-cutting pattern is "a language-level overload change is a framework-wide event: normalise it once, wire it where it actually breaks". security-review not triggered: no auth/crypto/secrets/user-input/new-dependency/endpoint surface; the rewrite preserves predicate meaning and is proven to by a compiled-delegate oracle in both directions.
