---
id: TASK-847
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
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

- [x] Each site reproduced: the caller receives `TargetInvocationException`.
      → All 4 sites reproduced on 2026-11-07: `ViewMapRegistry`'s mapping constructor, `Configure`, and `Build` (a
        mapping that skips `From<TSource>()`), and `ModelMapRegistry`'s mapping constructor.
- [x] Each does what TASK-846 settled (the user's decision, 2026-11-07): `InvalidOperationException` whose message names
      the mapping type and repeats the original message, with the original as `InnerException`, keeping its type and
      stack. One shared helper if both projects can reach one without a new reference (CLAUDE-projects.md § Dependency
      Flow); otherwise two small copies, saying why.
      → Message: `"View|Model mapping '<FullName>' failed in {its constructor|Configure|Build}: <original>"`.
        `Register<T>` still lets exceptions through as themselves.
      → **Two small copies** (a private `Invoke(mappingType, stage, step)` per registry): the projects share no direct
        reference. Models.SQL declares Data.Patterns and Data.SQL, while Views declares Data.Core and Data.Stores, and
        reaching Data.Core from Models.SQL would add a new edge. Both doc comments say why.
      → TASK-846's `Configure` catch moved into the helper, its message unchanged.
- [x] Regression tests red before the fix; the importer suites of both projects stay green.
      → `Task847RegisterFromAssemblyExceptionTests` (Views) and `Task847MappingConstructorExceptionTests`
        (Models.SQL), faults switched on per test with `AsyncLocal`.
      → Red proof, with the catch filters disabled: Views 3 of 53 and Models.SQL 3 of 73 failed, the TASK-846 tests
        included.
      → All 34 projects importing either projitems are green. No live database hosts were set; the change has no
        database path.
      → **Decided not to do:** a mapping with no parameterless constructor still throws `MissingMethodException`.
        That is not a reflection wrapper, and its message already names the type and the missing constructor, so a
        wrap adds nothing.

## Out of scope

- `ModelMapRegistry`'s `Configure` call (TASK-846, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
