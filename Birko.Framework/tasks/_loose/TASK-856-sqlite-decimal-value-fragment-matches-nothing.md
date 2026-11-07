---
id: TASK-856
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P2
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

# On SQLite, a decimal value fragment in IN or `=` matches nothing

## Context

Found by TASK-849 (2026-11-07), measured on SQLite. `small.Contains(x.Amount + 0m)` counted 0 rows where 5 match. A
decimal computed operand (a TASK-213 value fragment) compared with decimal parameters silently returns the wrong
answer.
- SQLite stores decimals through Birko's decimal handling (`SqLiteDecimal`), likely as text with a custom comparison.
- An arithmetic fragment yields a REAL, or a differently formatted value, that never equals the bound text.
- The answer is wrong and nothing is raised (rules 26 and 44).

## Acceptance criteria

- [ ] Reproduced first: `Contains` and `==` with decimal value fragments (`x.Amount + 0m`, `x.Amount * 1m`,
      `Math.Round(...)`) against decimal parameters, on SQLite. Record what matches against the C# oracle. Check the
      other providers for the same shapes.
- [ ] Either the comparison is made exact (both sides compared through Birko's decimal comparison), or a decimal value
      fragment that SQLite cannot compare exactly is refused by name before the statement runs. Never a wrong count.
- [ ] Regression tests red before the fix; the SQLite and Birko.Data.SQL importer suites stay green.

## Out of scope

- Decimal storage itself (TASK-826 established exact storage).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
