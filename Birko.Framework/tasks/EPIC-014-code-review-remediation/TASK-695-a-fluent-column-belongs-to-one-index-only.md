---
id: TASK-695
parent: EPIC-014
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
priority: P2
assignee: ai
created: 2026-10-20
depends-on: []
blocks: [TASK-693]
findings: []
pr: null
github-issue: null
jira-key: null
---

# A fluent column can belong to one index only, and a second `HasIndex` silently replaces the first

## Context

Split from [[TASK-693]] at plan time, 2026-10-20 (decided by the user). `FieldDescriptor` holds one `IndexName` /
`IndexOrder` / `IndexDescending` / `IndexUnique`. `FieldBuilder.HasIndex` overwrites them, so:

- A column in two composite indexes cannot be declared. Grants.SQL needs `ResourceType` in both
  `(UserGuid, ResourceType)` and `(ResourceType, ResourceId)`.
- A second `HasIndex` on one property is accepted and drops the first index silently (rule 55: a builder honours a
  declaration or refuses it).
- `HasUniqueIndex("A")` then `HasIndex("B")` leaves `IndexUnique` set, so B is made UNIQUE.

## Acceptance criteria

- [x] A property can carry several fluent indexes. Each keeps its own order, direction and uniqueness, and
      `ApplyToDatabase` writes every one into `Table.Indexes` (columns marked `IsIndexed`, rule 37).
- [x] The same index name twice on one property is refused with `InvalidOperationException`.
- [x] Uniqueness belongs to the index it was declared on, never to a later one.
- [x] The existing single-index readers (`IndexName`, `IndexOrder`, …) keep reading the first index; the existing
      mapping suites stay green.

## Out of scope

- Grants.SQL's indexes: [[TASK-693]].

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan

1. `Birko.Data.Patterns.Schema.FieldDescriptor`: new `Indexes` (list of `FieldIndex`: name, order, descending,
   unique). `IndexName` / `IndexOrder` / `IndexDescending` / `IndexUnique` become read-only views of the first entry.
2. `FieldBuilder.HasIndex` appends, refusing a name already on the property; `HasUniqueIndex` appends a unique entry.
3. `ModelMapRegistry.ApplyIndexes` groups `(property, index)` pairs by name. The mixed-uniqueness refusal and the
   unmapped-property refusal stay.
4. Tests in Birko.Models.SQL.Tests (one column in two composite indexes, its column order in each, refusal, uniqueness
   kept per index, the SQL emitted on SQLite). Mutations. Regression: every test project importing Birko.Models.SQL.

## Outcome

Done 2026-10-20.
- `FieldDescriptor.Indexes` (a list of `FieldIndex`: name, order, descending, unique). `HasIndex` / `HasUniqueIndex`
  append to it. The same name twice on one column throws `InvalidOperationException`, and a blank name throws
  `ArgumentException`.
- `ApplyIndexes` groups `(property, index)` pairs, so each index keeps the column's own order and its own uniqueness.
- `IndexName` / `IndexOrder` / `IndexDescending` / `IndexUnique` are read-only views of the first index. Nothing set
  them outside `FieldBuilder`.

**Tests:** `FluentMultipleIndexTests` (7):
- a column in two composite indexes, with its position in each;
- uniqueness kept per index;
- the shared column is `IsIndexed` (rule 37);
- the refusals;
- the first-index view;
- on SQLite, both indexes are created and the unique one refuses a duplicate.

Birko.Models.SQL.Tests now imports the SQLite stack.

**Mutations, all red:**
- second call replaces the first (2 red);
- duplicate accepted;
- uniqueness read from the first index (2 red);
- order read from the first index (red after asserting the order value itself; a tie had hidden it);
- `IsIndexed` not set;
- blank name accepted.

**Regression:** all 29 test projects importing Birko.Models.SQL, live where gated. Every one is green except
Grants.SQL's two new TASK-693 measurement tests, which are uncommitted and land with TASK-693. The solution builds with
no new warnings.
