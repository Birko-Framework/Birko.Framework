---
id: TASK-141
parent: STORY-051
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: ai
created: 2026-08-06
depends-on: []
blocks: []
# findings: ids this task remediates, from a review/audit/spec-harvest pass (CR-* SEC-* SH-* VC-*)
findings: [SH-M023]
pr: null
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

- [ ] `Birko.Data.MongoDB.Tests` asserts `ArgumentNullException` for a null filter on **all four** overrides:
      `MongoDBStore.Delete(filter)`, `MongoDBStore.Update(filter, …)`, `AsyncMongoDBStore.DeleteAsync(filter, …)`,
      `AsyncMongoDBStore.UpdateAsync(filter, …)`
- [ ] The tests run **without a live MongoDB** — no env gate, no skip; if store construction forces a client,
      that specific obstacle is recorded and only it is gated
- [ ] Each test names `SH-M023` and states the mechanism (an override of the *public* method bypasses
      `AbstractBulkStore`'s guard, so the guard is repeated and must stay repeated)
- [ ] **Red-verified**: removing each `RequireFilter` call fails its test. Report the split as numbers, and
      name any test that passes either way as a contract pin rather than as evidence
- [ ] A deliberate all-rows delete/update on the MongoDB stores still works (the guard removed the accidental
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

## Human test plan

N/A — fully covered by automated tests. The guard throws before any I/O, so there is nothing a human could
observe that the assertion does not.

## Implementation plan

_Populated by `/tasks plan TASK-141` — leave empty until then._
