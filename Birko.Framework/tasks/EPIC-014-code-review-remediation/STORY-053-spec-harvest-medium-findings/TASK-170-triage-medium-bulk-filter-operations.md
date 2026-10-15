---
id: TASK-170
parent: STORY-053
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P1
assignee: ai
created: 2026-08-09
depends-on: []
blocks: []
# findings: ids this task remediates, from a review/audit/spec-harvest pass (CR-* SEC-* SH-* VC-*)
findings: [SH-M022, SH-M023, SH-M024, SH-M025, SH-M026, SH-M027, SH-M028, SH-M029, SH-M030, SH-M031, SH-M032, SH-M033, SH-M034, SH-M434]
pr: null
github-issue: null
jira-key: null
---

# Triage the 14 medium spec-harvest findings in `bulk-filter-operations`

## Context

Filed by `/tasks intake --epic EPIC-014` on 2026-08-09, decomposing [[STORY-053]] — which held all
421 medium findings as an unscheduled checklist. A checklist line is
filed, not scheduled: only `status: todo` **tasks** are ranked by `/tasks pick`, by the `Next up` snapshot,
or by [[fix-next]], so the whole backlog was invisible to every picker. This task owns the **13** findings
that fall in the `bulk-filter-operations` area.

**Findings:** `SH-M022`–`SH-M034` (contiguous), detailed in
[`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md) § Medium severity →
`### area: bulk-filter-operations`, lines 576–659.

**The contract under review** is specced in [`docs/specs/bulk-filter-operations.md`](../../../docs/specs/bulk-filter-operations.md), harvested from
18 source globs — `../Birko.Data.Stores/IBulkStore.cs`, `../Birko.Data.Stores/IAsyncBulkStore.cs`, `../Birko.Data.Stores/AbstractBulkStore.cs`, and 15 more. Every one of
them points into a **sibling repo**, so a fix here normally lands as three commits in three repos (production,
regression suite, this file) per CLAUDE.md § Integration model.

**These are unverified harvester claims, not confirmed defects.** Confirm each against the code before fixing.
The prior to carry in comes from the only findings anyone has checked by hand: of 15 high findings verified at
harvest time, **12 held exactly and 3 needed their scope corrected**, 0 were outright refuted — so expect
roughly a quarter to be imprecise rather than wrong. Refuting on the record is a valid close; a finding
silently dropped is one the next sweep re-raises and someone re-litigates from scratch.

**Ordering constraint — the spec currently documents these defects as shipped behaviour.** The harvest specced
what the code *does*, defects included, which is exactly what let it find them. So a behavioural fix leaves
`docs/specs/bulk-filter-operations.md` lying until `/specs regen bulk-filter-operations` runs, and **that spec diff is the fix's evidence**.

**Three of these 13 already have history — do not re-file them.**

| Finding | State |
|---|---|
| `SH-M022` | Folded into [[TASK-110]] (`done`) — same call site and root cause as `SH-H003`: the ORDER BY key never resolved through `GetField().GetSelectName()`, so a `[NamedField]`-remapped property emitted ORDER BY on a nonexistent column and `RunReaderCommand` swallowed it, returning **empty rather than throwing**. Close it as remediated, don't re-triage. |
| `SH-M023` | Fixed by [[TASK-109]] (`done`); its MongoDB regression coverage is still open as [[TASK-141]]. Leave both alone. |
| `SH-M025` | Carries a **`Verdict: CONFIRMED` line that belongs to `SH-H003`** — pasted on by mistake. The finding is actually about `ReadCore` handing out a lazy iterator holding an open `DbConnection`/`DbDataReader` outside the store, which has nothing to do with ORDER BY. **Treat it as unverified**, and strip the stray verdict from the findings doc as part of this task. A copied verdict block is worse than no verdict — it launders an unchecked claim as a checked one. |

### Added by the 2026-10-12 spec regen (harvester claims, not reproduced)

- **SH-M434** — Filter `Update(filter, Action<T>)` is read-then-loop over single-entity updates (`AbstractBulkStore.cs:76-86`, `AbstractAsyncBulkStore.cs:113-127`, `DataBaseBulkStore.cs:113-127`, `AbstractInMemoryStore.cs:126-129`): an entity deleted between the read and its write makes the single update throw `EntityNotFoundException` (TASK-555), so earlier entities stay written and later ones are skipped — where the collection `Update(IEnumerable)` writes every stored item and then reports

## Acceptance criteria

- [x] All 14 findings are marked **confirmed**, **confirmed-narrower**, or **refuted** against the code,
      with the verdict and its evidence (`file:line` + the mechanism, not just the rule) written back into
      `SPEC-HARVEST-FINDINGS-2026-07-30.md`. Every `Verdict:` line names the specific code it traced
- [x] Confirmed findings are fixed with a regression test, or explicitly waived with a recorded reason.
      Findings sharing a root cause are fixed **together**, not one edit each
- [x] Each regression test is **red-verified**: reverting the fix fails it. Report the split as numbers, and
      name any test that passes either way as a contract pin rather than as evidence
- [ ] Any behavioural fix is followed by `/specs regen bulk-filter-operations`, with the spec diff reviewed as the change's
      evidence
- [x] A confirmed finding too large for this task is spawned via `/tasks spawn` — never left as a ticked box
      with the work undone
- [x] [[STORY-053]]'s **Progress** line and `finding-count` reflect this area's closed count

## Resolution (2026-10-15)

**14 findings triaged: 12 confirmed, 2 confirmed-narrower, 0 refuted.** 8 fixed here (SH-M024, M025, M026, M027,
M030, M031, M032, M034) plus SH-M434. 3 found fixed by earlier tasks (SH-M022 `ResolveOrderFields`, SH-M023
`RequireFilter`, SH-M029 TASK-498). SH-M033 waived with the reason on record: the portable fallback is the
whole-document write of backends whose unit of storage is the document. SH-M028 spawned as [[TASK-629]], a default
transaction for a bulk write that changes behaviour on every provider. SH-M025's earlier verdict line was a copy of
SH-M022's and is corrected in place.

- **SH-M434** (added by the 2026-10-12 regen), CONFIRMED, fixed: a matched row deleted between the read and its write
  threw `EntityNotFoundException` mid-loop. It no longer matches the filter, so every read-then-loop
  (`AbstractBulkStore`, `AbstractAsyncBulkStore`, both SQL bulk stores, `UpdateAll`) skips it, as a SQL
  `UPDATE … WHERE` would.
- **Behaviour changes, recorded:** a SQL store with no settings throws on first use instead of answering empty; the
  sync SQL bulk read is materialised; `Set` of a nested member is refused at the builder.

**Red-verified, as numbers.** `BulkFilterTriageSqlTests` (SQLite) 5 of 5; `BulkFilterTriageTests` (InMemory) 4 of 4.
SH-M024 and SH-M034 have no test: both are reachable only through a sync-only connector the framework does not ship.

**Found while closing, in TASK-154's code.** `TryLoadTable` caches "maps to no table", and `RegisterTableName`
invalidated only the positive cache, so a fluent table name registered after a failed lookup was ignored. Fixed here
(`A_table_name_registered_after_a_failed_lookup_is_honoured`, red without the line).

**Recorded, not explained (rule 54).** One full-batch live run failed two `ViewDdlTests`
(`BuildViewSelectSql_CustomerOrderView_ContainsCountAndSum`, `…AggregateAliases…`) without the message being
captured. They pass alone, and 52 consecutive runs of `Birko.Data.SQL.Tests` were clean afterwards. The negative-cache
defect above is a candidate, but neither test registers a table name, so it is not claimed as the cause.

The ViewModel test `ReadOne_ReturnsDefault_WhenNoStore` pinned SH-M031's silent answer; it is inverted to expect the
refusal. Regression: 38 suites live (SQL on four servers, ElasticSearch, RavenDB 6.0 and MongoDB 7 live), green.

**Spec regen pending**, batched with the database fixes: `bulk-filter-operations`.

## Out of scope

- The other 408 medium findings — they belong to the other 21 per-area tasks under [[STORY-053]].
- High-severity findings in this area — [[STORY-051]] owns those.
- Low-severity findings in this area — [[TASK-190]] owns them. Where a fix closes both, do it once and
  cross-reference; don't split one edit across two tasks.
- **Test gaps.** Test coverage was explicitly out of scope for the harvest sweep, so a missing test is not a
  finding here — only a test a confirmed fix needs.
- Per-sub-repo spec trees, which would make this area's staleness measurable from its own repo — that's
  [[TASK-131]].

## Human test plan

N/A: fully covered by automated tests. A library contract with no UI surface; every fix is asserted by the rows a
real store holds.

## Implementation plan

_Populated by `/tasks plan TASK-170` — leave empty until then._
