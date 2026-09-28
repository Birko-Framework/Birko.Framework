---
id: TASK-505
parent: null
feature: null
status: done  # 2026-09-28: shipped with consumer Symbio TASK-807
priority: P3
assignee: ai
created: 2026-09-28
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# `ISluggable` cannot reserve a slug that no row holds — a literal route beside `{slug}` shadows it

## Context

Requested by consumer Symbio, TASK-807. `GET /api/products/public/{slug}` shares its segment with the literal
routes `/facets` and `/categories`, and endpoint routing ranks a literal above a parameter. A product named
"Facets" got the slug `facets` from the sluggable wrapper and could never be fetched by slug: the storefront got
the facet list back, with a `200`. The wrapper only knew "taken by another row", so an entity had no way to say
"this word is never mine".

## Change

- `ISluggable.IsReservedSlug(string slug)` — a **default interface member** returning `false`, so every existing
  implementer is unchanged. It receives the NORMALIZED slug.
- `SluggableStoreWrapper` / `AsyncSluggableStoreWrapper` (and so both bulk variants, which call the same
  `ResolveSlug*`) treat a reserved slug exactly like a taken one: `facets` → `facets-2`. De-duplicate, not refuse —
  the same answer a merchant already gets for a name another product holds.

## Acceptance criteria

- [x] A reserved slug is de-duplicated on create (from the source and from an explicit slug) and on update.
- [x] A model that reserves nothing keeps its slug.
- [x] Mutation-proven: removing the check in the sync wrapper turns the three reservation tests red
      (`SluggableStoreWrapperTests`); the async path is covered and mutation-proven by Symbio's
      `PublicProductRouteSegmentsTests` on real SQLite (4 red with the check removed).

## Human test plan

N/A — a store-wrapper rule, asserted by unit tests on both paths.
