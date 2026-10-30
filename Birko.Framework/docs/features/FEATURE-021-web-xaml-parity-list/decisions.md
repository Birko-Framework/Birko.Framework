---
id: FEATURE-021
created: 2026-10-30
---

# Birko.Web ↔ Birko.Xaml — a parity list, kept honest by a test — Decisions

> The decision ledger for stakeholders. Every idea-branch is a row with exactly one **state**. Rows are never deleted — `removed` is a state, not a deletion — so the ledger stays auditable.

## Decisions

| ID | Decision | State | Rationale | Date | By | → Tasks |
|----|----------|-------|-----------|------|----|---------|
| D1 | Keep a parity list: every web piece has a row saying where it stands on the desktop | approved | Asked for: "we should keep them synced"; today the answer has to be surveyed by hand | 2026-10-30 | František Bereň (maintainer) | TASK-793 |
| D2 | The list is a readable table kept with the desktop control library, and the check reads that same table | approved | Chosen in the grill over a machine-only data file or a section of the developer notes: one copy that people read and the check enforces | 2026-10-30 | František Bereň (maintainer) | TASK-793 |
| D3 | A row's status is one of: built as a Birko control; a built-in control in the Birko look; a form field only; needs a Birko look; missing; skipped (with the reason); not applicable (a shell file that holds only type definitions) | approved | The four kinds of answer the 2026-10-30 survey found, plus the deliberate skip the user asked to record, plus a status for shell files with nothing to port | 2026-10-30 | František Bereň (maintainer) | TASK-793 |
| D4 | The list covers the web components and the web application shell | approved | Chosen in the grill over components only | 2026-10-30 | František Bereň (maintainer) | TASK-794 |
| D5 | Web components get one row each; the application shell gets one row per source file, except the files that only gather the others | approved | Chosen in the grill over one row per shell module (a new page base inside a module would go unnoticed) and one per export (fragile to read) | 2026-10-30 | František Bereň (maintainer) | TASK-794 |
| D6 | The check fails when a web piece has no row | approved | The core of the request: a new web component cannot ship unnoticed | 2026-10-30 | František Bereň (maintainer) | TASK-793 |
| D7 | The check fails when a row names a web piece that no longer exists | approved | Chosen in the grill: keeps the list from collecting rows for removed components | 2026-10-30 | František Bereň (maintainer) | TASK-793 |
| D8 | The check fails when a row claims a desktop control or Birko look that cannot be found | approved | Chosen in the grill: the list must not claim a port that is not there | 2026-10-30 | František Bereň (maintainer) | TASK-795 |
| D9 | The check fails when a skipped row gives no reason | approved | Offered in the grill and not selected — recorded so the verdict is explicit rather than silently dropped | 2026-10-30 | František Bereň (maintainer) | TASK-793 |
| D10 | When the web library is not checked out beside the framework, the check skips and says so; with the "require live" switch set it fails instead | approved | The same rule as the shared diff examples (FEATURE-020 D5), so a developer without the web checkout is not blocked | 2026-10-30 | František Bereň (maintainer) | TASK-793 |
| D11 | Skipped as not needed on the desktop: the guided tour, chat, image gallery, segmented control, button group, toolbar, empty state, loading skeleton and "data is stale" banner | approved | Chosen in the grill: web-app patterns of little use on the desktop, or easily composed from built-in desktop controls | 2026-10-30 | František Bereň (maintainer) | TASK-793 |
| D12 | Porting the components the list marks missing is not part of this feature; each port is decided on its own | approved | The feature makes the gaps visible; a port is separate scope with its own look and behaviour to settle | 2026-10-30 | František Bereň (maintainer) | TASK-793 |
| D13 | A build-server job of its own checks out the web library and runs the check with the "require live" switch, whenever the list or the check changes, and on demand | approved | Chosen over adding it to the diff examples' job and over local-only: without the web checkout the check skips, and a skip reads as green | 2026-10-30 | František Bereň (maintainer) | TASK-796 |

_`Date` and `By` stay `—` until `/feature decide` stamps a verdict — they record **when/who decided**, not when the row was created (creation is in the History log)._

**States:** `proposed` (fresh from grill, awaiting decision) · `approved` (build it) · `deferred` (not now — note unblock condition) · `changed` (approved but altered — record the delta) · `removed` (rejected / out of scope).

Only `approved` and `changed` rows generate tasks at `/feature decompose`. No row is terminal: a `deferred`/`removed` decision overturned by later evidence (incl. production feedback) is **reopened** by adding a *new* `proposed` row that links the superseded one — the old row is never deleted.

## History log

> Append-only. Every state change gets a dated line with the reason — this is the "why it changed", not just the current value.

- 2026-10-30 — feature created; decisions seeded as `proposed` from the grill-me interview (asked in the session's
  picker; the request is in docs/BRIEF.md under 2026-10-30). Q9 (does the build server run the check with the web
  library present) was raised and not asked, so it has no row yet.
- 2026-10-30 — D1–D12 proposed → approved (František Bereň, maintainer): D1–D8 and D10–D12 as grilled; D9, not picked in the grill, approved
  because a skip without a reason cannot be told from a forgotten one.
- 2026-10-30 — D13 added and approved, answering Q9: a CI job of its own, like the diff examples' job, over sharing that
  job or staying local-only.
- 2026-10-30 — decomposed into STORY-069 (EPIC-015): D1–D3, D6, D7, D9–D12 → TASK-793; D4, D5 → TASK-794;
  D8 → TASK-795; D13 → TASK-796.
- 2026-10-30 — tasks renumbered TASK-789–792 → TASK-793–796 (the maintainer's call): a parallel session filed
  EPIC-001 tasks under TASK-789, TASK-790 and TASK-791. Commits made before the renumber carry the old ids in their
  subjects (d803c4f2 … 741c0614); history is not rewritten.
