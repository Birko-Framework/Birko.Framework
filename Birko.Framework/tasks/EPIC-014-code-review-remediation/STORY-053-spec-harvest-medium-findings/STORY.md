---
id: STORY-053
parent: EPIC-014
status: planned
created: 2026-07-30
source: SPEC-HARVEST-FINDINGS-2026-07-30.md
severity: medium
finding-count: 421
finding-ids: SH-M001 … SH-M421
---

# Spec-harvest — medium findings

## Progress

**0 / 421 closed.** All are unverified harvester claims. Per-finding detail is in
[`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md) § Medium severity.

## User story

As a maintainer, I want the **medium**-severity spec-harvest findings triaged so the genuine defects among
them get fixed and the noise is refuted on the record rather than re-read every time.

## Scope

The 421 medium findings `SH-M001 … SH-M421`, spread across 22 areas. Medium is everything that is a real
behavioural defect but does not reach the high bar (no silent data loss, no cross-tenant leakage, no auth
bypass, no destructive op on the wrong rows).

## Tasks

**Decomposed 2026-08-09** by `/tasks intake --epic EPIC-014`, into **22 per-area triage tasks** —
[[TASK-151]] … [[TASK-172]], one per `### area:` section of the findings doc, each carrying that area's
explicit contiguous `SH-M` id list.

The earlier "extract on demand, one task per `SH-Mxxx`" policy is **superseded**, for two reasons:

- *A checklist line is filed, not scheduled.* Only `status: todo` tasks are ranked by `/tasks pick`, by the
  `Next up` snapshot, or by [[fix-next]]. On-demand extraction meant no picker could see any of these 421,
  and none had been extracted in the ten days since filing — the [[roadmap]] DV12 audit is what surfaced it.
- Per-finding tasks would have been the wrong grain anyway. Findings in one area share a spec, a source
  glob set, and frequently a root cause; the intake rule is that findings fixed in one edit are one task.

Per-finding extraction still happens — *inside* a per-area task, when triage confirms something too large to
fix there. That goes out via `/tasks spawn`, never as a ticked box with the work undone.

| Task | Area | Findings | Priority |
|---|---|---|---|
| [[TASK-151]] | `views-and-aggregation` | 36 (`SH-M377`–`SH-M412`) | P1 |
| [[TASK-152]] | `migrations` | 33 (`SH-M172`–`SH-M204`) | P1 |
| [[TASK-153]] | `filter-expression-translation` | 29 (`SH-M123`–`SH-M151`) | P1 |
| [[TASK-154]] | `schema-index-and-ddl` | 25 (`SH-M221`–`SH-M245`) | P1 |
| [[TASK-155]] | `event-bus-and-messaging` | 24 (`SH-M099`–`SH-M122`) | P2 |
| [[TASK-156]] | `validation-and-rules` | 22 (`SH-M355`–`SH-M376`) | P1 |
| [[TASK-157]] | `data-sync` | 21 (`SH-M049`–`SH-M069`) | P1 |
| [[TASK-158]] | `background-jobs` | 21 (`SH-M001`–`SH-M021`) | P2 |
| [[TASK-159]] | `store-decorator-composition` | 20 (`SH-M317`–`SH-M336`) | P1 |
| [[TASK-160]] | `llm-provider-and-agents` | 20 (`SH-M152`–`SH-M171`) | P2 |
| [[TASK-161]] | `tenant-isolation` | 18 (`SH-M337`–`SH-M354`) | P1 |
| [[TASK-162]] | `repository-contract` | 16 (`SH-M205`–`SH-M220`) | P1 |
| [[TASK-163]] | `store-crud-contract` | 15 (`SH-M302`–`SH-M316`) | P1 |
| [[TASK-164]] | `settings-configuration-chain` | 15 (`SH-M275`–`SH-M289`) | P2 |
| [[TASK-165]] | `security-and-authorization` | 15 (`SH-M246`–`SH-M260`) | P1 |
| [[TASK-166]] | `entity-tagging` | 15 (`SH-M084`–`SH-M098`) | P2 |
| [[TASK-167]] | `serialization` | 14 (`SH-M261`–`SH-M274`) | P2 |
| [[TASK-168]] | `entity-localization` | 14 (`SH-M070`–`SH-M083`) | P2 |
| [[TASK-169]] | `caching` | 14 (`SH-M035`–`SH-M048`) | P2 |
| [[TASK-170]] | `bulk-filter-operations` | 13 (`SH-M022`–`SH-M034`) | P1 |
| [[TASK-171]] | `specifications-and-paging` | 12 (`SH-M290`–`SH-M301`) | P1 |
| [[TASK-172]] | `workflow-state-machine` | 9 (`SH-M413`–`SH-M421`) | P2 |

P1 marks the security-&-tenancy and correctness-&-invariants themes of the intake ladder; P2 the rest.
[[fix-next]] ranks by blast radius rather than by this field, so it is a tie-breaker, not the running order.

**These are unverified reviewer claims: confirm against the code before fixing.** The high-severity
verification rate (roughly a quarter imprecise, none outright refuted) is the prior to carry in here.

Three findings in [[TASK-170]] already have history — `SH-M022` and `SH-M023` were remediated by
[[TASK-110]] and [[TASK-109]], and `SH-M025` carries a stray verdict pasted from `SH-H003`. That task
records all three; don't re-triage them.

## Two findings already have verdicts — and one of them is mis-stamped

`SH-M022` and `SH-M025` are the only mediums carrying a `**Verdict:**` line, and **both verdict
paragraphs are the same text as `SH-H003`'s** (the ORDER BY injection verdict), pasted onto them:

- **`SH-M022` — the stamp is apt by luck.** It is the same call site as SH-H003
  (`DataBaseBulkStore.cs:44`) and the same root cause: the ORDER BY key is never resolved through
  `GetField().GetSelectName()`. So a `[NamedField("col")]`-remapped property emits ORDER BY on a
  nonexistent column, which `RunReaderCommand` swallows — **the read returns empty rather than throwing**.
  Folded into [[TASK-110]] because one fix closes both.
- **`SH-M025` — the stamp is simply wrong.** That finding is about `ReadCore` handing out a lazy iterator
  that holds an open `DbConnection` and `DbDataReader` outside the store. It has nothing to do with ORDER
  BY. **Treat SH-M025 as unverified** despite the CONFIRMED line, and strip the stray verdict when it is
  triaged.

Worth generalising: a copied verdict block is worse than no verdict, because it launders an unchecked
claim as a checked one. Any future `**Verdict:**` line should name the specific code it traced.

## Acceptance criteria

- [ ] Every medium finding is confirmed, narrowed, or refuted against the code
- [ ] Every confirmed medium is fixed with a regression test, or explicitly waived with a reason
- [ ] `SH-M025`'s stray CONFIRMED verdict is removed from the findings doc
- [ ] Any finding whose fix changes behaviour triggers a `/specs regen` of its area, with the spec diff
      reviewed

## Human test plan

N/A — this is a tracking story. Each extracted task carries its own plan.
