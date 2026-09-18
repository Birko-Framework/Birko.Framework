---
id: TASK-463
parent: null
feature: null
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: ai
created: 2026-09-18
depends-on: []
blocks: []
related: [TASK-461]
findings: []
pr: null
github-issue: null
jira-key: null
---

# `TagServiceBase` is documented in three places and exists in none

## Context

Found while scoping [[TASK-461]]'s tagging coverage. `Birko.Data.Tagging` ships four files —
`ITaggable`, `Tag`, `EntityTag`, `ITagService`, plus `CrossTenantTagAccessException` and a DI
extension. There is **no `TagServiceBase`**. Measured: `grep -rl TagServiceBase --include=*.cs`
returns **0 files**; all six references are documentation.

Three documents promise it, one of them the framework's own contract sheet:

| Where | What it says |
|---|---|
| `Birko.Framework/CLAUDE.md` § Dependency Flow | lists the project's contents as `… ITagService, TagServiceBase` |
| `Birko.Data.Tagging/CLAUDE.md:15` | *"**TagServiceBase** — Template Method base class: concrete business logic (deduplication, idempotent attach, reconciliation) with abstract data access methods"* |
| `Birko.Data.Tagging/README.md:27-39` | a worked `public class SqlTagService : TagServiceBase` example, and a **tenant-scoping contract** stated as *"`TagServiceBase` stamps `TenantGuid` on every insert"* |

This is § TASK-263's *named an escape hatch that did not open*, and the tenant sentence makes it
worse than a missing convenience: a reader implementing `ITagService` from the README believes tenant
stamping is inherited, and it is not. Every implementation would have to stamp `TenantGuid` itself,
with nothing saying so and `CrossTenantTagAccessException` sitting there implying somebody checks.

## The decision this needs first

**Write the class, or delete the promise.** Both are defensible and they are not the same task:

- *Write it* — a Template Method base with the deduplication / idempotent-attach / reconciliation
  logic the docs describe and abstract data access, which is what makes the tenant contract real and
  gives `CrossTenantTagAccessException` a thrower.
- *Delete the promise* — if per-backend tag services are the intended design, the three documents
  must say `ITagService` and spell out that **the implementer** owns tenant stamping, because that
  obligation currently reads as somebody else's.

Measure the blast radius before choosing: **0** `.cs` references anywhere, so either is free today.

## Acceptance

1. One of the two paths is taken, and the reason is recorded — a gap that is a decision reads exactly
   like an oversight unless it says so (§ TASK-263).
2. Whichever path: the **tenant-stamping obligation** is stated where the implementer meets it, and
   is asserted by a test rather than described.
3. `Birko.Framework/CLAUDE.md` § Dependency Flow agrees with the tree, and the three documents agree
   with each other.
4. If the class is written: `Birko.Sandbox` gains a tagging check, which is the coverage
   [[TASK-461]] deliberately left out rather than faking with an invented implementation.
