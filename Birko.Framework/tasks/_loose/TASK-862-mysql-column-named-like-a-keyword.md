---
id: TASK-862
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-07
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# A column named like a MySQL keyword (`Rank`) breaks CREATE TABLE

## Context

Found by TASK-859 (2026-11-07), on MySQL live. A property named `Rank` makes CREATE TABLE fail, because column names
are never quoted. That is deliberate, per rule 17 ("quote table identifiers; never quote column identifiers"), but it
leaves keyword-named columns unusable on MySQL. The failure is loud.

## Acceptance criteria

- [ ] Reproduced on each provider with a set of keyword-named properties (`Rank`, `Order`, `Group`, `Key`, `Value`,
      `User`). Record which fail where, in DDL, reads, writes and filters.
- [ ] **Decide with the user:** quote column identifiers per provider (revisiting rule 17 and its measurement), or
      refuse a keyword-named column by name when the table is built, with the provider's keyword list. Record the
      decision.
- [ ] Tests per provider, red before the fix.

## Out of scope

- Cosmos DB view aliases (TASK-844, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
