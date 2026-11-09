---
id: TASK-892
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P2
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-09
depends-on: [TASK-887]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# SQL aggregates: NULL-only SUM, SQLite decimal views, store-level AVG truncation

## Context

Found by TASK-887 (2026-11-09):
- **The store-level `SelectAggregate`** (`AbstractConnectorBase.BuildAggregateQueryParts`) has SQL Server's integer AVG
  truncation (2 where LINQ says 2.5, silently) and returns raw provider types (NUMERIC vs bigint) with no conversion.
- **SUM over a group of only NULLs** answers NULL on all four providers; LINQ's `Sum(int?)` answers 0. Recorded as a
  deviation in TASK-887's contract, not asserted. Related to TASK-885/886.
- **SQLite view decimal SUM/AVG** use SQLite's REAL arithmetic; the registered `birko_decimal_sum` / `birko_decimal_avg`
  aggregates are never used by views.
- **AVG of an integral column into a decimal property** goes through float (about 15 significant digits); LINQ's
  `Average` into decimal is exact.

## Acceptance criteria

- [ ] Reproduce each (rule 54) per provider, against LINQ.
- [ ] Store-level aggregates share TASK-887's reading rule and AVG operand (one producer); the NULL-only SUM gets one
      recorded answer; SQLite views use the decimal aggregates; AVG precision matches or is refused.
- [ ] Tests, red before each fix.

## Out of scope

- None recorded.

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
