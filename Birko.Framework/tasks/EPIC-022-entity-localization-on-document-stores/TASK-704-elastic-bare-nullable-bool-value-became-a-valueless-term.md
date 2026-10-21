---
id: TASK-704
parent: EPIC-022
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
priority: P1
assignee: ai
created: 2026-10-21
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# ElasticSearch: a bare `x.Flag.Value` on a `bool?` became a term with no value (regression from TASK-703)

## Context

Found 2026-10-21 by TASK-696's spec harvest of `filter-expression-translation`, then measured.

[[TASK-703]] taught `ParseMember` to read `x.Nullable.Value` on a direct member as that member's field: a term or
terms query **field** for an operand (`guids.Contains(x.Guid.Value)`, `x.Guid.Value == g`). In predicate position a
bare `x => x.Flag.Value` on a `bool?` misses `ParsePredicate`'s bare-bool branch (only a direct member) and reached that
branch, so it answered with a `TermQuery` holding the field and **no value**. Measured: `x.Flag.Value`,
`!x.Flag.Value` and `x.Flag.Value && x.Name == "a"` all produced it. NEST treats a value-less term as conditionless and
drops it, so the clause vanished and the query widened (rules 3 and 42). Before TASK-703 this shape was refused.

## Acceptance criteria

- [x] A bare `x.Flag.Value` on a `bool?` member is `Flag == true`, also negated and inside `&&`.
- [x] A regression test that fails with the fix removed.

## Out of scope

- Other shapes the translator refuses.

## Human test plan

N/A — asserted by the suite.

## Outcome

Done 2026-10-21. `ParsePredicate` reads `x.Flag.Value` (a `bool?` direct member) as a term on `true`, as it reads a
bare `bool` member. **Test:** `TASK704_a_bare_nullable_bool_value_is_a_term_on_true_never_a_valueless_term` (the three
shapes; red with the fix removed). Birko.Data.ElasticSearch.Tests 306/306.
