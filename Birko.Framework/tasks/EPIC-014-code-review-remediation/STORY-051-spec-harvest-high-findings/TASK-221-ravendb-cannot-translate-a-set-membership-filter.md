---
id: TASK-221
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
related: [TASK-218, TASK-220]
pr: [Birko.Data.RavenDB@f3631d3, Birko.Data.RavenDB.Tests@7815890]
github-issue: null
jira-key: null
findings: []
---

# RavenDB cannot translate **any** set-membership filter — `Contains` is unsupported in every spelling

## Context

Found by [[TASK-218]]'s follow-up audit (2026-08-16) while measuring whether the span-binding rewrite
needed wiring into RavenDB. It does **not** — and the reason is a larger defect.

Measured offline (no server: `DocumentStore.Initialize()` and query *building* are local; only execution
needs a database), driver as referenced by `Birko.Data.RavenDB`:

| shape | RQL |
|---|---|
| `x => x.Amount > 3` (baseline) | `from 'Docs' where Amount > $p0` |
| `int[] arr; x => arr.Contains(x.Amount)` | **`NotSupportedException: Expression type not supported: TypedParameterExpression`** |
| `List<int> list; x => list.Contains(x.Amount)` | **same exception** |
| `x => Enumerable.Contains(arr, x.Amount)` | **same exception** |
| `x => x.Amount.In(arr)` (Raven's own operator) | `from 'Docs' where Amount in ($p0)` |

The baseline proves the probe and the RQL-rendering path are sound. **Every** collection `Contains`
fails, regardless of collection type — so unlike MongoDB and CosmosDB this is not the .NET 9+
`MemoryExtensions` binding, and [[TASK-218]]'s `SpanContains.Rewrite` would change one failure into an
identical one. Raven requires `RavenQueryableExtensions.In`.

**Why this matters more than it looks.** `IN` is the canonical N+1 batch pattern — load N entities by a
set of ids in one round trip. A consumer writing the portable spelling that works on SQL, ElasticSearch,
MongoDB and (after [[TASK-220]]) CosmosDB gets a runtime exception on RavenDB alone. It is loud, so no
data is at risk; the cost is that the store is not substitutable, which is the whole point of the store
abstraction.

**⚠ CORRECTION (during implementation).** This paragraph originally read *"`Birko.Data.RavenDB.Tests` has
no filter-matrix suite at all"* and called that absence the more important finding. **That was wrong.**
`Stores/RavenFilterMatrixLiveTests.cs` exists, covers 27 shapes, and is gated on `BIRKO_RAVEN_URL` — I
inferred its absence from a non-recursive listing of the test project's root. Corrected here rather than
quietly dropped, because the wrong version was also reported verbally and the acceptance criteria were
written against it.

**Not caught until now because the suite could never run — even with the gate satisfied.** Its setup does
`store.ReadAsync(x => true)` to build the oracle, and RavenDB refuses `Where(x => true)` outright, so it
threw in setup before reaching a single shape. A suite that is gated *and* broken at line 122 reports
nothing at all. That is the same "green because it never ran" shape as [[TASK-214]] and [[TASK-218]],
one layer worse.

## Approach

Not settled — there is a real choice, and it should be made deliberately:

1. **Rewrite `Contains` → `In` for RavenDB**, the way [[TASK-218]] rewrites the span binding for MongoDB
   and CosmosDB: a per-backend pre-pass in `Birko.Data.RavenDB` turning
   `collection.Contains(x.Member)` into `x.Member.In(collection)`. Makes the portable spelling work and
   keeps the store substitutable. Needs care with the negated form and with `x.ArrayMember.Contains(const)`,
   which is the *opposite* direction of membership and must not be rewritten.
2. **Document it** in `Birko.Data.RavenDB/CLAUDE.md` and let it throw. Cheap and honest, but leaves the
   abstraction leaky for the one operation most likely to be written.

Recommendation: option 1, because the framework already accepts this shape of fix (`SpanContains`) and
because a store that cannot express `IN` is not really interchangeable with the others.

**Whichever is chosen, add the missing matrix suite** — a `RavenFilterMatrixLiveTests` mirroring the
MongoDB/Cosmos ones. Without it the next divergence is invisible again.

## Acceptance criteria

- [x] `x => ids.Contains(x.Member)` either translates on RavenDB, or throws an exception that **names
      `.In()` as the supported spelling** rather than `Expression type not supported: TypedParameterExpression`
      — it now **translates**: `from 'Docs' where Amount in ($p0)`, confirmed against a live RavenDB
- [x] `x => x.Member.In(ids)` keeps working, and the negated set-membership form is covered either way —
      both pinned; `!ids.Contains(x)` renders `not Amount in ($p0)`
- [x] A **non-gated** test pins the chosen behaviour — RQL rendering needs no server, as the measurement
      above shows. `RavenSetMembershipTests`, 15 tests, no gate
- [x] ~~`Birko.Data.RavenDB.Tests` gains a filter-matrix suite mirroring `MongoFilterMatrixLiveTests`~~ —
      **criterion was based on a false premise; see § Correction.** The suite already existed. Met in
      substance instead: it had never been able to run, and now does. First run ever: **21 of 27 shapes
      OK**, the remaining 6 filed as [[TASK-222]]. I wrote a duplicate suite before noticing and deleted it
- [x] Red-verified with the split as numbers; contract pins named as pins — see step 6 in the progress log

## Out of scope

- [[TASK-218]] / [[TASK-220]]'s span-binding rewrite. Measured **not** to apply here: all spellings fail
  equally on Raven, so that rewrite would change nothing.

## Human test plan

- [x] Against a live RavenDB (`docker run -p 8080:8080 ravendb/ravendb`), read a set of entities by id
      using the portable `Contains` spelling and confirm the same rows come back as `.In()` returns.
      **Done during this task** — and it earned its place twice over: the live run is what caught that the
      bulk-read wiring had landed inside an `if (store == null)` block as dead code, which every offline
      test passed straight over.

## Outcome

**What was broken.** RavenDB's LINQ provider translates **no** collection `Contains`, in any spelling, so
the canonical batch-load filter — `ids.Contains(x.Guid)` — threw there while working on SQL,
ElasticSearch, MongoDB and CosmosDB. The store was not substitutable for the others. Separately, and
found while building the reproduction, Raven also refuses `x => true`, which this framework documents as
the read-all / `*All` synonym.

**The fix.** `RavenSetMembership.Rewrite`, wired at the six entry points where a caller filter arrives:

- `constCollection.Contains(x.Member)` → `x.Member.In(constCollection)`, Raven's own operator;
- an explicitly-all-rows predicate → **no predicate at all**, which is exactly equivalent.

Both are the same root cause — a portable spelling Raven rejects — which is why they are one rewriter
rather than two. The all-rows judgement reuses `PredicateScope.IsExplicitAllRows`, the framework's single
producer of it, so the rewriter cannot disagree with the destructive guards about what "explicitly
everything" means. The array spelling also carries the .NET 9+ span binding, handled by reusing
`SpanContains.UnwrapSpanConversion` — that unwrap now has three consumers and still one definition.

**The discrimination that makes it safe.** Two shapes share the method name and mean opposite things.
`x.Tags.Contains("red")` — a collection *field* holding a constant — **already works** on Raven
(`from 'Docs' where Tags = $p0`), so rewriting it would have broken working code. Told apart by which
operand references the lambda parameter, the same test `ElasticSearch.ParseContains` makes. Pinned by a
test asserting the rendered RQL is byte-identical with and without the rewrite.

**Judgement calls.**

- **Rewrote rather than documented.** The task offered both; `IN` is too central to leave as a
  per-backend footgun, and the framework already accepts this shape of fix.
- **Folded in the `x => true` refusal** rather than spawning it. Same root cause, same file, same wiring
  — and without it the matrix suite still could not run, so the fix could not be demonstrated through the
  project's own regression suite.
- **`string.Contains` is left strictly alone.** Raven refuses it deliberately and its message names the
  supported alternative (`Search()`), which is more useful than anything a rewrite would produce. A test
  asserts the refusal survives unchanged rather than being swallowed or reworded.
- **Did NOT add a known-divergence ledger to the matrix suite**, though I built one for the duplicate
  before deleting it. Declaring those 6 divergences acceptable is [[TASK-222]]'s call, not mine; leaving
  the suite red is the honest signal that work is outstanding, matching the precedent TASK-218 set.

**Flagged, not fixed.**

- **[[TASK-222]]** — the 6 shapes still diverging on RavenDB, one of them (`ternary`) a **silent wrong
  answer**: 6 rows where C# says 1. `ExpressionNormalizer` already exists in Core to desugar exactly
  ternary / coalesce / arithmetic, and its doc comment claims native-LINQ backends never need it — now
  measurably false.
- I asserted, in this task and out loud, that Raven had no matrix suite. It does. Recorded in
  § Correction; the lesson is that `ls *.cs` is not a survey of a test project.

## Implementation plan

_Populated by `/tasks plan TASK-221` — leave empty until then._

## Progress log

- step 2 — picked. Option 1 (rewrite Contains -> In) taken, on measured evidence rather than the filed guess: .In() renders correctly negated and in conjunction, and the dangerous opposite-direction shape is distinguishable with a test ElasticSearch's ParseContains already makes.
- step 3 — verified, and the finding held with a correction: it is not the span binding (all spellings fail, not just the array one) and Raven has an `.In()` that works. Feasibility measured before committing to the rewrite: `.In()` renders correctly negated and in conjunction, and `x.Tags.Contains(const)` — the opposite direction — ALREADY works and therefore must not be touched.
- step 4 — layer: local to Birko.Data.RavenDB. `.In()` is a Raven API, so the rewriter cannot live in Core; it reuses Core's `SpanContains.UnwrapSpanConversion` and `PredicateScope.IsExplicitAllRows` rather than re-deriving either.
- step 5 — fix in Birko.Data.RavenDB/{Expressions/RavenSetMembership.cs (new), Stores/RavenDBStore.cs, Stores/AsyncRavenDBStore.cs (6 entry points), .projitems}; tests in Birko.Data.RavenDB.Tests/RavenSetMembershipTests.cs (new, 15, non-gated).
- step 5a — the LIVE run caught a defect every offline test passed over: the bulk `ReadCoreAsync` wiring had been inserted INSIDE `if (_documentStore == null) { return ...; }`, i.e. unreachable. The 15 non-gated tests all passed because they call the rewriter directly. Checked the Cosmos and MongoDB wirings for the same slip afterwards — both were correct, verified rather than assumed.
- step 6 — revert of the rewriter body: 6 of 51 failed, all fix-dependent = the four Every_portable_spelling_renders_an_IN cases plus The_negated_form_is_rewritten_and_negated and A_membership_test_beside_another_condition_keeps_both. Contract pins, passing either way: The_baseline_confirms_RQL_rendering_needs_no_server, the four Every_portable_spelling_would_throw_untranslated cases (they pin the DEFECT, and are what would fail first if Raven ever supported Contains natively — the signal to delete this rewriter), A_collection_FIELD_holding_a_constant_is_left_strictly_alone, A_string_Contains_keeps_Ravens_own_deliberate_refusal, and the unchanged/null pair. Live: the matrix suite went from throwing in SETUP to reporting 21 of 27 shapes OK.
- step 7 — respecced filter-expression-translation: RavenDB moves out of the "excluded" note into its own requirement, since it now has a rewrite of its own with different semantics from SpanContains.
- step 8 — closed done; f3631d3 (production) / 7815890 (tests). Merge gate: builds warning-clean (two CS8603 in the new tests were fixed, not suppressed at project level); no new cross-cutting pattern — this is the third instance of the existing "normalise a portable spelling per backend" rule, and CLAUDE.md was extended with what Raven taught rather than duplicated. security-review not triggered: no auth/crypto/secrets/user-input/new-dependency/endpoint surface; the all-rows rewrite drops a WHERE that Raven rejected anyway and reuses the same IsExplicitAllRows the destructive guards use, so it cannot widen a delete.
