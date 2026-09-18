---
id: TASK-212
parent: STORY-051
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P1
assignee: ai
picked-by: fix-next
created: 2026-08-14
depends-on: []
blocks: []
related: [TASK-137, TASK-109, TASK-141]
pr: 7c66862
github-issue: null
jira-key: null
findings: []
---

# A MongoDB `Delete(filter)` guards only a NULL filter — a filter that *reduces* to everything is not refused

## Context

Spawned by [[TASK-137]] (2026-08-14) while reviewing that fix's spec diff. TASK-137 closed the SQL instance of
"a filter that constrains nothing reaches a whole-collection write": an empty `NOT IN` rendered `1 = 1`, which
satisfied the SQL guard's rendered-clause test with a tautology, and `Delete(x => !empty.Contains(x.Col))`
emptied the table silently (measured: 0 of 3 rows left, no exception).

MongoDB has the same two doors and only half the guard. `MongoDBStore.Delete(Expression<Func<T,bool>>)` and
`Update(filter, PropertyUpdate<T>)` (and their `AsyncMongoDBStore` twins) call `RequireFilter(filter, …)` —
added by [[TASK-109]] for SH-M023 — which throws `ArgumentNullException` for a **null** filter and nothing
else. The non-null filter is then handed **straight to the driver**:

```csharp
RequireFilter(filter, "delete");
if (Collection == null) return;
Collection.DeleteMany(filter);       // ../Birko.Data.MongoDB/Stores/MongoDBStore.cs:268-279
```

So the shape TASK-137 spent its whole scope on — a non-null filter whose translation matches every document —
has no guard here at all. `WholeTableWriteException` is not referenced anywhere in `Birko.Data.MongoDB`
(grepped); it lives in `Birko.Data.SQL/Exceptions/`, which is itself worth questioning for a rule
§ Conventions states in backend-neutral terms.

**⚠ The mechanism is UNVERIFIED and must be measured before it is fixed.** TASK-137's own lesson is that a
plausible mechanism can be wrong in the load-bearing direction, and here the translation is done by the
MongoDB driver rather than by Birko's parser, so nothing in this repo determines the answer. What to measure,
in this order:

1. Does `MongoDB.Driver` translate `x => !empty.Contains(x.Field)` (empty collection) into `$nin: []`, and
   does `$nin: []` match every document? If it instead throws, or matches nothing, **there is no defect** and
   the honest close is a refutation on the record — see § Out of scope.
2. Same question for `x => true` and for a captured-true flag: the SQL side treats a one-node constant as the
   *deliberate* door (`DeleteAll()`), so establish what MongoDB currently does with it before deciding what it
   *should* do.
3. Whether `DeleteMany` with a match-everything filter differs observably from the `DeleteAll` path, i.e.
   whether a caller can already tell them apart.

### MEASURED 2026-08-15 (step 3) — the mechanism holds, and one measurement changes the fix's shape

No MongoDB is reachable here (port 27017 closed, `BIRKO_MONGO_HOST` unset), so the questions were answered
the way they *can* be answered offline: the driver renders a `FilterDefinition` to BSON with no connection.
`MongoDB.Driver` 3.2.0, filters built with `Builders<T>.Filter.Where(expr)`:

| predicate | rendered BSON | elements |
|---|---|---|
| `!empty.Contains(x.Amount)` | `{ "Amount" : { "$nin" : [] } }` | **1** |
| `empty.Contains(x.Amount)` | `{ "Amount" : { "$in" : [] } }` | 1 |
| `!some.Contains(x.Amount)` | `{ "Amount" : { "$nin" : [1, 5] } }` | 1 |
| `x => true` | `{ }` | **0** |
| `x => capturedTrue` | `{ }` | 0 |
| `x => 1 == 1` | `{ }` | 0 |
| `x => false` | `{ "_id" : { "$type" : -1 } }` | 1 |
| `x.Amount > 4` (control) | `{ "Amount" : { "$gt" : 4 } }` | 1 |
| `x.Amount > 4 \|\| !empty.Contains(x.Amount)` | `{ "$or" : [ { "Amount" : { "$gt" : 4 } }, { "Amount" : { "$nin" : [] } } ] }` | 1 |
| `FilterDefinition.Empty` (the match-everything baseline) | `{ }` | 0 |

**Answer to question 1 — and it kills option 2 of § Approach.** The defect shape renders a **one-element**
document, not an empty one. So "render the filter and refuse an empty document" would **not** catch it: it
looks like an ordinary field predicate. That is the exact structural parallel to the SQL instance, where
`1 = 1` was a non-empty `WHERE` and therefore satisfied a guard that tested whether anything was rendered
(TASK-137). The lesson repeats verbatim: **a scope guard must test what the statement MEANS, not whether
output was produced.**

**Answer to question 2.** `x => true`, a captured true flag and `x => 1 == 1` all render `{ }` — byte-identical
to `FilterDefinition.Empty`, i.e. unambiguously the whole collection. That matches the SQL side's treatment of
a one-node constant as the *deliberate* door, so it is **kept working**, not refused.

**Answer to question 3.** `DeleteMany({})` and the inherited `DeleteAll()` both remove every document; they
differ only in mechanism (one statement versus `AbstractBulkStore`'s read-then-loop). Not observably different
in outcome, so no caller can currently distinguish deliberate from accidental.

**What is NOT measured, stated plainly.** Whether `$nin: []` actually matches every document was **not
executed** — that needs a server. MongoDB documents `$nin` as selecting documents whose field value is not in
the given array (and documents lacking the field entirely), so an empty array excludes nothing and selects
everything. That is the documented semantics, not a measurement, and it is the one link in the chain taken on
authority.

**The guard does not depend on that link.** `!empty.Contains(x.Amount)` is true of every entity **by C#
semantics**, whatever the driver does downstream — so a caller who writes it has expressed "affect every
document" without saying so explicitly. Guarding the *expression* refuses the expressed intent and is correct
regardless of how Mongo evaluates `$nin: []`. Only the **severity** (is data being destroyed today?) rests on
the unexecuted link.

### Found by accident, out of scope, filed separately

Building the probe surfaced two serialization failures that have nothing to do with this task and are filed
as [[TASK-214]]: a model deriving `MongoDBModel` cannot be class-mapped at all
(`BsonSerializationException` — the `Guid` override collides with `AbstractModel.Guid`), and a model deriving
`AbstractModel` fails on `GuidSerializer ... GuidRepresentation is Unspecified`. Nothing in
`Birko.Data.MongoDB` registers a class map or sets a Guid representation. Not investigated further here.

## Approach

**Do not reach for `AbstractConnectorBase.IsAlwaysTrueCondition`.** It reduces a *Birko SQL condition tree*,
which this store never builds — it is not a portable predicate analyser and making it one would couple
`Birko.Data.MongoDB` to `Birko.Data.SQL`. Two candidate shapes, both to be costed after step 1 above:

1. **Guard on the expression, before the driver.** `DataBase.IsExplicitAllRows` already answers "is this a
   one-node constant" over a normalized `LambdaExpression` and lives in `Birko.Data.SQL` — so the reusable
   part is the `ExpressionNormalizer` funcletization in `Birko.Data.Core`, not the SQL wrapper. A portable
   "does this predicate reduce to a constant true" helper on the `Birko.Data.Core` side is the plausible home,
   and it would serve InMemory and any future backend that hands predicates to a driver.
2. **Guard on the translated filter**, by rendering the `FilterDefinition` and refusing an empty document.
   Backend-honest and catches driver behaviour the expression cannot predict, but it costs a render per call
   and is MongoDB-specific.

Whichever wins, the refusal must be `WholeTableWriteException` (or the same-family sibling) so
`catch (WholeTableWriteException)` keeps selecting it across backends, and it must **name the deliberate
door** — and that door has to be verified to open, per § SH-H037: check what MongoDB's equivalent of
`DeleteAll()` actually is before pointing a message at it.

## Acceptance criteria

- [x] The three questions in § Context are answered by measurement against a real MongoDB (or an honest
      statement that they could not be, with what was tried) — recorded in this file before any fix
- [x] If the defect holds: a non-null filter that matches every document is refused on
      `MongoDBStore.Delete/Update` and `AsyncMongoDBStore.DeleteAsync/UpdateAsync` — all **four** overrides,
      since TASK-109 already established that this store's overrides bypass the base guard one at a time
- [x] The deliberate whole-collection door exists, is named in the refusal message, and is **executed** by a
      test — not merely mentioned (the Redis lesson: a refusal pointing at a door that throws is a wall)
- [x] `x => true` keeps working as the explicit synonym if it works today; if it does not, that is recorded
      rather than quietly changed
- [x] Regression tests in `Birko.Data.MongoDB.Tests`, red-verified with the split reported as numbers, and
      contract pins named as pins
- [x] `/specs regen bulk-filter-operations` — the area already globs both MongoDB stores, so the diff is real
      evidence here (unlike the `AbstractConnectorBase.cs` gap noted in that spec's § Regen provenance)
- [x] If refuted: the evidence is written into this file and the task is cancelled, not silently dropped

## Out of scope

- The SQL instance — [[TASK-137]] closed it.
- ElasticSearch. `ElasticSearch.cs` renders an empty negated `Contains` as a `must_not MatchNoneQuery`, i.e.
  match-everything, and whether that reaches a destructive `UpdateByQuery`/`DeleteByQuery` unguarded is the
  same question again in a third backend. Deliberately not folded in: it is a different translation layer with
  a different guard story, and one measured backend per task is what kept TASK-137 honest. File it separately
  if the MongoDB answer confirms the pattern.
- Moving `WholeTableWriteException` out of `Birko.Data.SQL`. Worth doing if this fix needs it from a
  non-SQL project, but it is a public-API relocation and belongs in its own task with its own decision.

## Human test plan

**Resolved 2026-08-15: `N/A — fully covered by automated tests.`** The guard throws *before* any driver call
and before a connection is opened, so a server adds nothing a test cannot see: `MongoUnboundedFilterGuardTests`
exercises all four overrides plus every not-refused case offline, and `PredicateScopeTests` covers the
analyser directly. The one thing a live instance would add — confirming that `$nin: []` really removes every
document, i.e. the **severity** of what was happening before the fix, not the correctness of the fix — is
carried by [[TASK-214]]'s human test plan, which already requires starting a MongoDB and running the gated
suite. Duplicating it here would park this task on a step someone else owns.

## Implementation plan

_Populated by `/tasks plan TASK-212` — leave empty until then._

## Outcome

`store.Delete(x => !emptyList.Contains(x.Field))` on MongoDB reached `DeleteMany` **unrefused**. The four
overrides called `RequireFilter`, which refuses only a **null** filter, and then handed the expression to the
driver. A filter that is present but constrains nothing was nobody's case.

The guard now lives on the **expression**, in `AbstractBulkStore.RequireBoundedFilter` / its async twin,
backed by a new portable `Birko.Data.Expressions.PredicateScope`. It refuses when a predicate covers every
entity *unless* the caller said so explicitly with a constant `x => true`, which stays the documented
`DeleteAll()` synonym.

### Why the expression and not the rendered query — the measurement that decided it

§ Approach offered two shapes and preferred neither. Rendering the driver's filter offline settled it:
`x => !empty.Contains(x.Amount)` becomes `{ "Amount": { "$nin": [] } }` — a **one-element** document that
looks exactly like an ordinary field predicate while selecting every document. So option 2, "render the
filter and refuse an empty document", **would never have fired**. That is the same trap as the SQL instance,
where `1 = 1` was a non-empty `WHERE` and satisfied a guard testing whether anything had been rendered
(TASK-137). Guarding the expression also means the refusal does not depend on how any driver evaluates
`$nin: []` — `!empty.Contains(x)` is true of every entity by C# semantics, full stop.

### Step-6 split — 13 of 34, across two independent reverts

The suite has two halves that fail for different reasons, so one revert could not measure both.

| revert | suite | new | failed | pins |
|---|---|---|---|---|
| (a) the four MongoDB wirings removed | `MongoUnboundedFilterGuardTests` | 14 | **7** | 7 |
| (b) the analyser's `Contains` detection neutered | `PredicateScopeTests` | 20 | **6** | 14 |

**Fix-dependent (13).** (a) `Delete_`/`Update_`/`DeleteAsync_`/`UpdateAsync_with_an_empty_negated_Contains_is_refused`,
`An_OR_chain_containing_an_unbounded_term_is_refused`, `The_refusal_is_the_same_type_the_SQL_connectors_throw`,
`The_refusal_names_the_deliberate_door_and_not_a_SQL_one`. (b)
`An_empty_negated_Contains_covers_everything_but_is_not_explicit`,
`Both_the_instance_and_the_extension_Contains_forms_are_recognised`,
`An_OR_covers_everything_when_either_side_does`, `An_AND_covers_everything_only_when_both_sides_do`,
`Nested_grouping_is_respected`, `A_negated_always_false_term_covers_everything`.

**Contract pins — pins, not evidence, and one whole half is pins BY CONSTRUCTION.** Every "is NOT refused"
test (`An_explicit_constant_true_filter_...`, `A_captured_true_flag_...`, `A_bounded_filter_...`,
`A_NON_empty_negated_Contains_...`, `An_empty_UN_negated_Contains_...`, `An_AND_chain_with_one_bounded_term_...`,
`A_null_filter_still_raises_...`) asserts the guard does **not** over-fire — and revert (a) *removes* the
guard, so nothing can fire and they all pass trivially. They are the false-positive suite: they can only fail
if the fix is too aggressive, which is the risk this fix actually carries. Naming them as evidence would be
straightforwardly false. The same applies to the precision cases in (b): `A_string_Contains_...`,
`A_per_entity_collection_...`, `A_null_collection_...`, `A_collection_that_cannot_be_evaluated_offline_...`,
`A_bare_boolean_column_...`, plus the explicit-door and constant cases.

### The test suite found a real gap in the fix

`Both_the_instance_and_the_extension_Contains_forms_are_recognised` **failed on first run**: on .NET 9+ an
array binds `MemoryExtensions.Contains(ReadOnlySpan<T>, T, IEqualityComparer<T>?)`, so the collection arrives
wrapped in an implicit `ReadOnlySpan` conversion — a ref struct, which cannot be boxed, so evaluating it threw
and the analyser silently declined. **Every array-typed caller would have been left unguarded**, and arrays
are the common spelling. Fixed with `UnwrapSpanConversion`. The SQL parser was bitten by the same overload
change from the other direction (its `IsNonOperandArgument` comment records it); this is the second time that
one C# language/runtime change has quietly reshaped this family's expression handling.

### Judgement calls, and the stricter option rejected

- **The relocation the task deferred was done, because the task's own condition for it was met.**
  § Out of scope said moving `WholeTableWriteException` out of `Birko.Data.SQL` was "worth doing **if this fix
  needs it**" but belonged in its own task. It does need it: `Birko.Data.MongoDB` does not reference
  `Birko.Data.SQL`, so without the move the refusal could not be the same type and one `catch` would stop
  selecting it across backends — the criterion this task was written around. It is a **file move only**: the
  namespace was already the backend-neutral `Birko.Data.Exceptions`, `Birko.Data.Core/Exceptions/` already
  holds `StoreException` in that namespace, and **no project imports `Birko.Data.SQL.projitems` without
  `Birko.Data.Core.projitems`** (checked across `Framework.Tests` and `Consumers`). Zero source edits
  anywhere. Flagged to the user rather than buried.
- **A second constructor instead of rewording the existing message.** The shipped message says "without a
  WHERE clause ... to drop the table use `Destroy()`", which is meaningless for a document store and would
  send a reader hunting for an API that does not exist. Rewriting it would have broken
  `AddRequiredWhere_ErrorNamesTheExplicitAlternative`, which asserts that wording. A backend-neutral overload
  keeps both true, and the new test asserts the MongoDB message contains neither `WHERE` nor `Destroy()`.
- **The guard is defined for every backend but WIRED only into MongoDB.** `RequireBoundedFilter` sits on the
  shared bulk bases, so InMemory and ElasticSearch can adopt it in one line — but each needs its own
  measurement of whether the shape actually reaches a destructive path there, which is what kept this task
  honest. Wiring them blind would be the "widening scope silently" this loop exists to avoid. Filed as
  [[TASK-215]].
- **Narrow analyser, deliberately.** `PredicateScope` answers `false` when it cannot prove a predicate
  unbounded — an unevaluatable collection, a per-entity collection, a null collection, any string `Contains`.
  A refusal that fires on a predicate which *does* constrain something breaks working code, which is worse
  than the hole being closed; five of the twenty unit cases exist only to pin that direction.

### Flagged and not fixed

- **[[TASK-214]] (spawned, P1) — `MongoDBModel` cannot be class-mapped by the driver at all.** Found while
  building the probe: `BsonSerializationException`, because the `Guid` override collides with
  `AbstractModel.Guid`; and an `AbstractModel`-derived type fails separately on
  `GuidRepresentation is Unspecified`. Nothing in `Birko.Data.MongoDB` registers a class map or sets a
  representation. **Filed with the mechanism marked unconfirmed** — the offline registry may not be what the
  store uses at runtime. The alarming part is why it could be true unnoticed: the only suite that would
  exercise serialization is gated on `BIRKO_MONGO_HOST` and no-ops without it, which is the TASK-209 shape.
- **[[TASK-215]] (spawned, P2) — wire the same guard into InMemory and ElasticSearch**, each after its own
  measurement.
- **The unexecuted link.** Whether `$nin: []` really matches every document was not run against a server;
  it rests on MongoDB's documented `$nin` semantics. This affects the *severity* claim, not the fix.
- **`.map.yml` glob repaired, not redesigned.** Moving the exception file dangled this area's glob at its old
  path; the path was corrected in place with a comment. The area's membership is unchanged. Also recorded:
  `PredicateScope.cs` falls inside `filter-expression-translation`'s `Expressions/*.cs` glob, so that area now
  carries a short cross-reference to where the behaviour is actually specced, rather than leaving a new public
  class with no spec presence.

### Follow-up landed after close — the duplication this task introduced (98c0a74)

The close left **two identical implementations of `IsExplicitAllRows`** — `DataBase.IsExplicitAllRows`
(Birko.Data.SQL) and `PredicateScope.IsExplicitAllRows` (Birko.Data.Core) — same signature, same
`LambdaExpression` input, same normalizer call, same body. The second appeared because
`Birko.Data.Stores` cannot see the first; consolidating was considered during the work and dropped to avoid
widening the blast radius mid-task, **and then not written down**, which is how it survived the merge gate.

Worse than the usual duplication because **both copies feed destructive guards**: the SQL funnels ask one
whether a whole-table DELETE was deliberate, `RequireBoundedFilter` asks the other. Two definitions of the
deliberate door, free to drift, on the paths where drift destroys data — exactly what § Conventions'
one-producer rule is for, and cited three times in this very task file while the tree was left in the state
it warns about.

`DataBase.IsExplicitAllRows` now forwards to `PredicateScope.IsExplicitAllRows`. The public overload stays
(public API, and it reads naturally beside the SQL filter surface) but holds no logic. Behaviour unchanged:
the 8 `IsExplicitAllRows` cases in `DestructiveFilterGuardTests` exercise the delegation, and 484 + 194 + 48
+ 69 plus nine further SQL-touching suites are green.

**Not consolidated, and deliberately so:** `AbstractConnectorBase.IsAlwaysTrueCondition` (parsed SQL
condition tree, post-translation) and `PredicateScope.ReducesToAllRows` (LINQ expression, pre-translation)
encode the same *algebra* over different inputs and neither can do the other's job. That remains a
maintenance hazard — a new "means everything" shape must be taught to both — and is recorded here rather
than papered over.

## Progress log

- step 2 — picked; ranked above the 45 per-area triage batches on severity key 1 (an unguarded
  whole-collection destructive write beats a mixed bag of unverified claims) and on key 4 (one guard across
  four overrides, versus "confirm or refute 9-36 findings" which cannot complete in one session).
  **Deferred twice before on the premise that it needs a live MongoDB — that premise is now being tested
  rather than repeated.** No server is reachable (port 27017 closed, `BIRKO_MONGO_HOST` unset), but the
  driver renders a filter to BSON with no connection, which is what establishes reachability; and a guard
  placed on the *expression* is sound whatever the driver does downstream. A live server is needed only to
  confirm blast radius, and criterion 1 already permits an honest statement of what could not be measured.
  Step 3 may still refute this outright, in which case the deliverable is the refutation.
- step 3 — verified: **holds, measured offline** (§ MEASURED). The driver renders the defect shape as
  `{ "Amount": { "$nin": [] } }` — a **one-element** document, so § Approach's option 2 ("refuse an empty
  rendered filter") is now known to be **insufficient** and is rejected; option 1 (guard the expression) is
  the fix. `x => true` renders `{ }` and stays the deliberate door. The one unexecuted link (`$nin: []`
  matching every document) is recorded as documented-not-measured, and the guard is deliberately built so it
  does not depend on it. Spawned [[TASK-214]] for two unrelated serialization failures found while probing.
- step 4 — layer: **local**, but not where the task assumed. The guard belongs beside the existing
  `RequireFilter` in `Birko.Data.Stores` (portable, one place a reader looks, reusable by every backend that
  overrides the public destructive methods), and the predicate analysis belongs in `Birko.Data.Core`
  alongside `ExpressionNormalizer`. Only the *wiring* is MongoDB-local.
- step 5 — fix in `../Birko.Data.Core/Expressions/PredicateScope.cs` (new),
  `../Birko.Data.Core/Exceptions/WholeTableWriteException.cs` (**moved** from `Birko.Data.SQL`, plus a
  backend-neutral constructor), `../Birko.Data.Stores/AbstractBulkStore.cs` + `AbstractAsyncBulkStore.cs`
  (new `RequireBoundedFilter`), and the four MongoDB overrides. Tests in
  `Birko.Data.MongoDB.Tests/Stores/MongoUnboundedFilterGuardTests.cs` (new, 14) and
  `Birko.Data.Core.Tests/PredicateScopeTests.cs` (new, 20). Suites green: **69/69** and **48/48**, both under
  200 ms — still offline. Blast radius cleared against **25** suites, and verified that no project imports
  `Birko.Data.SQL.projitems` without `Birko.Data.Core.projitems`, so the exception move breaks nothing.
- step 6 — two reverts, because the suite has two independent halves. **(a)** the four MongoDB wirings
  removed -> **7 of 14** in the Mongo suite; **(b)** the analyser's `Contains` detection neutered ->
  **6 of 20** in the Core suite. Combined **13 of 34**. Both restored and re-verified green. Fix-dependent
  and pins named in § Outcome, including why the "is NOT refused" half cannot witness revert (a) by
  construction.
- step 8 — closed done; production 189b119 (Core) / 71001d4 (SQL) / e37bebf (Stores) / 7c66862 (MongoDB), tests 4242ed5 + 719e160.
- step 8 (follow-up) — consolidated the duplicate `IsExplicitAllRows`; 98c0a74.
