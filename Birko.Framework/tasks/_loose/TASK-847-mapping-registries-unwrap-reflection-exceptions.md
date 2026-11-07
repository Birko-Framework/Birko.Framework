---
id: TASK-847
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-07
depends-on: [TASK-846]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# The view mapping registry, and a throwing mapping constructor, hand back `TargetInvocationException`

## Context

Found by TASK-846 (2026-11-07). TASK-846 fixed `ModelMapRegistry.RegisterFromAssembly`'s `Configure` call. Two more
reflection call sites wrap a mapping's own exception the same way:
- `Birko.Data.Views/ViewMapRegistry.cs`, its `Configure` and `Build` calls (around lines 51 and 55).
- `Activator.CreateInstance` of a mapping whose constructor throws, in both registries.

## Acceptance criteria

- [ ] Each site reproduced: the caller receives `TargetInvocationException`.
- [ ] Each rethrows the inner exception with its own type and stack, the same way TASK-846 does, with the mapping type
      on `Exception.Data` (rule 9). One shared helper if both projects can reach one without a new reference
      (CLAUDE-projects.md § Dependency Flow); otherwise two small copies, saying why.
- [ ] Regression tests red before the fix; the importer suites of both projects stay green.

## Out of scope

- `ModelMapRegistry`'s `Configure` call (TASK-846, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
