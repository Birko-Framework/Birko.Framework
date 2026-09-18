---
id: TASK-463
parent: null
feature: null
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: cancelled
priority: P2  # rating stands as filed; the premise, not the rating, was wrong
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

# ~~`TagServiceBase` is documented in three places and exists in none~~ — CANCELLED, the premise was false

## Context (as filed — every factual claim below is WRONG; see the cancellation at the end)

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


---

## Cancelled (2026-09-18) — the premise is false, and the error was mine

`TagServiceBase` **exists**: `Birko.Data.Tagging/Services/TagService.cs`, an `abstract class
TagServiceBase : ITagService` declaring **exactly the twelve hooks the README names**, stamping
`TenantGuid = GetCurrentTenantId()` on every insert, with `TagServiceBaseTests.cs`,
`TagServiceTenantGuardTests.cs` and a 139-line `InMemoryTagService` reference implementation — 20
tests, all green. Symbio's `SymbioTagService : TagServiceBase, ITagUsageQuery` derives from it in
production.

So there is nothing to write and nothing to delete. Every document that describes it is accurate,
including the tenant-scoping contract this task claimed was unbacked.

### How I got it wrong — two compounding errors, both mine

1. **A truncated listing read as a complete one.** `ls -R Birko.Data.Tagging | head -20` cut off
   inside the `Services:` block after `CrossTenantTagAccessException.cs` and `ITagService.cs`.
   `TagService.cs` was the next line. I read the truncation as the directory's contents.
2. **I misread my own count.** `grep -rl TagServiceBase --include=*.cs . | wc -l` printed **6** and I
   wrote *"exists in zero `.cs` files"* into the task, because the lines displayed under it were the
   `.md` hits from a second command with `head -5`. The number that mattered was on screen and said
   the opposite of what I filed.

**The rule worth keeping:** a count and a listing are two measurements, and when they disagree the
one that was truncated is the one to re-run. Neither `head` nor `wc -l` says it has hidden something.
The *filename* helped hide it too — the class is `TagServiceBase` and the file is `TagService.cs`, so
a listing does not name it.

This is the inverse of § TASK-283: that rule exists because a **stale** measurement kept a defect
open; here a **misread** one opened a defect that never existed. Both fail the same way — a claim
about the tree that nobody re-ran.

### What it changes elsewhere

- [[TASK-461]]'s out-of-scope bullet said tagging *"has no runnable implementation to check"* and used
  that to justify leaving it out of the Sandbox. Corrected there, and the check added, since the
  premise that excluded it was this one.
- § Dependency Flow in `CLAUDE.md` lists `TagServiceBase` and is **correct as written** — this task
  proposed changing it, which would have introduced the very drift it was filed to remove.
