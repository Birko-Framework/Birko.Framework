---
id: TASK-299
parent: EPIC-014
feature: FEATURE-014
status: todo
priority: P3
assignee: ai
created: 2026-09-07
depends-on: []
blocks: []
related: [TASK-264, TASK-274, TASK-275]
findings: []
pr: null
github-issue: null
jira-key: null
---

# `FieldDescriptor`'s three index properties are read by nothing, in any backend

## Context — measured at TASK-264's Step 0

`FieldDescriptor` declares `IndexName`, `IndexOrder` and `IndexDescending`, so a field appears to be able
to declare its own index inline. Measured 2026-09-07: the only references to those three names in the
whole framework are **their own declarations**. Nothing reads them — not `SchemaField`, not
`SqlSchemaBuilder`, not any other backend's schema builder.

So a migration written as

```csharp
.WithField(new FieldDescriptor { Name = "Code", Type = FieldType.String,
                                 MaxLength = 32, IndexName = "ix_code" })
```

produces the column and **no index**, silently. The author must call `CreateIndex(...)` separately, which
is the only path that works.

TASK-264 fixed the descriptor properties whose consequence was a wrong column and left these, because an
index the framework never emits is a missing feature rather than a dropped assignment.

## Why it matters

Lower severity than its siblings — nothing is silently *wrong*, an index is merely absent — but it is the
same silent-drop family as TASK-274 (`Sparse()` and `WithProperty()` were `=> this` in all six schema
builders) and TASK-246 (`SqlIndexBuilder.Build()` dropped `Unique`). Its own bullet in TASK-274's rule:
**a builder whose every method returns `this` has a silent option at every step — so it must honour a
declaration or refuse it, never accept one and do nothing.** These are three properties on a DTO rather
than fluent methods, but the failure is identical.

There is a second reason to settle it: TASK-264 could not set `AbstractField.IsIndexed` because
`SqlCollectionBuilder` and `SqlIndexBuilder` are separate builders with no shared state, so at
`CREATE TABLE` time nothing knows an index is coming. **An inline `IndexName` is the one shape where that
information IS available at column time** — which would let the column be auto-bounded the way
`DataBase.LoadIndexes` does for attribute-mapped entities, instead of relying on the author to declare a
length. That makes honouring these properties worth more than it first looks.

## What to decide

1. **Honour them** — group descriptors by `IndexName`, order by `IndexOrder`, respect `IndexDescending`,
   and emit the index after the `CREATE TABLE`. Then set `IsIndexed` on those fields so the per-provider
   bounding (TASK-257 MSSql, TASK-265 MySQL) applies, exactly as the attribute path gets it.
2. **Delete them** from `FieldDescriptor`. They are a public DTO's properties, so this is a breaking
   change — measure the blast radius first; TASK-247/259 measured 0 production consumers of
   `ISchemaBuilder`, but `FieldDescriptor` is used more widely than the builder.
3. **Document them as reserved.** The weakest option and the one that leaves the trap.

Option 1 is worth more than the others because of the `IsIndexed` consequence above.

## Acceptance criteria

- [ ] The three properties either produce an index, or are gone, or are refused when set — not accepted
      and ignored.
- [ ] If honouring: a field named by an inline `IndexName` is marked `IsIndexed`, so an unlengthed string
      is bounded on MSSql and MySQL and the index is buildable there. This is the case TASK-264 explicitly
      could not cover.
- [ ] Ordering is deterministic (`IndexOrder`, then declaration order) — the emitted statement is
      compared byte-for-byte, per TASK-273.
- [ ] Every backend's schema builder gives the same answer, or the divergence is asserted per backend so
      it reads as a decision (§ TASK-274 refused a compound `Sparse` rather than picking a meaning).
- [ ] Proven able to fail, with a named revert.

## Out of scope

- `DefaultValue` — TASK-298 owns it.
- The column-metadata properties — TASK-264, done.

## Human test plan

- [ ] N/A — mechanical; the proof is the index existing in each provider's catalogue.

## Implementation plan

_Populated by `/tasks plan TASK-299` — leave empty until then._
