---
id: STORY-055
parent: EPIC-014
status: in-progress
created: 2026-07-30
source: SPEC-HARVEST-FINDINGS-2026-07-30.md
severity: unrated
finding-count: 16
finding-ids: recovered as CMC-1…4, SLI-1…6, UOW-1…6 — awaiting SH- ids
---

# Spec-harvest — the three unrated areas

## Progress

**16 / 16 recovered (2026-07-31)**, verbatim, in [`RECOVERED-FINDINGS.md`](RECOVERED-FINDINGS.md).
Remaining: confirm severities, assign `SH-` ids, fold into the severity stories, correct the totals.

## User story

As a maintainer, I want the findings from the three areas that predate the severity rating actually written
down, so they are triageable like the other 865 instead of being a number in a footnote.

## Where this came from

`SPEC-HARVEST-FINDINGS-2026-07-30.md` totals **"57 high · 421 medium · 387 low = 865"** and separately notes:

> **3 areas were never capped** and are complete, but predate the severity rating, so they carry no
> high/medium/low split: `core-model-contracts` (4), `store-lazy-initialization` (6),
> `unit-of-work-and-transactions` (6).

The 865 **excludes** those 16, and because the document is organised as three severity sections, findings
with no severity had nowhere to live. So the note read as "these areas are complete" when it meant
**"swept, and the results were not kept."**

## Why they were lost — a schema change between passes

Not an oversight in the writing. The two harvest passes returned **different shapes**, confirmed by reading
both workflow journals:

| Pass | Workflow | Areas | Per-area keys |
|---|---|---|---|
| First harvest (2026-07-30) | `wf_0987dab2-cb0` | **25** | `keyBehaviors`, `suspectedBugs`, `problems` — **no severity** |
| Uncapped re-sweep | `wf_7de49a4a-db4` | 19 | `suspectedBugs` **with `severity`**, `sweptToExhaustion`, `problems` |

Severity was added for the re-sweep. These three areas were *never re-swept* — precisely because they had
never been capped, so they looked finished — which left their findings in the **old, severity-less shape**.
The aggregation that built `SPEC-HARVEST-FINDINGS-2026-07-30.md` grouped by severity, so entries carrying no
severity matched no section and survived only as a count.

**The general lesson, worth more than the 16 findings:** when a fan-out's output schema gains a field
mid-project, the items that already ran under the old schema fall out of any aggregation keyed on that
field — silently, and looking like completeness rather than loss. The tell was that the note reported
*counts* it could only have got from data it then failed to include.

## How they were recovered

Not by re-sweeping. `/specs regen` would **not** have worked: regen is diff-based (an unexplained
behavioural change between the old spec and the new one is what it calls a finding), and none of these
areas' sources have changed since the harvest — `Birko.Contracts/Models` last moved 2026-03-26,
`Birko.Data.Patterns/UnitOfWork` 2026-03-10, `Birko.Data.Stores` bases 2026-07-14. An unchanged area
regenerates to an identical spec and reports nothing.

The findings were instead read straight out of the first harvest's journal:

```
~/.claude/projects/C--Source-Birko-Framework-Birko-Framework/
  fe3dba93-b49b-440d-bcc9-be524c94117e/subagents/workflows/wf_0987dab2-cb0/journal.jsonl
```

All 25 areas' agent returns are intact there, and the three areas' `suspectedBugs` arrays hold **4, 6 and
6** entries — matching the coverage note exactly, each with summary, `file`, `line` and a reasoned `why`.
Several state they were verified by compiling or running the case.

**This is a perishable source.** Journals live under a session directory, not in git. The recovered text is
now committed in `RECOVERED-FINDINGS.md`, which is the point of that file.

## Scope

| Area | Count | Recovered as |
|---|---|---|
| `core-model-contracts` | 4 | CMC-1 … CMC-4 |
| `store-lazy-initialization` | 6 | SLI-1 … SLI-6 |
| `unit-of-work-and-transactions` | 6 | UOW-1 … UOW-6 |

Proposed split: **1 high** (UOW-1, an ES commit retry double-applying items that already succeeded),
**9 medium**, **6 low**. Severities are this story's proposal — the harvester emitted none.

## Out of scope

Fixing anything. This story ends when the findings are rated, given `SH-` ids, and routed to
[[STORY-051]] / [[STORY-053]] / [[STORY-054]]. The fixes belong to whichever severity story receives them.

## Acceptance criteria

- [x] The three areas' findings are recovered verbatim with file, line and reasoning (2026-07-31)
- [x] The recovery is committed to the repo, off the perishable journal
- [x] The root cause of the loss is recorded, not just the loss (schema change between passes)
- [ ] Severities confirmed for all 16
- [ ] `SH-` ids assigned, continuing the existing ranges — **do not renumber** `SH-H`/`SH-M`/`SH-L`
- [ ] Folded into `SPEC-HARVEST-FINDINGS-2026-07-30.md` under their severity sections
- [ ] Header total corrected from 865 to **881** (58 high · 430 medium · 393 low), and each severity story's
      `finding-count` updated
- [ ] The coverage-gaps note rewritten or deleted — it currently says "complete"
- [ ] Duplicates cross-referenced rather than double-filed. **Known overlap:** two `SH-L` entries near lines
      4829/4835 of the findings doc already describe SLI-4 (non-volatile double-checked read) and SLI-6
      (undisposed `SemaphoreSlim`), filed under another area. Check for others before assigning ids.

## Human test plan

N/A — documentation recovery, no runtime surface.
