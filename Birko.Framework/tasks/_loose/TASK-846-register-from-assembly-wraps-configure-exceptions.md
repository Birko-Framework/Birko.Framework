---
id: TASK-846
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
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
- [x] The inner exception is rethrown with its own type and stack (`ExceptionDispatchInfo.Capture(...).Throw()`), and
      the message names the mapping type. Every other path through `Register` stays as it is.
      → **The two halves conflict:** the message cannot name the mapping without wrapping, and a wrap changes the type.
        - The first build (commit 12560fa9) kept the type and put the mapping on `Exception.Data`.
        - **The user decided (2026-11-07) to wrap instead.**
      → `RegisterFromAssembly` alone catches `TargetInvocationException` around `Configure` and throws
        `InvalidOperationException("Model mapping '<full type>' failed in Configure: <original message>", original)`.
        The original keeps its type and stack as `InnerException`, and the `Data` key is removed.
      → A mapping that throws registers nothing; mappings scanned before it stay registered. `Register<T>` still lets
        the exception through as itself.
- [x] Regression test red before the fix; the Birko.Models.SQL importer suites stay green.
      → `Task846RegisterFromAssemblyExceptionTests`, now asserting the wrap: the message names the mapping and the
        original; the inner exception has its type, and its stack reaches the throw site.
        - On the original code, the tests were red (`TargetInvocationException`).
        - On the first build (dispatch rethrow), 2 of 3 were red.
      → Birko.Models.SQL.Tests 71 green. The first build's 29-suite sweep was green, and this change touches only the
        scan's catch block.
      → **Found, filed:** `ViewMapRegistry` wraps the same way, and `Activator.CreateInstance` of a throwing mapping
        constructor wraps in both registries → TASK-847.

## Out of scope

- What `Configure` refuses (TASK-845).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
