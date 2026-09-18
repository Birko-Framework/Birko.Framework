---
id: TASK-215
parent: STORY-051
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P2
assignee: ai
picked-by: fix-next
created: 2026-08-15
depends-on: []
blocks: []
related: [TASK-212, TASK-109]
pr: c828ef1
github-issue: null
jira-key: null
findings: []
---

# Wire `RequireBoundedFilter` into the base wrappers, InMemory and ElasticSearch

> Retitled at close. The task was filed as "the InMemory and ElasticSearch destructive **overrides**";
> step 3 measured that the base's own six wrappers were the root cause and the scope was corrected
> before any code was written (§ Context). The filename keeps the original slug — the id is what is
> referenced.

## Context

Spawned by [[TASK-212]] (2026-08-15). That task built the portable guard —
`AbstractBulkStore<T>.RequireBoundedFilter` / `AbstractAsyncBulkStore<T>.RequireBoundedFilter`, backed by
`Birko.Data.Expressions.PredicateScope` — which refuses a filter-based destructive operation whose predicate
covers every entity, unless every entity was asked for explicitly. It **wired it into MongoDB's four
overrides only**.

The guard is already available to every backend: it sits on the shared bulk bases, so adopting it is one line
per override. What is *not* established is whether the shape actually reaches a destructive path on the other
two backends, and TASK-212's discipline was one measured backend per task — wiring the rest blind would be
exactly the silent scope-widening the drain loop exists to avoid.

The same SH-M023 sweep that found MongoDB's four overrides also found these:

- **InMemory** — `AbstractInMemoryStore<T>` / `AbstractAsyncInMemoryStore<T>` override the public
  `Delete(filter)`. These are read-then-loop over an in-process dictionary, evaluating the predicate as a
  **C# delegate**, so `!empty.Contains(x.Field)` is true for every entity by definition — no translation
  layer is involved and no measurement of a driver is needed. This one is provable by inspection and is the
  cheaper half.
- **ElasticSearch** — four public overrides. `ElasticSearch.cs` renders an empty negated `Contains` as
  `must_not MatchNoneQuery`, i.e. match-everything, and whether that reaches a destructive
  `DeleteByQuery` / `UpdateByQuery` unrefused is the open question. Needs the same offline-rendering
  treatment TASK-212 used for MongoDB (build the query, inspect it) before any wiring.

### MEASURED 2026-08-16 (step 3) — the finding holds and is WIDER than filed: the base wrappers are the hole

Both halves were measured before any code changed. The InMemory half held exactly as written. The
ElasticSearch half held too. But the probe written to establish the InMemory half found the actual root
cause, which this task did not name: **`AbstractBulkStore`'s own filter-based destructive wrappers never
call `RequireBoundedFilter` either.** TASK-212 added the helper to the base and wired it into MongoDB's
four overrides — it never wired it into the base's own six.

**InMemory** (`Birko.Data.InMemory.Tests`, 3 seeded rows, `var empty = new List<int>()`):

| call | override? | result |
|---|---|---|
| `Delete(x => !empty.Contains(x.Amount))` | InMemory override | no throw, **0 of 3 rows left** |
| `DeleteAsync(…)` | InMemory override | no throw, **0 of 3 rows left** |
| `Update(filter, PropertyUpdate)` | **base** | no throw, **3 of 3 clobbered** |
| `Update(filter, Action)` | **base** | no throw, **3 of 3 clobbered** |
| `UpdateAsync(filter, PropertyUpdate)` | **base** | no throw, **3 of 3 clobbered** |
| `UpdateAsync(filter, Action)` | **base** | no throw, **3 of 3 clobbered** |
| `Delete(x => !emptyArray.Contains(…))` (array form) | InMemory override | no throw, **0 of 3 rows left** |
| `Delete(x => !some.Contains(…))` (non-empty, control) | InMemory override | 1 of 3 left — correct |
| `Delete(x => empty.Contains(…))` (un-negated, control) | InMemory override | 3 of 3 left — correct |

**The base path, measured on a backend this task never mentions.** `Birko.Data.JSON` overrides *none* of
the six, so it exercises `AbstractBulkStore` directly: `Delete(x => !empty.Contains(x.Value))` left
**0 of 3 rows**, no exception. `Delete(x => true)` and `DeleteAll()` also emptied it (the explicit doors,
correct), and `Delete(x => x.Value > 10)` left 1 of 3 (bounded, correct). So the defect is **not**
InMemory-specific: it is live on every portable backend that does not override — JSON, XML, RavenDB,
CosmosDB, InfluxDB — and on the four *non*-overridden `Update` paths of InMemory, MongoDB and
ElasticSearch as well.

**ElasticSearch** — rendered offline through `ParseRequiredFilterQuery`, no cluster, inner query type
unwrapped from the `QueryContainer`:

| predicate | rendered query |
|---|---|
| `!empty.Contains(x.Count)` | `bool { must_not: [ **match_none** ] }` |
| `!emptyArray.Contains(x.Count)` | `bool { must_not: [ match_none ] }` |
| `!some.Contains(x.Count)` (control) | `bool { must_not: [ **terms** ] }` |
| `empty.Contains(x.Count)` | `match_none` |
| `x => true` | `match_all` |
| `x => false` | `match_none` |
| `x.Count > 4` (control) | `numeric_range` |

`must_not: [match_none]` selects **every** document, and it reaches `DeleteByQuery` / `UpdateByQuery`
with no refusal — `ParseRequiredFilterQuery`'s guard (CR-H047) is a *null* check, and a query was
produced. This is the third instance of the family, and the tell is the same one every time: the defect
shape and the legitimate shape are **the same query structure**, differing only in the inner type
(`match_none` vs `terms`) — exactly as `$nin: []` looked like an ordinary field predicate on MongoDB and
`1 = 1` looked like an ordinary `WHERE` on SQL. A guard on the rendered query would have to enumerate
"ways to spell everything" per backend; the expression says it once.

### Scope corrected before any code was written

The task asked for InMemory + ElasticSearch. The measurement says that is **half a fix**: wiring
InMemory's two `Delete` overrides while leaving its four inherited `Update` paths wiping every row would
ship a store whose `Delete` refuses and whose `Update` does not. So this task now covers **all twelve
sites**: the six base wrappers (the root cause, which fixes JSON/XML/RavenDB/CosmosDB/InfluxDB by
construction), InMemory's two overrides and ElasticSearch's four. MongoDB's four were done by TASK-212.
Not a widening of theme — one root cause, one guard call, measured on three backends.

## Approach

1. **InMemory first** — it needs no backend measurement, only a test that the shape currently deletes
   everything and is refused afterwards.
2. **ElasticSearch second**, and only after establishing what the destructive paths actually send. If the
   client rejects the shape, or the store already refuses it elsewhere, record that and wire nothing.
3. Reuse `RequireBoundedFilter` exactly as MongoDB does — call it immediately after the existing
   `RequireFilter`, so the null case and the unbounded case stay two distinct, separately-named refusals.

## Acceptance criteria

- [x] **The six base wrappers** on `AbstractBulkStore` / `AbstractAsyncBulkStore` refuse a
      present-but-unbounded filter — added at step 3 after measuring that they, not the overrides, are the
      root cause and that JSON (which overrides nothing) empties itself today
- [x] InMemory's overrides refuse a present-but-unbounded filter, with a test that fails on revert
- [x] ElasticSearch's behaviour is **measured and recorded** before any wiring; if the shape cannot reach a
      destructive path there, that is written up and no guard is added
- [x] Every backend touched keeps its explicit door working (`x => true`, `DeleteAll()`), executed by a test
      rather than assumed (§ SH-H037)
- [x] The false-positive direction is covered per backend: a bounded filter, a non-empty negated `Contains`
      and an empty un-negated `Contains` are all still allowed
- [x] Red-verified with the split as numbers; pins named as pins
- [x] `/specs regen bulk-filter-operations` — the area globs the InMemory and ElasticSearch stores

## Out of scope

- Changing `PredicateScope` itself. If a backend needs a shape it does not model, that is a change to the
  analyser with its own blast-radius measurement, not a local workaround.
- Converting these public overrides to `protected *Core` methods, which is the real fix for why the guard has
  to be repeated at all. Pre-existing, noted in the spec, and its own work.

## Human test plan

`N/A — fully covered by automated tests.` Both backends run in-process (InMemory entirely; ElasticSearch's
query construction can be asserted without a cluster), so there is nothing a human can check that a test
cannot.

## Implementation plan

_Populated by `/tasks plan TASK-215` — leave empty until then._

## Outcome

**Commits** — six repos, production before tests (`pr:` points at the first):
`Birko.Data.Stores` c828ef1 · `Birko.Data.Core` 1ee0793 · `Birko.Data.InMemory` 89e3ed5 ·
`Birko.Data.ElasticSearch` 9b523e2 · `…InMemory.Tests` 3a901be · `…JSON.Tests` bc4ae90 ·
`…ElasticSearch.Tests` 560b653.


`store.Delete(x => !emptyList.Contains(x.Field))` emptied the store, silently, on every portable backend —
not just the two this task named. The filter is not null, so `RequireFilter` passed it; the predicate is
compiled and run as a C# delegate, so it matched every entity; and because the deletes it issues are
per-row and each carries its own key, nothing downstream could ever have reported it. Measured before the
fix: **0 of 3 rows left**, no exception, no log entry.

`RequireBoundedFilter` — the guard TASK-212 built and wired into MongoDB only — is now called by all
twelve destructive filter paths: the **six base wrappers** on `AbstractBulkStore` / `AbstractAsyncBulkStore`,
InMemory's two overrides, and ElasticSearch's four.

### The task asked for two backends; the root cause was one layer up

The probe written to confirm the InMemory half showed the base's own `Update(filter, …)` wrappers — which
InMemory does *not* override — rewriting **3 of 3** rows. Re-measured on `JsonStore`, which overrides none
of the six: `Delete` left **0 of 3**. So the hole was in `Birko.Data.Stores`, and JSON, XML, RavenDB,
CosmosDB and InfluxDB all inherited it. Wiring only the two named backends would have shipped a store whose
`Delete` refuses beside an `Update` that wipes — which is why the scope was corrected in § Context, with an
acceptance row added, **before** any code was written rather than justified afterwards.

### ElasticSearch: measured, and the reason the guard cannot live on the query

`!empty.Contains(x.Count)` renders `bool { must_not: [ match_none ] }` — which selects every document —
while the legitimate `!some.Contains(x.Count)` renders `bool { must_not: [ terms ] }`. **Same structure,
different inner type.** CR-H047's `ParseRequiredFilterQuery` guard is a *null* check and a query was
produced, so it never fired. That is the third time this family has arrived disguised as ordinary output:
SQL's `1 = 1` satisfied a guard testing whether anything was rendered (TASK-137), MongoDB's `$nin: []` is a
one-element document indistinguishable from a field predicate (TASK-212). Guarding the expression states it
once for all three, and cannot be defeated by a translation nobody anticipated.

### Step-6 split — 18 of 34, plus a second isolating revert

| revert | suite | new | failed | pins |
|---|---|---|---|---|
| whole fix | `Birko.Data.InMemory.Tests` | 16 | **10** | 6 |
| whole fix | `Birko.Data.JSON.Tests` | 5 | **2** | 3 |
| whole fix | `Birko.Data.ElasticSearch.Tests` | 13 | **6** | 7 |
| door name only | `Birko.Data.InMemory.Tests` | — | **2 of 69** | — |

**Fix-dependent (18).** InMemory: `UnboundedFilter_Delete_IsRefused_AndKeepsEveryRow`,
`…_DeleteAsync_…`, `…_AsAnEmptyArray_IsRefusedToo`, `…_UpdateWithPropertyUpdate_…`, `…_UpdateWithAction_…`,
`…_UpdateAsyncWithPropertyUpdate_…`, `…_UpdateAsyncWithAction_…`, `TheRefusal_NamesADoorThatWorks_Sync`,
`TheRefusal_NamesADoorThatWorks_Async`, `TheRefusal_NamesTheAsyncUpdateDoor_AndItWorks`. JSON:
`UnboundedFilter_Delete_IsRefused_AndKeepsEveryRow`, `UnboundedFilter_Update_IsRefused_AndChangesNothing`.
ES: the four `UnboundedFilter_*_IsRefused`, `…_AsAnEmptyArray_IsRefusedToo`,
`TheRefusal_HappensBeforeTheIndexIsTouched`.

**Contract pins — these passed on revert and are NOT evidence for this fix (16).** They pin the
false-positive direction and the escape hatch, which is what they are for: InMemory's two
`AnExplicitTruePredicate_…`, `ANonEmptyNegatedContains_IsBounded_AndStillDeletes`,
`AnEmptyUnNegatedContains_MatchesNothing_AndIsNotRefused`, `AnOrdinaryBoundedFilter_IsUntouched`,
`APerEntityCollection_IsNotClaimedUnbounded`; JSON's `TheExplicitDoors_StillEmptyTheStore`,
`ABoundedFilter_StillDeletesExactlyItsRows`, `ANonEmptyNegatedContains_IsNotRefused`; ES's
`TheDefectShape_RendersMatchEverything_…`, `AnExplicitTruePredicate_IsNotRefused`, `DeleteAll_IsNotRefused`,
`ANonEmptyNegatedContains_IsNotRefused`, `AnEmptyUnNegatedContains_IsNotRefused`,
`AnOrdinaryFilter_IsNotRefused`, `ANullFilter_StillGetsTheNullRefusal_NotTheScopeOne`.

### Judgement calls

- **Guarded the base as well as the overrides, rather than only what was filed.** The stricter reading —
  stay inside the task's two named backends and spawn the base as its own work — was rejected because the
  measurement showed the two halves are not separable: InMemory's *own* `Update` paths are the base's, so
  a task-scoped fix would leave the very store it was about half-guarded. Blast radius was measured rather
  than argued: **35 suites, ~2,100 tests, all green**, before the change was kept.
- **Guarded both `Update(filter, PropertyUpdate)` and `Update(filter, Action)`** even though the first
  delegates to the second, mirroring the existing `RequireFilter` placement. The cost is one extra
  analyser pass on the delegating path; the benefit is that a backend overriding only one of them (which
  is exactly what ES and MongoDB do) still inherits the guard on the other.
- **Fixed the async refusal's door name, which was in scope by § SH-H037 and nothing else.** The message
  named `DeleteAll()`, which an async store does not have — a caller following the refusal would hit a
  compile error. Shipped by TASK-212 and not filed by anyone. It gets its own isolating revert above
  rather than hiding inside the main one.
- **The guard runs before `EnsureInitialized()` on ElasticSearch, which reorders one untested
  combination.** CR-L111 deliberately made init the first statement so an already-cancelled token is
  observed; now an unbounded filter *plus* a cancelled token throws `WholeTableWriteException` rather than
  `OperationCanceledException`. Accepted deliberately: a safety refusal must not depend on the store being
  initialisable, or it stops firing exactly when a cluster is unreachable — and the operation does not run
  under either exception. No existing test covers the combination (ES suite 129/129 green).
- **Kept the double guard on `Update(filter, PropertyUpdate)` → `Update(filter, Action)`, and it has a
  cost.** `PredicateScope` evaluates the collection operand, so a *side-effecting* operand is now evaluated
  three times instead of once on the base path. Kept anyway, because each of the two guards covers a
  distinct partial-override case and the placement mirrors the existing `RequireFilter`; recorded in
  § Conventions rather than left for someone to rediscover as a performance surprise.
- **Did not convert the public overrides to `*Core`.** That is the real reason the guard has to be
  repeated at all, and it stays out of scope: it is a behaviour change to shipped stores across three
  backends, already tracked as TASK-143 with its own decision to make.

### Flagged, not fixed

- **`.map.yml` under-coverage, fourth instance.** ElasticSearch's overrides were reachable by no glob in
  `bulk-filter-operations`, so the regen for this very fix would have been blind to them. Added, and noted
  in the spec's § Regen provenance. The older `AbstractConnectorBase.cs` gap that the file already
  documents twice is untouched and still waiting on **TASK-208**.
- **The ES assertions cannot reach a cluster.** The guard runs before `EnsureInitialized()`, which is what
  makes the tests meaningful offline — the refusal holds whether or not a cluster is up. What is *not*
  covered here is that `must_not: [match_none]` really deletes every document on a live ElasticSearch;
  that is the severity of the pre-fix behaviour, not the correctness of the fix, and it rests on the
  documented semantics of `match_none` rather than on a measurement.

## Progress log

- step 2 — picked; ranked above TASK-214 (MongoDBModel unserializable) because that one fails *loudly*
  (`BsonSerializationException`) and its first acceptance step demands a live MongoDB server, which this
  environment does not have — it cannot finish inside one session. This one is silent data loss and is
  self-contained.
- step 3 — verified: **held, and rescoped wider.** InMemory's two `Delete` overrides wipe (0/3), and the
  probe additionally found the *base* wrappers unguarded — measured on JSON, which overrides none of them
  and also emptied itself (0/3). ElasticSearch renders `bool { must_not: [match_none] }`, structurally
  identical to a legitimate `must_not: [terms]`. Scope corrected to 12 sites and an acceptance row added
  for the base, both recorded in § Context before writing code.
- step 4 — layer: local. Every site is in this framework; the root cause is `Birko.Data.Stores`, the two
  shared bulk bases, which is the most upstream point that can hold the fix.
- step 5 — fix in `AbstractBulkStore.cs` + `AbstractAsyncBulkStore.cs` (6 base wrappers),
  `AbstractInMemoryStore.cs` + `AbstractAsyncInMemoryStore.cs` (2 overrides),
  `ElasticSearchStore.cs` + `AsyncElasticSearchStore.cs` (4 overrides), `WholeTableWriteException.cs`
  (door name); tests in `PortableUnboundedFilterGuardTests.cs` (16),
  `BaseBulkUnboundedFilterGuardTests.cs` (5, JSON), `UnboundedFilterGuardTests.cs` (13, ES).
  Blast radius cleared across **35 suites / ~2,100 tests**, all green.
- step 6 — reverted fix: **18 of 34 failed** (InMemory 10/16, JSON 2/5, ES 6/13). Second targeted revert
  of the door-name change alone: **2 of 69** in the InMemory suite. Fix-dependent and pins named in
  § Outcome.
- step 7 — respecced `bulk-filter-operations`. Requirements changed: *"A present filter that constrains
  nothing is refused, on the expression"* (now mandates the six **base** wrappers, and mandates the named
  door be one the caller can take); *"A backend overriding the public destructive methods repeats the
  guard"* (its ElasticSearch scenario asserted the old behaviour — retitled from *"already covered by
  their own filter boundary"* to *"covers the null case but not the scope case"*). Four scenarios added
  (base wrappers on JSON, partial-override coverage, ES's four overrides, ES's rendering). `.map.yml`
  edited to add the two ElasticSearch store files — they were reachable by no glob in this area, the
  fourth instance of the under-coverage that file already documents twice.
