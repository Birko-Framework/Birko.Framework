---
id: TASK-899
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-10
depends-on: [TASK-896]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Cosmos DB filters: leftovers after TASK-896

## Context

Found by TASK-896 (2026-11-10):
- **One translator for store and view:** `CosmosViewStore` still renders its own SQL. Moving it onto the LINQ provider
  needs a `TView` → source lambda rewrite (renamed and nested paths, TASK-843), the `root` alias, and replacing
  TASK-839's single-parameter `ARRAY_CONTAINS`. A refactor; null handling is already shared.
- **A non-nullable member missing from a document:** C# reads the type's default, Cosmos treats it as undefined. Not
  covered.
- **A `Contains` list holding null** is now `null OR IN(..)`, which cannot be chunked, so past ~13k values it is refused
  rather than split.
- **Nested lambdas** (`Any` / `All` predicates) with nullable members were not measured.
- **View-store aggregates** (TASK-897, measured): decimal SUM/AVG are read through the serializer's double (0.1 + 0.2 =
  0.30000000000000004; 1/3 with 16 digits), and a DateTime GROUP BY groups the stored text, so a local and an
  unspecified time with equal ticks are two groups (C#: one). The store fixed both; the view needs a post-processing
  pass or a refusal, and the grouping change touches `ResolveOrderKey`.
- Query cost / index use of `IS_DEFINED` / `IS_NULL` was not measured on a real account.

## Acceptance criteria

- [ ] Measure each (rule 54).
- [ ] Fix or record each; decide the single-translator refactor on its cost.
- [ ] Tests, red before each fix.

## Out of scope

- Null semantics in flat filters (TASK-896, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
