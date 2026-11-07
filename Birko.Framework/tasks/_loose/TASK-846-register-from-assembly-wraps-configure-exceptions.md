---
id: TASK-846
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: in-progress
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

- [x] Reproduced: a mapping whose `Configure` throws, registered through `RegisterFromAssembly`, reaches the caller as
      `TargetInvocationException`.
      → Reproduced 2026-11-07 with TASK-845's `NotSupportedException` (`x => x.Code.Length`) and with a plain
        `ArgumentException`. The mapping throws only on the test's own flow (an `AsyncLocal` switch), because other
        tests scan the same assembly in parallel.
- [ ] The inner exception is rethrown with its own type and stack (`ExceptionDispatchInfo.Capture(...).Throw()`), and
      the message names the mapping type. Every other path through `Register` stays as it is.
      — ⚠ PARTLY MET:
      - **Met:** `RegisterFromAssembly` alone catches `TargetInvocationException` around `Configure` and rethrows the
        inner exception with `ExceptionDispatchInfo.Capture(inner).Throw()`, keeping its type and stack. A mapping
        that throws registers nothing; mappings scanned before it stay registered, as before. `Register<T>` is
        unchanged.
      - **Not met as written:** "the message names the mapping type". `Exception.Message` cannot change without
        wrapping, and rule 9 says not to wrap where wrapping earns nothing. The mapping's full type name goes on
        `Exception.Data[ModelMapRegistry.MappingTypeDataKey]` (new public constant), as rule 9's precedent does for
        command text. TASK-845's own message already names the model type.
- [x] Regression test red before the fix; the Birko.Models.SQL importer suites stay green.
      → `Task846RegisterFromAssemblyExceptionTests`: with only the constant added, 2 of 3 failed ("found
        TargetInvocationException"). With `throw thrown;` in place of the dispatch, the stack test failed: the top frame
        became `RegisterFromAssembly`.
      → All 29 importer suites green (Birko.Models.SQL.Tests 71).
      → **Found, filed:** `ViewMapRegistry` wraps the same way, and `Activator.CreateInstance` of a throwing mapping
        constructor wraps in both registries → TASK-847.

## Out of scope

- What `Configure` refuses (TASK-845).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
