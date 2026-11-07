---
id: TASK-859
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

# A SQL filter on a nullable column drops the NULL rows that C# matches

## Context

Found by the TASK-856 review (2026-11-07), measured on SQLite; expected on every SQL provider, because the cause is
the parser.
- `x.Optional != 8m` and `new decimal?[] { null, 10.1m }.Contains(x.Optional)` miss the rows where `Optional` is NULL,
  which C# matches: in C#, `null != 8` is true and `null` is in the list.
- SQL's three-valued logic makes `NULL <> 8` UNKNOWN, and `NULL IN (NULL, …)` is never true.
- The value-fragment forms (`x.Optional + 1m != 8m`) behave the same.
- TASK-213's tests already record this divergence (a NULL `Score` row excluded by SQL and counted by C#), but nothing
  ever scheduled a fix.

The answer is silently different from the C# expression it was translated from (rules 26 and 42).

## Acceptance criteria

- [ ] Measured per SQL provider (live where available), against the C# oracle: `!=` against a constant and against a
      nullable column, `Contains` with a list holding null, `!Contains`, and `==` null. Record each.
- [ ] Translated to C# semantics: `col <> v OR col IS NULL`; for a list with null, `col IN (…non-null) OR col IS NULL`;
      `NOT IN` likewise NULL-aware; column-vs-column with `IS [NOT] DISTINCT FROM` or the provider's equivalent. Or
      refused by name where a provider cannot express it. Decide with the user whether this is a breaking change
      worth a flag, since some callers may rely on SQL semantics.
- [ ] Regression tests per shape and provider, red before the fix. The TASK-213 tests that pin the old divergence are
      updated deliberately, not deleted.

## Out of scope

- Decimal fragment comparison on SQLite (TASK-856).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
