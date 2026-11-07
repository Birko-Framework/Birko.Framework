---
id: TASK-860
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P1
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-07
depends-on: [TASK-857]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# The SQL filter parser silently drops method calls and casts it does not translate

## Context

Found by TASK-857 (2026-11-07). TASK-857 made every `Math.*` call either translate exactly or refuse. The same silent
dropping applies to other calls and to casts, and it was measured on SQLite, PostgreSQL, MySQL and SQL Server:
- `x.Code.Substring(1) == …` and `Convert.ToInt32(x.Amount) == …` drop the call and compare the bare column: wrong
  rows on every provider.
- `(decimal)x.Hits / 4m` drops the cast, so PostgreSQL and SQL Server do integer division: wrong rows.
- Update SET, from code reading only: `ParseExpression` returns null for a unary minus.

A translator that cannot express a node must refuse it (rules 41–44). Today the parser's default for an unknown call
or conversion is to keep the operand and lose the operation, which turns a filter into a different filter with no
error.

## Acceptance criteria

- [ ] Inventory every expression node kind the SQL filter parser (`DataBase.ParseConditionExpression` and the value
      fragment path) can meet: method calls, conversions, unary and binary operators, conditionals, member access,
      invocations. For each, record "translated exactly", "refused" or "silently dropped", per provider, measured
      against the C# oracle for a representative shape.
- [ ] Every "silently dropped" node becomes "translated exactly" (only where the translation is exact on every
      provider; numeric casts, for example, made explicit per provider) or "refused by name" with
      `NotSupportedException` before the statement runs. The parser's default for an unknown node is refusal, not
      pass-through.
- [ ] The same for update SET values (`ParseExpression`), including unary minus.
- [ ] A shared contract (like TASK-857's `MathFilterContract`) runs the inventory on all four providers, red before the
      fix. Every SQL importer suite stays green, and any consumer-visible refusal is listed as breaking.

## Out of scope

- `Math.*` (TASK-857, done).
- NULL semantics (TASK-859).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
