---
id: TASK-846
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
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

# `RegisterFromAssembly` hands a mapping's own exception back wrapped in `TargetInvocationException`

## Context

Found by TASK-845 (2026-11-07); predates it. `ModelMapRegistry.RegisterFromAssembly` (Birko.Models.SQL) calls each
mapping's `Configure` through reflection. Any exception `Configure` throws reaches the caller wrapped in a
`TargetInvocationException`:
- TASK-845's `NotSupportedException` for a nested member;
- an `ArgumentException` for a non-member expression.

A caller filtering on the exception type misses it. The real message is one level down, so a log line that shows only
the outer message names reflection, not the mapping (rule 9: an exception's type is a contract).

## Acceptance criteria

- [ ] Reproduced: a mapping whose `Configure` throws, registered through `RegisterFromAssembly`, reaches the caller as
      `TargetInvocationException`.
- [ ] The inner exception is rethrown with its own type and stack (`ExceptionDispatchInfo.Capture(...).Throw()`), and
      the message names the mapping type. Every other path through `Register` stays as it is.
- [ ] Regression test red before the fix; the Birko.Models.SQL importer suites stay green.

## Out of scope

- What `Configure` refuses (TASK-845).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
