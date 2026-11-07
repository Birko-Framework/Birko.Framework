---
id: TASK-857
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P2
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-07
depends-on: [TASK-856]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# The SQL filter parser mistranslates `Math.Round`, `Math.Abs` and unary minus, on every provider

## Context

Found by TASK-856 (2026-11-07), measured on SQLite, PostgreSQL, MySQL and SQL Server. Grouped, because all three are
the same producer: the SQL filter parser's value-fragment translation (`DataBase.ParseConditionExpression`, TASK-213
fragments).
- **`Math.Round(x.Amount, 1, MidpointRounding.ToEven) == 10.1m`** drops the Round and renders `Amount = 10.1`: 3 rows
  expected, 2 returned on every provider. Silent.
- **`Math.Abs(x.Amount - 10m) < 1m`** returns 0 rows instead of 3. Silent.
- **`-x.Amount == -7m`** throws "Condition name cannot be null". Loud, but only by accident.

A translator that cannot express a node must refuse it (rules 41–44), and the first two violate that.

## Acceptance criteria

- [ ] Each shape reproduced per provider against the C# oracle, together with any other `Math.*` or unary operator
      the parser accepts. Record what each returns.
- [ ] Each is translated exactly (`Round` with an explicit mode as TASK-826's `Compute` does, `ABS`, unary minus), or
      refused by name with `NotSupportedException` before the statement runs. Never a wrong count. On SQLite, decimal
      operands go through TASK-856's fragment dialect.
- [ ] Regression tests per shape and provider (live where available), red before the fix.

## Out of scope

- The SQLite decimal comparison itself (TASK-856).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
