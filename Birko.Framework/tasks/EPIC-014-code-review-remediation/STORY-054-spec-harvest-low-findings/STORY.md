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

**Decomposed 2026-08-09** by `/tasks intake --epic EPIC-014`, into **22 per-area triage tasks** —
[[TASK-173]] … [[TASK-194]], one per `### area:` section of the findings doc, each carrying that area's
explicit contiguous `SH-L` id list.

This is the batching the earlier note already called for, made schedulable. The "extract on demand" policy
was the reason none of these 387 had been picked in the ten days since filing: only `status: todo` tasks are
ranked by `/tasks pick`, by the `Next up` snapshot, or by [[fix-next]], so a finding parked as a bullet is
invisible to all three. [[STORY-027]] closed 418 lows by batching per area; this is the same instinct with
the tasks actually written down.

| Task | Area | Findings |
|---|---|---|
| [[TASK-173]] | `llm-provider-and-agents` | 31 (`SH-L144`–`SH-L174`) |
| [[TASK-174]] | `event-bus-and-messaging` | 29 (`SH-L102`–`SH-L130`) |
| [[TASK-175]] | `background-jobs` | 24 (`SH-L001`–`SH-L024`) |
| [[TASK-176]] | `security-and-authorization` | 23 (`SH-L217`–`SH-L239`) |
| [[TASK-177]] | `data-sync` | 23 (`SH-L055`–`SH-L077`) |
| [[TASK-178]] | `migrations` | 22 (`SH-L175`–`SH-L196`) |
| [[TASK-179]] | `workflow-state-machine` | 21 (`SH-L367`–`SH-L387`) |
| [[TASK-180]] | `views-and-aggregation` | 20 (`SH-L347`–`SH-L366`) |
| [[TASK-181]] | `validation-and-rules` | 19 (`SH-L328`–`SH-L346`) |
| [[TASK-182]] | `store-crud-contract` | 19 (`SH-L281`–`SH-L299`) |
| [[TASK-183]] | `settings-configuration-chain` | 18 (`SH-L250`–`SH-L267`) |
| [[TASK-184]] | `caching` | 17 (`SH-L038`–`SH-L054`) |
| [[TASK-185]] | `tenant-isolation` | 15 (`SH-L313`–`SH-L327`) |
| [[TASK-186]] | `entity-tagging` | 14 (`SH-L088`–`SH-L101`) |
| [[TASK-187]] | `store-decorator-composition` | 13 (`SH-L300`–`SH-L312`) |
| [[TASK-188]] | `specifications-and-paging` | 13 (`SH-L268`–`SH-L280`) |
| [[TASK-189]] | `filter-expression-translation` | 13 (`SH-L131`–`SH-L143`) |
| [[TASK-190]] | `bulk-filter-operations` | 13 (`SH-L025`–`SH-L037`) |
| [[TASK-191]] | `serialization` | 10 (`SH-L240`–`SH-L249`) |
| [[TASK-192]] | `schema-index-and-ddl` | 10 (`SH-L207`–`SH-L216`) |
| [[TASK-193]] | `repository-contract` | 10 (`SH-L197`–`SH-L206`) |
| [[TASK-194]] | `entity-localization` | 10 (`SH-L078`–`SH-L087`) |

All P2 — low severity is the intake ladder's suggestion tier regardless of theme. [[fix-next]] ranks by
blast radius, so a low finding on a tenancy surface still outranks a medium one on a doc comment.

**Where a low and a medium finding in the same area are one edit, do it once** and cross-reference — the
paired medium task is named in each task's Out-of-scope section. [[TASK-182]] additionally owns `SH-L297`
and `SH-L298`, which are exact duplicates of `SLI-4` / `SLI-6` from the recovered set ([[TASK-195]]).

## Acceptance criteria

- [ ] Every low finding is confirmed, refuted, or explicitly deferred with a reason
- [ ] Confirmed lows are fixed (batched by area where that is cheaper) with tests where behaviour changes
- [ ] Any finding whose fix changes behaviour triggers a `/specs regen` of its area, with the spec diff
      reviewed

## Human test plan

N/A — this is a tracking story. Each extracted task carries its own plan.
