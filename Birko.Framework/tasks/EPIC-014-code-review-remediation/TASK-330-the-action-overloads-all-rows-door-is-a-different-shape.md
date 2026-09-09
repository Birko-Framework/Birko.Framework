---
id: TASK-330
parent: EPIC-014
feature: FEATURE-014
status: todo
priority: P3
assignee: ai
created: 2026-09-09
depends-on: []
blocks: []
related: [TASK-215, TASK-329]
findings: [SH-H002]
pr: null
github-issue: null
affects: [Birko.Data.Stores, Birko.Data.SQL]
---

# Decide whether the `Action<T>` destructive overloads need an all-rows door of their own shape

## Context

Spawned at [[TASK-329]]'s close, from its own "flagged, not fixed" bullet — filed rather than left as
prose because § *findings become tasks* treats an unowned "Z is also awkward" as work that evaporates.

`RequireBoundedFilter` refuses a filter that reduces to every row and, per § SH-H037 / [[TASK-215]],
its message names the all-rows door **this caller** has. For the `Action<T>` overloads that message
currently says `UpdateAll(updates)` / `UpdateAllAsync(updates)` — and measured 2026-09-09, the two SQL
bulk stores **declare no `Action<T>` form of that door at all**, while the portable bases do:

| Class | all-rows update door(s) declared |
|---|---|
| `AbstractBulkStore<T>` | `UpdateAll(PropertyUpdate<T>)` **and** `UpdateAll(Action<T>)` (lines 93, 99) |
| `AbstractAsyncBulkStore<T>` | `UpdateAllAsync(PropertyUpdate<T>, ct)` **and** `UpdateAllAsync(Action<T>, ct)` (lines 157, 163) |
| `DataBaseBulkStore<DB,T>` | `UpdateAll(PropertyUpdate<T>)` **only** |
| `AsyncDataBaseBulkStore<DB,T>` | `UpdateAllAsync(PropertyUpdate<T>, ct)` **only** |

That table was **measured** on all four classes 2026-09-09 (declared public surface, not inferred), but
§ TASK-283's rule is that a recorded measurement expires — re-run it before working this, since it is the
whole premise.

⚠ **The portable bases have the milder half of the same problem, and it is easy to miss.** They *do*
declare `UpdateAll(Action<T>)`, yet their refusal still says `UpdateAll(updates)` — so an action-overload
caller there is pointed at the `PropertyUpdate<T>` door when the one they want exists beside it. Same
sentence, different fix: the SQL stores lack the door, the portable bases merely mis-name it. Answering
one without the other would leave two behaviours behind one message.

So the refusal on a SQL store is not wrong — the named door exists and reaches the intended whole-table
write — but it is a **different shape** from the call being refused: a caller passing
`r => { r.A = f(r.B); }` cannot express that as a `PropertyUpdate<T>`, and the honest remedy left to them
is the `x => true` synonym, which the message does not mention.

## Why this is a decision and not a defect

Nothing is broken and nothing silently does the wrong thing. Three answers are defensible and they differ
in what they add to public surface:

1. **Add `UpdateAll(Action<T>)` / `UpdateAllAsync(Action<T>, ct)` to the two SQL bulk stores**, matching
   the portable bases. Removes the asymmetry, and the read-then-loop implementation already exists one
   overload over. Cost: two new public methods on a widely-derived class, and a second destructive
   all-rows door per store to keep guarded.
2. **Name `x => true` in the refusal when the refused call is the action overload.** No new API. Cost: the
   message becomes overload-dependent, which means the door name stops being the single thing a caller
   passes to `BoundedFilterGuard.Require` — the parameter TASK-329 deliberately kept to one string.
3. **Nothing, recorded.** The synonym works and is tested sync and async. Cost: § SH-H037's *the refusal
   names a door this caller has* is satisfied only in the letter.

## Acceptance criteria

- [ ] The table above **re-measured** on both hierarchies, and the answer recorded even if it changed
- [ ] The portable bases' mis-named door answered in the same change, or the split explained
- [ ] One of the three answers taken, with the measurement that decided it — in particular whether any
      caller anywhere (framework, tests, all 16 consumer repos) passes an `Action<T>` to a filter-based
      destructive overload at all. A zero there argues for answer 3 and makes answer 1 speculative API
- [ ] If answer 1: the new doors are guarded on the same terms as the existing ones, and § Conventions'
      naming rule holds (`*All`, never a short name), with a test that the emitted work covers every row
- [ ] If answer 2: `BoundedFilterGuard.Require`'s single-`allRowsDoor` shape is preserved — the caller
      composes the string, the guard does not learn about overloads
- [ ] Whichever answer, the deliberate asymmetry between the SQL stores and the portable bases is either
      removed or **asserted by a test**, so the next reader meets a decision rather than an oversight
      (§ TASK-263 / TASK-257)

## Out of scope

- The guard itself and its placement — [[TASK-329]] closed that; this is only about which door the
  refusal is entitled to name.

## Human test plan

- [ ] N/A — the deliverable is a decision plus either API or a pinned asymmetry, both covered by tests.
