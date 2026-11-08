---
id: TASK-869
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P2
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-08
depends-on: [TASK-860]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# A SQL `Contains` / `StartsWith` / `EndsWith` follows the column collation, not C#'s comparison

## Context

Found by TASK-860 (2026-11-08). The LIKE family ignores `StringComparison` and follows the column's collation:
- SQLite, MySQL and SQL Server match case-insensitively by default, where C#'s `x.Name.Contains("ab")` is
  case-sensitive (ordinal).
- PostgreSQL is case-sensitive.

So the same filter returns different rows per provider, and differs from C# on three of them. TASK-860 deliberately
left this alone, because Symbio's search depends on case-insensitive matching (85 uses of `ToLower().Contains`, plus
plain `Contains`).

## Acceptance criteria

- [ ] Measure per provider (live): `Contains`, `StartsWith` and `EndsWith`, each without a comparison, with `Ordinal`
      and with `OrdinalIgnoreCase`, plus `ToLower().Contains(term.ToLower())`, on mixed-case and non-ASCII data.
      Record each against C#.
- [ ] **Decide with the user**: make the no-argument form C#-exact (case-sensitive everywhere, which breaks
      Symbio's search unless it moves to `OrdinalIgnoreCase` or `ToLower`), or keep it collation-following and
      document it. Either way, translate the explicit `Ordinal` and `OrdinalIgnoreCase` exactly, or refuse them.
- [ ] Tests per provider, red before the fix.

## Out of scope

- Other calls (TASK-860, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
