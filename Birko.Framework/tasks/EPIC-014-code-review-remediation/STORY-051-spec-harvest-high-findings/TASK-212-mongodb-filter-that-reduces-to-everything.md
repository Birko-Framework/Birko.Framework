---
id: TASK-212
parent: STORY-051
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P1
assignee: ai
created: 2026-08-14
depends-on: []
blocks: []
related: [TASK-137, TASK-109, TASK-141]
pr: null
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

- [ ] The three questions in § Context are answered by measurement against a real MongoDB (or an honest
      statement that they could not be, with what was tried) — recorded in this file before any fix
- [ ] If the defect holds: a non-null filter that matches every document is refused on
      `MongoDBStore.Delete/Update` and `AsyncMongoDBStore.DeleteAsync/UpdateAsync` — all **four** overrides,
      since TASK-109 already established that this store's overrides bypass the base guard one at a time
- [ ] The deliberate whole-collection door exists, is named in the refusal message, and is **executed** by a
      test — not merely mentioned (the Redis lesson: a refusal pointing at a door that throws is a wall)
- [ ] `x => true` keeps working as the explicit synonym if it works today; if it does not, that is recorded
      rather than quietly changed
- [ ] Regression tests in `Birko.Data.MongoDB.Tests`, red-verified with the split reported as numbers, and
      contract pins named as pins
- [ ] `/specs regen bulk-filter-operations` — the area already globs both MongoDB stores, so the diff is real
      evidence here (unlike the `AbstractConnectorBase.cs` gap noted in that spec's § Regen provenance)
- [ ] If refuted: the evidence is written into this file and the task is cancelled, not silently dropped

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

Resolve before `/tasks close` — it depends on whether a real MongoDB is reachable in this environment. If the
measurement in § Context has to run against a live instance, the same steps are the human plan; if it can run
against the driver's expression translation alone, expect
`N/A — fully covered by automated tests`, written out with that reason.

## Implementation plan

_Populated by `/tasks plan TASK-212` — leave empty until then._
