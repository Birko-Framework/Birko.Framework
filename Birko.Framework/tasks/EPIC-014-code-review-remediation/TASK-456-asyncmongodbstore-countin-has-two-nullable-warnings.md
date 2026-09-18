---
id: TASK-456
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P3
assignee: ai
created: 2026-09-17
depends-on: []
blocks: []
# findings: ids this task remediates, from a review/audit/spec-harvest pass (CR-* SEC-* SH-* VC-*)
findings: []
pr: null
github-issue: null
jira-key: null
---

# `AsyncMongoDBStore.CountIn` dereferences a nullable `Collection` — two CS8602 warnings

## Context

Spawned by [[TASK-314]]'s close gate (`/tasks close` step 5d), found while building
`Birko.Data.Migrations.MongoDB.Tests` — which imports `Birko.Data.MongoDB` — with
`--no-incremental`:

```
Birko.Data.MongoDB/Stores/AsyncMongoDBStore.cs(113,19): warning CS8602: Dereference of a possibly null reference.
Birko.Data.MongoDB/Stores/AsyncMongoDBStore.cs(114,19): warning CS8602: Dereference of a possibly null reference.
```

Both are the two arms of `CountIn`:

```csharp
private Task<long> CountIn(FilterDefinition<T> filter, CancellationToken ct)
    => TransactionContext != null
        ? Collection.CountDocumentsAsync(TransactionContext, filter, null, ct)
        : Collection.CountDocumentsAsync(filter, null, ct);
```

`Collection` is nullable (it is populated by `SetSettings`, so it is null until the store is
configured), and this expression-bodied member dereferences it unguarded.

**Pre-existing and unrelated to TASK-314** — that task touched `Birko.Data.Migrations.MongoDB`, not
`Birko.Data.MongoDB`, and the warnings are visible from it only because the migration test project
imports the store project. It is recorded here rather than fixed there because a defect fix must not
quietly widen into a neighbouring project.

**Why it matters despite being only a warning.** `CLAUDE.md` § Code Style states plainly: *"All new
code must compile without CS8600–CS8605, CS8618, CS8625."* These two are the only ones reachable from
the migrations suites, so they are a standing exception to a rule the convention gate enforces on
everything else — and a build with known-acceptable warnings is one where a *new* warning is easy to
miss.

**Priority P3** because nothing misbehaves: every path that reaches `CountIn` has already been through
`SetSettings`, so `Collection` is in practice non-null. This is rulebook hygiene, not a live defect —
but it should be established by measurement rather than assumed (see criterion 1).

## Acceptance criteria

- [ ] Establish whether `Collection` can actually be null at `CountIn` — i.e. whether any public entry
      point reaches it without `SetSettings` having run. Record the answer; it decides which of the two
      fixes below is correct, and "it is fine in practice" is a claim to verify, not to assert.
- [ ] If it genuinely cannot be null, the fix is an assertion the compiler can read (a
      `?? throw`-backed property, or a null-forgiving `!` **with the reason written beside it**) — never
      a bare `!`, which records nothing and is indistinguishable from not having thought about it.
- [ ] If it can be null, the fix is a guard that fails with a message naming `SetSettings` as the door
      the caller missed (§ SH-H037: a refusal names the door this caller has).
- [ ] `dotnet build --no-incremental` on `Birko.Data.MongoDB.Tests` and
      `Birko.Data.Migrations.MongoDB.Tests` reports **0** warnings.
- [ ] Check the sibling members for the same shape before closing — `FindIn` is named in `CountIn`'s own
      doc comment and any other `*In` helper on the same class is a candidate. § TASK-215's *guard the
      whole verb family or none of it*: fixing the two lines a build happened to surface, while an
      identical third sits beside them, is the half-fix that rule names.
- [ ] `Birko.Data.MongoDB.Tests` stays green.

## Out of scope

- Any behavioural change to `AsyncMongoDBStore`. This is a nullable-annotation fix; if the
  investigation in criterion 1 turns up a real null-dereference path, that is a **defect** and gets its
  own task rather than being folded in here.
- Nullable warnings elsewhere in `Birko.Data.MongoDB` that these suites do not surface. If the build
  reveals more once these two are fixed, say so and decide then whether they belong here or in a
  project-wide sweep.

## Human test plan

`N/A — fully covered by automated tests.` The acceptance is a warning count from the compiler and a
green suite; a human adds nothing either cannot assert.

## Implementation plan

_Populated by `/tasks plan TASK-456` — leave empty until then._
