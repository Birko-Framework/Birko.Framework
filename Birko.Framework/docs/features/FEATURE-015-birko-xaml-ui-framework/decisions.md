---
id: FEATURE-015
created: 2026-06-25
---

# Birko.Xaml — Avalonia-first XAML UI framework mirroring Birko.Web — Decisions

> The decision ledger for stakeholders. Every idea-branch is a row with exactly one **state**. Rows are never deleted — `removed` is a state, not a deletion — so the ledger stays auditable.

## Decisions

| ID | Decision | State | Rationale | Date | By | → Tasks |
|----|----------|-------|-----------|------|----|---------|
| D1 | Tier 0 — single-source design tokens + multi-target generator ([[STORY-029]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Story is `done`. | 2026-06-25 | both | — (tracked in prose) |
| D2 | Tier 0 — Avalonia theme system + runtime ThemeVariant swap ([[STORY-030]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Story is `done`. | 2026-06-25 | both | — (tracked in prose) |
| D3 | Tier 0 validation — Avalonia gallery app + first restyled controls ([[STORY-031]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Story is `done`. | 2026-06-25 | both | — (tracked in prose) |
| D4 | Birko.Xaml.Core — i18n ({l:Tr}) + base ViewModels (Avalonia-free) ([[STORY-032]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Story is `done`. | 2026-06-25 | both | — (tracked in prose) |
| D5 | Building blocks — schema-driven Form, Drawer, SplitPanel ([[STORY-033]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Story is `done`. | 2026-06-25 | both | — (tracked in prose) |
| D6 | Tier 1 — restyled native controls (~20) ([[STORY-034]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Story is `done`. | 2026-06-25 | both | — (tracked in prose) |
| D7 | Tier 2 — composite controls with no native peer ([[STORY-035]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Story is `done`. | 2026-06-25 | both | — (tracked in prose) |
| D8 | Tier 3 — Birko.Xaml.Shell: page bases + app chrome + navigation ([[STORY-036]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Story is `done`. | 2026-06-25 | both | — (tracked in prose) |
| D9 | Avalonia 12 / .NET 10 upgrade for the Birko.Xaml stack ([[STORY-048]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Story is `planned`. | 2026-07-29 | both | [[TASK-092]], [[TASK-093]], [[TASK-094]], [[TASK-095]], [[TASK-096]] |
| D10 | Office-style ribbon overflow — progressive group scaling + group-to-popup collapse ([[STORY-049]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Story is `done`. | 2026-07-29 | both | [[TASK-097]], [[TASK-098]], [[TASK-099]], [[TASK-100]] |
| D11 | Mixed per-item size variants within one ribbon group ([[STORY-056]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Story is `planned`. | 2026-07-31 | both | [[TASK-119]], [[TASK-120]], [[TASK-121]], [[TASK-122]], [[TASK-123]], [[TASK-124]] |
| D12 | Work tracked directly on the epic, outside any story | approved | Backfilled: these tasks exist and are tracked, so the scope decision was taken. | 2026-06-25 | both | [[TASK-054]], [[TASK-055]], [[TASK-056]], [[TASK-057]], [[TASK-101]], [[TASK-102]], [[TASK-103]] |

**States:** `proposed` (fresh from grill, awaiting decision) · `approved` (build it) · `deferred` (not now — note unblock condition) · `changed` (approved but altered — record the delta) · `removed` (rejected / out of scope).

Only `approved` and `changed` rows generate tasks at `/feature decompose`. No row is terminal: a `deferred`/`removed` decision overturned by later evidence (incl. production feedback) is **reopened** by adding a *new* `proposed` row that links the superseded one — the old row is never deleted.

## History log

> Append-only. Every state change gets a dated line with the reason — this is the "why it changed", not just the current value.

- 2026-08-01 — **Ledger opened by backfill**, not by a `/feature new` interview. This feature was created to
  close the [[roadmap]] DV5 gap: `EPIC-015` had been tracked in `tasks/` since 2026-06-25 while this repo's
  `CLAUDE.md` committed to a family-wide `docs/features/` tree that did not exist.
- 2026-08-01 — What the rows above **do** claim: each names a real story or a real set of epic-direct tasks,
  and `→ Tasks` lists task IDs that exist. `Date` is the story's/epic's own `created`; `By` is the epic's
  recorded `owner`. State is `approved` because the work was decomposed and tracked — decomposition is
  the observable decision.
- 2026-08-01 — What they **do not** claim: no rationale text, alternative, or rejected option has been
  reconstructed. Where a real dated decision with reasoning exists it lives in `CHANGELOG.md` or
  `CLAUDE.md` § Recent Updates, which remain the authority for *why*. Rows carry no invented `deferred`
  or `removed` history, so the absence of such rows means "not recorded", not "never considered".
