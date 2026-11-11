---
id: TASK-915
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-11
depends-on: [TASK-882]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Rules: support a `Like` pattern with an interior `%`

## Context

Filed at the user's request (2026-11-11). TASK-882 refuses a rule `Like` with an interior `%` (`a%b`) by name on every
runner, because the compiled rule has no form a store can translate, so the runners cannot give one answer (before:
memory matched nothing, SQL compared the literal text). Supporting it means: the pattern in `RuleEvaluator` and the
compiled rule (`OrdinalIgnoreCase`, as the other string operators), and a translation every store accepts (SQL `LIKE`
with escaping through TASK-869's exact form; MongoDB regex; ES wildcard; Cosmos `RegexMatch`/`LIKE`; others refuse).

## Acceptance criteria

- [ ] Measure the candidate translations per store against the in-memory answer (rule 54).
- [ ] Implement on every runner or keep refusing where a store cannot match; tests red before.

## Out of scope

- The other rule leftovers (TASK-916).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
