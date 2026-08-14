---
id: TASK-141
parent: STORY-051
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P2
assignee: ai
picked-by: fix-next
created: 2026-08-06
depends-on: []
blocks: []
# findings: ids this task remediates, from a review/audit/spec-harvest pass (CR-* SEC-* SH-* VC-*)
findings: [SH-M023]
pr: b86bc1c
github-issue: null
jira-key: null
---

# MongoDB's four null-filter guards have no regression test

## Context

Spawned from [[TASK-109]]'s close-gate review (2026-08-06). TASK-109 closed SH-H002/SH-M023 by refusing a
destructive write that would target every row. Part of that fix was a **sweep**: 10 stores across 3 backends
override the *public* `Delete(filter)` / `Update(filter, …)` rather than the `protected *Core` methods, so
they bypass `AbstractBulkStore`'s guard entirely and must repeat it.

The sweep's coverage is uneven, and only the untested part is left:

| Backend | Overrides | Guard | Test |
|---|---|---|---|
| ElasticSearch | 4 | `ParseRequiredFilterQuery` (CR-H047) | yes, pre-existing |
| InMemory | 2 | `RequireFilter` (`4f680b7`) | yes — `Birko.Data.InMemory.Tests@86df89c`, 6 of them fix-dependent |
| **MongoDB** | **4** | `RequireFilter` (`88f96ee`) | **none** |

`Birko.Data.MongoDB.Tests` contains no assertion touching the guard — the fix rests on four one-line calls
verified by inspection only.

**Why this is worth a test rather than a shrug.** The InMemory half of this same sweep was *discovered* by
tests: 6 of TASK-109's new portable tests failed against a base class that was already correct, because
`AbstractInMemoryStore` overrode the public `Delete` and bypassed the guard. The identical mechanism in
MongoDB was then found by grepping for the pattern, not by a failing test — so if a future refactor drops one
of those four `RequireFilter` lines, nothing fails. A null filter there reaches the driver as an empty
predicate, i.e. `DeleteMany` over the whole collection.

**A live backend is not required.** `RequireFilter` throws *before* any driver call, so the null-filter case
is assertable with no MongoDB server — which is why this is cheap and why the existing env-gated
`FilterMatrixLiveTests` pattern does not apply. Constructing the store may need a settings object but not a
connection; if construction turns out to demand a live client, say so in the task and gate only that part.

## Acceptance criteria

- [x] `Birko.Data.MongoDB.Tests` asserts `ArgumentNullException` for a null filter on **all four** overrides:
      `MongoDBStore.Delete(filter)`, `MongoDBStore.Update(filter, …)`, `AsyncMongoDBStore.DeleteAsync(filter, …)`,
      `AsyncMongoDBStore.UpdateAsync(filter, …)`
- [x] The tests run **without a live MongoDB** — no env gate, no skip; if store construction forces a client,
      that specific obstacle is recorded and only it is gated
- [x] Each test names `SH-M023` and states the mechanism (an override of the *public* method bypasses
      `AbstractBulkStore`'s guard, so the guard is repeated and must stay repeated)
- [x] **Red-verified**: removing each `RequireFilter` call fails its test. Report the split as numbers, and
      name any test that passes either way as a contract pin rather than as evidence
- [x] A deliberate all-rows delete/update on the MongoDB stores still works (the guard removed the accidental
      case, not the intentional one) — or, if those stores expose no `*All` door, that gap is recorded here
      and filed rather than silently accepted

## Out of scope

- **Converting the 10 public overrides to `protected *Core`.** That is the actual correct fix — the family
  convention exists *precisely* so the base can enforce invariants — but it changes behaviour in 3 backends
  and needs its own task and its own decision. TASK-109 chose the contained repeat deliberately; this task
  only tests what shipped.
- ElasticSearch's four overrides — already guarded and covered via CR-H047.
- InMemory's two — covered by `Birko.Data.InMemory.Tests@86df89c`.
- Any live-backend MongoDB behaviour (that the `DeleteMany` actually deletes, ordering, sessions). This is a
  guard test, not a driver test.
- **The two vulnerable transitive packages this task's close gate surfaced** — `Snappier 1.0.0` (NU1903,
  high) and `SharpCompress 0.30.1` (NU1902, moderate), both via `MongoDB.Driver 3.2.0`. Pre-existing, in the
  shared `.projitems` so every consumer inherits them, and reported by nothing because only
  `verify-conventions` check 1 builds with `-warnaserror`. Spawned as [[TASK-210]] rather than folded in.

## Outcome

**What was missing.** MongoDB's `MongoDBStore` and `AsyncMongoDBStore` override the *public*
`Delete(filter)` / `Update(filter, …)` instead of the `protected *Core` methods, so they never reach
`AbstractBulkStore`'s null-filter guard and have to repeat it themselves. TASK-109 added those four
`RequireFilter` calls and no test ever executed one — a refactor dropping any of them would have failed
nothing, and a null filter on that path reaches the driver as an empty predicate, i.e. `DeleteMany` over the
whole collection. Ten tests now execute all four, plus the refusal message and the all-rows escape hatch.
**No production change** — this task tests what shipped.

**Step-6 split: 6 of 10** with all four guards removed, and then **each guard removed individually**, which
is what criterion 4 actually asks for. Every line maps to exactly its own test (mapping in the progress log
above): no guard is untested and no test is redundant. The four `*All is not refused` tests are **contract
pins, not evidence** — removing a guard cannot make the all-rows doors throw.

**The per-guard revert proves the premise, not just the guard.** Only the four *MongoDB-local*
`RequireFilter` lines were removed; `AbstractBulkStore.RequireFilter` stayed intact throughout. The tests
still failed — which is direct evidence that these overrides genuinely **do not fall through to the base
guard**, the claim the whole sweep rests on and which had until now been asserted only by reading. A test
that merely showed "a null filter throws" could not have distinguished the two.

**Judgement calls.**

- **Criterion 5 is met within this task's scope, and the limit is worth stating plainly.** The `*All` doors
  exist (inherited from `AbstractBulkStore` / `AbstractAsyncBulkStore`, not overridden by MongoDB), and the
  tests prove they are **not refused** — offline they are a genuine no-op, because `InitCore` is a no-op
  (MongoDB is schema-less) and `ReadCore` returns empty when `Collection` is null. What they do *not* prove
  is that a live `DeleteAll()` actually deletes rows; that is explicitly `## Out of scope` ("Any live-backend
  MongoDB behaviour"). Recorded rather than left to look stronger than it is.
- **The `*All` assertions are bare `NotThrow()`, not `NotThrow<ArgumentNullException>()`.** TASK-117 shipped
  exactly that mistake: a type-scoped negative passes on every *other* exception, so it claimed "the escape
  hatch opens" while proving nothing. The stricter-looking, narrower assertion was rejected for being weaker.
- **The refusal-message test is a `[Theory]` over both operations.** `RequireFilter` picks the door name from
  its `operation` string, so a single-case test would pass with `"update"` mis-wired to `DeleteAll()`.
- **No env gate, and none was needed.** Criterion 2 allowed gating if construction forced a client. It does
  not: both stores have a parameterless constructor that leaves `Client` null. The suite runs in **168 ms**,
  which is itself the evidence it opens no socket — TASK-117's regression suite was silently talking to a
  real Redis and the tell was a 36 s runtime nobody read.

**Flagged, not fixed.**

- **The real fix is still the one this task declines, and it *is* owned: [[TASK-143]]** (`_loose`,
  `status: todo`, P2 — "public CRUD overrides defeat base guards"). Ten stores across three backends
  override public CRUD instead of `*Core`, which is precisely what the family convention exists to prevent;
  repeating the guard is containment, not correction. An earlier draft of this Outcome asserted the work was
  unowned — checked before reporting, and it was wrong. Worth noting that TASK-143 sits in `_loose` with
  `findings: []`, so it is **outside `fix-next`'s pool**: not under a `kind: review-intake` epic and
  carrying no finding id, it can only ever be picked by hand. Adding `SH-M023` to its `findings:` would
  onboard it; not done here because it is a convention change rather than a defect, which is the
  distinction the pool definition draws.
- Nothing else surfaced; the production guards were correct as written.

## Human test plan

N/A — fully covered by automated tests. The guard throws before any I/O, so there is nothing a human could
observe that the assertion does not.

## Implementation plan

_Populated by `/tasks plan TASK-141` — leave empty until then._

## Progress log

- step 2 — picked; ranked above TASK-137 because TASK-141 guards an unbounded destructive write (a null
  filter reaching MongoDB DeleteMany as an empty predicate = whole collection) while TASK-137 is
  operational-only by its own Context, and because TASK-137's fix interacts with TASK-109's refusal path.
  TASK-209 outranks both on severity and reachability but its acceptance mandates a real PostgreSQL
  reproduction; no Docker daemon, no psql, nothing on 5432 — recorded in that task rather than rediscovered.
- step 3 — verified: **held as written.** All four overrides carry `RequireFilter` with an `SH-M023`
  comment (`MongoDBStore.cs:272,285`, `AsyncMongoDBStore.cs:374,387`) and nothing in
  `Birko.Data.MongoDB.Tests` asserts any of them. Two details the task did not have: the stores have
  **different generic constraints** (sync `T : MongoDBModel`, async `T : AbstractModel`), so one model
  deriving from `MongoDBModel` satisfies both; and construction needs **no settings at all** — the
  parameterless ctor leaves `Client` null and `Collection` resolves to null — so the "if construction
  forces a client, gate only that part" escape in criterion 2 is not needed.
- step 4 — layer: local (test-only; the production guards already shipped under TASK-109)
- step 5 — tests in `Birko.Data.MongoDB.Tests/Stores/MongoNullFilterGuardTests.cs` (10); no production
  change. Suite **55/55 green**, 168 ms — the runtime is itself evidence the tests are offline (TASK-117:
  a slow suite is evidence it is *not*).
- step 6 — removed all four `RequireFilter` calls: **6 of 10 failed**. Then removed them **one at a time**
  (criterion 4 asks for each), and each maps to exactly its own test:
  `MongoDBStore.Delete` → `Delete_with_a_null_filter_is_refused` + `The_refusal_names_the_all_rows_door("delete")`;
  `MongoDBStore.Update` → `Update_with_a_null_filter_is_refused` + `The_refusal_names_the_all_rows_door("update")`;
  `AsyncMongoDBStore.DeleteAsync` → `DeleteAsync_with_a_null_filter_is_refused`;
  `AsyncMongoDBStore.UpdateAsync` → `UpdateAsync_with_a_null_filter_is_refused`.
  Contract pins (pass either way, **not** evidence) = `DeleteAll_is_not_refused`, `UpdateAll_is_not_refused`,
  `DeleteAllAsync_is_not_refused`, `UpdateAllAsync_is_not_refused` — removing a guard cannot make the
  all-rows doors throw, so these pin criterion 5 rather than proving the guard.
- step 7 — area is `bulk-filter-operations` (its globs name both MongoDB store files explicitly).
  **No spec change, and that is the right answer, not a skipped step:** this task altered no shipped
  behaviour, and the existing scenarios already state it correctly — *The MongoDB store repeats the guard
  on all four overrides* (:647), the refusal *directing the caller to `DeleteAll()` / `UpdateAll(updates)`*
  (:550) and the `*All` doors (:582, :601). Verified by reading them rather than assumed. Specs describe
  what the code does, not what the tests cover, so adding a scenario for the new coverage would be wrong.
- step 8 — merge gate. `verify-conventions` (project-local shadow): check 1 clean (**0 warnings**, no
  CS86xx) but `-warnaserror` surfaced two **pre-existing** transitive advisories via `MongoDB.Driver 3.2.0`
  — `Snappier 1.0.0` (NU1903, high) and `SharpCompress 0.30.1` (NU1902, moderate). Untracked anywhere;
  spawned as [[TASK-210]] and noted in `## Out of scope`, not folded in. Checks 2–8, 10 N/A for a test-only
  diff; check 9 not triggered (one file, no new project or public interface, and coverage is not an
  architectural change). Step 0b/0c: no new cross-cutting pattern — the two rules this suite leans on
  (bare `NotThrow()`, and a slow suite as evidence of a live connection) are already recorded from
  TASK-117. `code-review` run **inline** rather than forked: the diff is a single test file with no
  production change, and the previous fork returned nothing readable. Security: no surface in the diff
  itself (test-only); the dependency advisories are the real security item and are now tracked.
  `integration: single-branch`, so no branch or merge. Closed `done`; `pr: b86bc1c`.
  `tasks/README.md` not regenerated — unchanged call from TASK-207/TASK-129: ~50 task files from an
  uncommitted `/tasks intake` run would enter the dashboard as links no other checkout has.
