---
id: STORY-054
parent: EPIC-014
status: planned
created: 2026-07-30
source: SPEC-HARVEST-FINDINGS-2026-07-30.md
severity: low
finding-count: 387
finding-ids: SH-L001 … SH-L387
---

# Spec-harvest — low findings

## Progress

**0 / 387 closed.** All are unverified harvester claims. Per-finding detail is in
[`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md) § Low severity.

## User story

As a maintainer, I want the **low**-severity spec-harvest findings available as a triaged backlog, so that
cheap correctness and documentation wins can be picked up opportunistically without re-reading the sweep.

## Scope

The 387 low findings `SH-L001 … SH-L387` across 22 areas. Low is: contract/doc divergence, a defect behind
an unlikely configuration, a missing guard with no data consequence, or an inconsistency between two
backends that no consumer currently depends on.

Expect a higher refute rate here than in the highs. A low finding is often the harvester noticing that code
and its own doc comment disagree — which is sometimes a defect, sometimes a stale comment, and sometimes
the harvester misreading a deliberate asymmetry. Refuting on the record is a valid close.

## Tasks

**Not pre-created.** Extract on demand, one task per `SH-Lxxx` entry. Batching is usually right here — a
dozen doc-comment corrections in one area is one task, not twelve, following the same instinct STORY-027
used to close 418 low findings.

## Acceptance criteria

- [ ] Every low finding is confirmed, refuted, or explicitly deferred with a reason
- [ ] Confirmed lows are fixed (batched by area where that is cheaper) with tests where behaviour changes
- [ ] Any finding whose fix changes behaviour triggers a `/specs regen` of its area, with the spec diff
      reviewed

## Human test plan

N/A — this is a tracking story. Each extracted task carries its own plan.
