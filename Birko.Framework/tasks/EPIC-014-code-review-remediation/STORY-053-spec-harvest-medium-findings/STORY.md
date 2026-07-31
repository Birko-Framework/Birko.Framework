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

**Not pre-created.** Extract on demand, one task per `SH-Mxxx` entry — heading → title, the `file:line`
line → context, the detail paragraph → reasoning, then derive acceptance and add a regression test.
**These are unverified reviewer claims: confirm against the code before fixing.** The high-severity
verification rate (roughly a quarter imprecise) is the prior to carry in here.

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
