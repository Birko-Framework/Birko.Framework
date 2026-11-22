---
id: FEATURE-023
created: 2026-11-22
---

# Kanban board: quieter column headers (footer, lock, a board-wide lock) — Decisions

> The decision ledger for stakeholders. Every idea-branch is a row with exactly one **state**. Rows are never deleted — `removed` is a state, not a deletion — so the ledger stays auditable.

## Decisions

| ID | Decision | State | Rationale | Date | By | → Tasks |
|----|----------|-------|-----------|------|----|---------|
| D1 | A board can hide the "N cards" footer under each column; boards that do not ask keep it | approved | Chosen in the grill over dropping it by default and removing it: the footer repeats the header count, but dropping it would change Symbio's boards unasked | 2026-11-22 | František Bereň (maintainer) | TASK-968 |
| D2 | A locked column shows only the lock icon in its header, on every board; screen readers still hear "Locked", and pointing at the icon shows it | approved | Chosen in the grill over an option with the chip kept by default: the chip is noise on every board, and nothing it says is lost | 2026-11-22 | František Bereň (maintainer) | TASK-969 |
| D3 | A board can be locked as a whole in one setting, which locks every column; the lock is then shown once for the board, and the columns do not show their own | approved | Chosen in the grill over a board lock shown per column and over no board lock: DraCode locks every column of its board and the lock repeated in four headers | 2026-11-22 | František Bereň (maintainer) | TASK-970 |

## History log

> Append-only. Every state change gets a dated line with the reason — this is the "why it changed", not just the current value.

- 2026-11-22 — feature created from DraCode's request after v0.4.21 (docs/BRIEF.md, 2026-11-22). Q1-Q3 grilled with
  the maintainer in the session's picker the same day; all three approved as recommended (D1-D3).
- 2026-11-22 — Feature reviewed and done: D1-D3 built in TASK-968..970 and shipped in Birko.Web v0.5.2;
  every human test plan run and signed off by the maintainer in the Playground ("all passed").
