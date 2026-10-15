---
id: TASK-626
parent: STORY-053
feature: FEATURE-014
status: todo
priority: P2
assignee: ai
created: 2026-10-15
depends-on: []
blocks: []
findings: [SH-M147, SH-M148]
pr: null
github-issue: null
jira-key: null
---

# A CROSS join with conditions loses its ON clause, and a join with no conditions disappears

## Context

Spawned by [[TASK-153]] (2026-10-15), which confirmed both findings by reading `AbstractConnectorBase`'s join
rendering and left them unfixed because the fix changes what existing joins render.

- **SH-M147.** `if (joingroup.Key.JoinType != JoinType.Cross && … Any())` renders ` ON (…)` only for non-cross joins,
  so a `JoinType.Cross` group's conditions are discarded. `Cross` is the **default** of both `Join.Create` overloads and
  the `Join` constructor, so a caller who supplies conditions and forgets the join type gets a cartesian product.
- **SH-M148.** `joingroups.Where(x => x.Value.Any())` skips a group with no conditions, so a conditionless CROSS JOIN,
  the only way to express one, vanishes from the FROM clause, and an INNER / LEFT join whose conditions failed to
  translate vanishes the same way.

## What to decide

1. A Cross join carrying conditions: refuse it, or treat the conditions as the ON of an INNER join? Refusing is the
   rule-42 answer; promoting changes the meaning of the declared join type.
2. Should the default join type stay `Cross`? A default that discards the caller's conditions is the trap itself.

## Acceptance criteria

- [ ] A Cross join with conditions never renders without them: refused, or rendered with them, per the decision
- [ ] A conditionless Cross join is emitted, measured against a real database (row count of the product)
- [ ] An INNER / LEFT join whose conditions are empty is refused rather than dropped
- [ ] Regression tests red before, green after, on SQLite and at least one server provider

## Human test plan

N/A: asserted by row counts.
