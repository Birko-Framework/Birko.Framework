---
id: TASK-866
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-08
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# `Condition.IsOr` means two things in a flat condition list

## Context

Noticed by TASK-861 (2026-11-08) while writing a test; not shown to produce wrong rows yet.
- In a flat condition list, `Condition.IsOr` means "this term joins the previous one with OR".
- On a group, it means "this group's children are OR'd".
- A group in a flat list therefore carries both meanings on one flag. A hand-built condition, or a future parser
  path, can set it for one meaning and get the other. Rule 43 is the same family.

## Acceptance criteria

- [ ] Find every producer and reader of `IsOr` on a group in a list (parser, `RuleConditionConverter`, the chunk
      planner, `ConstantTruth`, the renderers). Record whether any shape the parser or the public API can produce
      renders a different meaning than intended. Reproduce before changing anything (rule 54).
- [ ] If one is reachable: separate the two meanings (for example a join flag and a children-combine flag), with
      tests red before the fix. If none is: record that and cancel, or add a guard test that pins the invariant.

## Out of scope

- The constant reduction itself (TASK-861, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
