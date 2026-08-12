---
id: TASK-206
parent: null
feature: null
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: human
created: 2026-08-12
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# `HybridCache`'s L2 fallback filter cannot tell a misconfiguration from an outage

## Context

Filed from [[TASK-117]]'s spec-diff review, where the new degradation path was **recorded but deliberately
not changed** — fixing it alters the Hybrid fallback contract for every failure mode, which is a decision
this task exists to make.

`HybridCache` wraps every L2 interaction in `catch when (_options.FallbackToL1OnL2Failure)`
(`../Birko.Caching.Hybrid/HybridCache.cs:234-247` for `ClearAsync`), default `true`. The filter is
**unqualified**, so it catches everything the L2 can throw and reports success.

TASK-117 made `RedisCache.ClearAsync` refuse with `WholeDatabaseDeleteException` when no `KeyPrefix` gives it
a namespace of its own. That is the right answer for the cache — but under a `HybridCache`, the refusal is
swallowed: **L1 is cleared, L2 is not, and the caller sees no exception.** The operator's Redis is
misconfigured and the only symptom is a cache that stops clearing its shared tier.

Before TASK-117 the same configuration flushed the whole database. So this is not a regression the fix
introduced — it is a *pre-existing* blind spot in the filter that the fix newly reaches, and it converts a
loud misconfiguration into a quiet one. The `caching` spec now records it as shipped behaviour: *"A refusing
L2 clear degrades to an L1-only clear"*.

**This family is already known.** The same unqualified filter catches `OperationCanceledException` — spec'd as
*"Cancellation is swallowed as a miss"*. So there are now at least two exception classes flowing through a
filter written for "the L2 is unreachable", and neither is one.

## Acceptance criteria

- [ ] **Decide** whether `FallbackToL1OnL2Failure` should mean "tolerate *transport* faults" rather than
      "tolerate everything", and if so what the discriminator is — an allow-list of transport exception types,
      an exclusion list (`WholeDatabaseDeleteException`, `OperationCanceledException`,
      `ArgumentException`-family), or a delegate on `HybridCacheOptions`
- [ ] The reasoning is recorded either way, **including the cost of being wrong in each direction**: a filter
      that is too narrow turns a transient outage into a caller-visible exception (the thing the flag exists to
      prevent), and one that is too wide is what produced this task
- [ ] `OperationCanceledException` is decided in the same pass — it is the same hole and the spec already
      records it. Deciding only the new half would leave the task half-done
- [ ] Whatever is chosen, a misconfigured L2 is **observable**: at minimum the condition is reported (a log,
      a counter, an event) rather than returning success. A silent no-op on a "clear the cache" call is the
      failure mode TASK-117 refused to ship in `RedisCache`, and forwarding it through `HybridCache` re-ships
      it one layer up
- [ ] Tests pin the chosen behaviour for both exception classes, and the `caching` spec scenarios
      *"A refusing L2 clear degrades to an L1-only clear"* and *"Cancellation is swallowed as a miss"* are
      regenerated to match
- [ ] If the answer is "no change", both spec scenarios stay and `Birko.Caching.Hybrid/CLAUDE.md` says
      explicitly that the flag tolerates *any* L2 exception, so the next reader does not have to rediscover it

## Out of scope

- The `RedisCache` refusal itself ([[TASK-117]]) — it is correct at its own layer.
- `HybridCache`'s write-through and read-through paths, except insofar as the same filter governs them.

## Human test plan

- [ ] Point a `HybridCache` at a Redis L2 with no `KeyPrefix`, call `ClearAsync`, and confirm the operator can
      tell — from logs or an exception — that L2 was not cleared. Today nothing distinguishes it from a
      successful clear, which is the whole point of the task.

## Implementation plan

_Populated by `/tasks plan TASK-206` — leave empty until then._
