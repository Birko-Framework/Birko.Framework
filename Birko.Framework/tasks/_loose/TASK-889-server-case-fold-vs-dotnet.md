---
id: TASK-889
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-09
depends-on: [TASK-870]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# `ToLower` / `ToUpper` on PostgreSQL, MySQL and SQL Server differ from .NET on rare letters

## Context

Found by TASK-870 (2026-11-09), measured: the servers' `LOWER` / `UPPER` differ from .NET on 0–19 of 34 texts per form
(Kelvin `K`, `ſ`, Turkish `İ`/`ı`, `ȿ`; MySQL stores "" for some). `č`, `ä`, `é` fold correctly everywhere. On MySQL
and SQL Server, `ToLower().Contains` compared with `==` / `Contains` also follows the accent- and case-insensitive
collation. The gap is pinned as per-provider budgets (`MeasuredFoldGap`, rule 56). SQLite is exact since TASK-870.

## Acceptance criteria

- [ ] **Decide with the user:** (A) leave and document (current); (B) exact in TASK-869 style, which also makes Symbio's
      searches accent-sensitive on MySQL and SQL Server; or move Symbio's searches to `OrdinalIgnoreCase` (exact
      already). A fold in SET has no exact server form; the only exact remedy there is refusal.
- [ ] Implement the decision, inverting the budget tests where they close (rule 56).

## Out of scope

- The items TASK-870 fixed.

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
