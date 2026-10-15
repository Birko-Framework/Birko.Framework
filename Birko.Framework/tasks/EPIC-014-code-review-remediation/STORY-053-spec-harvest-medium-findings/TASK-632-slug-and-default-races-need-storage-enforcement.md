---
id: TASK-632
parent: STORY-053
feature: FEATURE-014
status: todo
priority: P2
assignee: ai
created: 2026-10-15
depends-on: []
blocks: []
findings: [SH-M327, SH-M328]
pr: null
github-issue: null
jira-key: null
---

# Concurrent writers defeat the unique-slug and single-default decorators, which only read then write

## Context

Spawned by [[TASK-159]] (2026-10-15), which confirmed both findings by reading. TASK-159 fixed the single-caller defects
of these decorators (the slug probe, the batch's own slugs, the default rule on filtered and property updates); what is
left is the race, and no decorator can close it.

- **SH-M327:** the slug decorators probe `Slug == s` and then write. Two callers creating "widget" at once both see it
  free and both store it.
- **SH-M328:** the default decorators read the defaults, unset them, then write their own. Two callers promoting two rows
  at once both clear the same set and both store a default.

A read-then-write in a decorator narrows the window and cannot close it: the invariant needs the storage to enforce it.

## What to decide

1. **Slug:** a unique index on the slug column, declared by the model (`[UniqueField]` / `[CompositeIndex]` with a tenant
   column where the model is tenant-scoped), and the decorator turning the duplicate-key error into a retry with the next
   suffix. Soft-deletable models need it scoped to live rows: `WhereNull = DeletedAt` (rule 58, TASK-273) expresses that.
2. **Default:** "at most one row with `IsDefault = true`" is a partial unique index over a **boolean value**, which
   `WhereNotNull` / `WhereNull` cannot express (rule 58 deferred the general predicate as an uncontainable sink). Options:
   a nullable marker column (`DefaultMarker` = 1 or null) under `WhereNotNull`, a provider-native constraint, or an
   explicit "best effort" contract on the decorator. Measure each provider before choosing (rule 58: each polarity is
   answered separately).
3. Non-SQL backends (InMemory, JSON, XML, Mongo, Raven, ES, Cosmos): which can enforce, and what the decorator documents
   where none can.

## Acceptance criteria

- [ ] Two concurrent creates of one slug source end with distinct slugs or one refusal, never two equal slugs, measured
      on SQLite and one server
- [ ] Two concurrent promotions end with one default, measured the same way
- [ ] Where a backend cannot enforce, the decorator says so in its contract
- [ ] Red on the current code

## Human test plan

N/A: a library contract, asserted by tests.
